// Disposable test DB only: creates accounts/armies/matches, closes matches through API, retains test accounts.
import { randomUUID } from 'node:crypto';
import { performance } from 'node:perf_hooks';
import { defaultLineup, lineupPayload } from '../../frontend/src/api.js';
import { createMatchRefresh, mergeMatchSnapshot } from '../../frontend/src/matchSync.js';

const base = new URL(process.env.HERO_CHESS_SMOKE_URL || 'http://127.0.0.1:5197');
if (!['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname) && process.env.HERO_CHESS_ALLOW_REMOTE_LOAD !== 'YES')
  throw new Error('Remote load is disabled. Use a dedicated test environment and explicitly set HERO_CHESS_ALLOW_REMOTE_LOAD=YES.');
const number = (key, fallback, min, max) => {
  const value = Number(process.env[key] ?? fallback);
  if (!Number.isFinite(value) || value < min || value > max) throw new Error(`Invalid ${key}`);
  return value;
};
const users = number('LOAD_USERS', 4, 1, 100), seconds = number('LOAD_SECONDS', 30, 5, 1800);
const think = number('LOAD_THINK_MS', 5000, 500, 60000);
if (!Number.isInteger(users)) throw new Error('LOAD_USERS must be an integer.');
const mode = process.env.LOAD_MODE || 'bot', polling = process.env.LOAD_POLLING || 'optimized';
const difficulty = process.env.LOAD_DIFFICULTY || 'hard';
if (!['bot', 'pvp', 'mixed'].includes(mode) || !['legacy', 'optimized'].includes(polling) || !['easy', 'medium', 'hard'].includes(difficulty)) throw new Error('Invalid workload.');
if (mode === 'pvp' && users % 2) throw new Error('PvP needs an even LOAD_USERS.');
const clients = [], groups = [], samples = new Map(), botReplies = [], errors = [], cleanupErrors = [];
let measuring = false, stopped = false, deadline = Infinity, completedGames = 0;
process.on('SIGINT', () => { stopped = true; });
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const alive = () => !stopped && performance.now() < deadline;
async function api(client, path, method = 'GET', body, signal) {
  const started = performance.now(); let status = 0;
  try {
    const timeout = AbortSignal.timeout(10000);
    const request = () => fetch(new URL('/api/v1' + path, base), { method,
      headers: { 'Content-Type': 'application/json', ...(client?.token ? { Authorization: `Bearer ${client.token}` } : {}) },
      signal: signal ? AbortSignal.any([signal, timeout]) : timeout, body: body === undefined ? undefined : JSON.stringify(body) });
    let response = await request();
    // Account setup is outside measurement: respect auth quotas rather than disabling server protection.
    for (let attempt = 0; !client && path.startsWith('/auth/') && response.status === 429 && attempt < 5; attempt++) {
      const delay = Number(response.headers.get('Retry-After')) * 1000;
      if (!Number.isFinite(delay) || delay <= 0 || delay >= 10000) break;
      await response.arrayBuffer(); await sleep(delay);
      response = await request();
    }
    status = response.status;
    const value = await response.json().catch(() => null);
    if (!response.ok) throw Object.assign(new Error(value?.code || `${response.status}`), {
      status, retryAfterMs: Math.max(0, Number(response.headers.get('Retry-After')) * 1000) || 0 });
    return value;
  } finally {
    if (measuring) {
      const key = method + ' ' + path.replace(/[0-9a-f]{8}-[0-9a-f-]{27,}/gi, ':id');
      const list = samples.get(key) || []; list.push({ ms: performance.now() - started, status }); samples.set(key, list);
    }
  }
}
async function player() {
  const credentials = { email: `capacity-${randomUUID()}@hero.local`, password: 'Test!A1-' + randomUUID() };
  await api(null, '/auth/register', 'POST', credentials);
  const login = await api(null, '/auth/login?useCookies=false', 'POST', credentials);
  const client = { token: login.accessToken }; clients.push(client);
  const catalog = await api(client, '/catalog'), picks = defaultLineup(catalog);
  client.lineup = await api(client, '/lineups', 'POST', lineupPayload(catalog, 'Capacity test', picks.heroes, picks.skills));
  return client;
}
async function start(group) {
  for (const client of group.players) {
    const ticket = await api(client, '/matchmaking/tickets', 'POST', { mode: group.mode, ...(group.mode === 'bot' ? { botDifficulty: difficulty } : {}) });
    if (ticket.matchId) group.id = ticket.matchId;
  }
  if (!group.id) throw new Error('Matchmaking did not pair the test group.');
  for (const client of group.players) {
    await api(client, `/matches/${group.id}/selection`, 'PUT', { lineupId: client.lineup.id, expectedRevision: client.lineup.revision });
    await api(client, `/matches/${group.id}/confirm`, 'POST');
  }
  group.sessions = [];
  for (const client of group.players) {
    const session = { client, state: null, socket: null, ready: false, botSentAt: null, botVersion: null };
    group.sessions.push(session);
    const accept = next => {
      session.state = mergeMatchSnapshot(session.state, next, group.id);
      if (session.botSentAt != null && session.state.version > session.botVersion && session.state.sideToMove === session.side) {
        if (measuring) botReplies.push(performance.now() - session.botSentAt);
        session.botSentAt = null;
      }
    };
    session.sync = createMatchRefresh(signal => api(client, `/matches/${group.id}/state`, 'GET', undefined, signal), () => session.state, accept,
      error => { if (alive()) errors.push(error.message); });
    await session.sync.refresh();
    session.side = session.state.sides.find(side => side.isSelf).side;
    const { ticket } = await api(client, '/ws-ticket', 'POST', {});
    const url = new URL('/ws/v1', base); url.protocol = base.protocol === 'https:' ? 'wss:' : 'ws:'; url.searchParams.set('ticket', ticket);
    const socket = session.socket = new WebSocket(url);
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => { socket.close(); reject(new Error('Subscribe timeout')); }, 10000);
      socket.onopen = () => socket.send(JSON.stringify({ type: 'match.subscribe', payload: { matchId: group.id } }));
      socket.onerror = () => { clearTimeout(timeout); reject(new Error('WebSocket failed')); };
      socket.onclose = () => { session.ready = false; };
      socket.onmessage = event => {
        const message = JSON.parse(event.data);
        if (message.type === 'match.state') {
          if (polling === 'optimized') accept(message.payload);
          else session.sync.refresh().catch(error => errors.push(error.message));
          session.ready = true; clearTimeout(timeout); resolve();
        } else if (message.type === 'match.changed') session.sync.refresh({ version: message.payload.version }).catch(error => errors.push(error.message));
        else if (message.type === 'match.ended' || message.type === 'match.settled') session.sync.refresh().catch(error => errors.push(error.message));
      };
    });
    session.nextPoll = performance.now() + (polling === 'legacy' ? 1200 : 10000);
    session.nextMove = performance.now() + think * (.5 + Math.random());
  }
}
async function close(group) {
  for (const session of group.sessions || []) { session.socket?.close(); session.sync?.dispose(); }
  if (!group.id) return;
  const client = group.players[0];
  for (let i = 0; i < 3; i++) {
    try {
      const state = await api(client, `/matches/${group.id}/state`);
      if (state.status !== 'active') return;
      await api(client, `/matches/${group.id}/commands`, 'POST', { commandId: randomUUID(), expectedVersion: state.version, action: { type: 'resign' } }); return;
    } catch (error) { if (i === 2) cleanupErrors.push({ matchId: group.id, error: error.message }); else await sleep(100); }
  }
}
async function run(group) {
  try {
    while (alive()) {
      if (group.sessions.every(s => s.state.status !== 'active')) {
        completedGames++; await close(group); await start(group); continue;
      }
      await Promise.all(group.sessions.map(async s => {
        const now = performance.now();
        if (now >= s.nextPoll) {
          await s.sync.refresh();
          s.nextPoll = performance.now() + (polling === 'legacy' ? 1200 : s.ready ? 10000 : 2000) * (1 + Math.random() * .2);
        }
        if (s.state.status !== 'active' || s.state.sideToMove !== s.side || now < s.nextMove) return;
        const moves = await api(s.client, `/matches/${group.id}/legal-actions`);
        if (!moves.length) return;
        const move = moves[Math.floor(Math.random() * moves.length)], version = s.state.version;
        const action = { type: 'move', pieceId: move.pieceId, to: { x: move.toX, y: move.toY } };
        if (group.mode === 'bot') { s.botSentAt = performance.now(); s.botVersion = version + 1; }
        try {
          const reply = await api(s.client, `/matches/${group.id}/commands`, 'POST', { commandId: randomUUID(), expectedVersion: version, action });
          s.state = mergeMatchSnapshot(s.state, reply.snapshot, group.id);
        } catch (error) {
          s.botSentAt = null;
          if (error.status !== 409) throw error;
          await s.sync.refresh();
        }
        s.nextMove = performance.now() + think * (.5 + Math.random());
      }));
      await sleep(100);
    }
  } catch (error) { errors.push(error.message); stopped = true; }
}
const percentile = (values, p) => values.length ? Math.round([...values].sort((a, b) => a - b)[Math.ceil(values.length * p) - 1] * 100) / 100 : null;
let started;
try {
  for (let i = 0; i < users; i++) await player();
  for (let i = 0; i < clients.length;) {
    const pvp = (mode === 'pvp' || (mode === 'mixed' && groups.length % 2 === 0)) && i + 1 < clients.length;
    const group = { players: clients.slice(i, i + (pvp ? 2 : 1)), mode: pvp ? 'ranked' : 'bot' }; i += group.players.length;
    groups.push(group); await start(group);
  }
  started = performance.now(); deadline = started + seconds * 1000; measuring = true;
  console.log(JSON.stringify({ phase: 'measuring', users, matches: groups.length, mode, polling, difficulty, seconds, thinkMs: think }));
  await Promise.all(groups.map(run));
} catch (error) { errors.push(error.message); process.exitCode = 1; }
finally {
  measuring = false; stopped = true;
  const elapsed = started ? (performance.now() - started) / 1000 : 0;
  for (const group of groups) await close(group);
  const routes = Object.fromEntries([...samples].map(([key, values]) => [key, { requests: values.length,
    p50Ms: percentile(values.map(v => v.ms), .5), p95Ms: percentile(values.map(v => v.ms), .95), p99Ms: percentile(values.map(v => v.ms), .99),
    failures: values.filter(v => !v.status || v.status >= 500).length, rateLimited: values.filter(v => v.status === 429).length,
    conflicts: values.filter(v => v.status === 409).length }]));
  console.log(JSON.stringify({ users, mode, polling, elapsedSeconds: Math.round(elapsed * 100) / 100, completedGames, routes,
    botObservedReply: { count: botReplies.length, p95Ms: percentile(botReplies, .95) }, errors, cleanupErrors,
    note: 'Accounts/armies remain in the disposable DB. Client HTTP latency is not VM capacity or isolated bot search time.' }, null, 2));
  if (errors.length || cleanupErrors.length) process.exitCode = 1;
}
