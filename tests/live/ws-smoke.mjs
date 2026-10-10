// Vai trò file: Smoke test qua HTTP/WebSocket thật; cần API đang chạy và database Development có fixture. Tạo tài khoản/trận test.
// Run only against a disposable Development database; creates three accounts and test matches.
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
const base = process.env.HERO_CHESS_SMOKE_URL || 'http://127.0.0.1:5197';
const sockets = [];
// api: Gọi HTTP và trả status/body.
async function api(token, path, method = 'GET', body) {
  const response = await fetch(base + '/api/v1' + path, { method, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) }, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await response.text();
  const value = text ? JSON.parse(text) : null;
  return { status: response.status, value };
}
// ok: Gọi api rồi assert success.
async function ok(token, path, method = 'GET', body) {
  const r = await api(token, path, method, body);
  assert.ok(r.status >= 200 && r.status < 300, `${method} ${path}: ${r.status} ${JSON.stringify(r.value)}`); return r.value;
}
// player: Đăng ký/đăng nhập và tạo lineup fixture.
async function player() {
  const credentials = { email: `fix-smoke-${randomUUID()}@hero.local`, password: 'Review!A1-' + randomUUID() };
  await ok(null, '/auth/register', 'POST', credentials);
  const { accessToken: token } = await ok(null, '/auth/login?useCookies=false', 'POST', credentials);
  const catalog = await ok(token, '/catalog'), groups = {};
  for (const hero of catalog.heroes.filter(x => x.isOwned).sort((a, b) => Number(!a.code.startsWith('dev-slot-')) - Number(!b.code.startsWith('dev-slot-')) || a.code.localeCompare(b.code))) (groups[hero.classCode] ??= []).push(hero);
  const lineup = await ok(token, '/lineups', 'POST', { name: 'Review smoke', rulesetId: catalog.ruleset.id,
    entries: catalog.slots.map(s => ({ slotNo: s.slotNo, heroId: groups[s.classCode].shift().id, cosmeticId: null })),
    skills: catalog.teamSkills.slice(0, 3).map((x, i) => ({ slotNo: i + 1, skillId: x.id })) });
  return { token, lineup };
}
// connect: Xin vé, mở socket thật và cung cấp helper gửi/nhận message.
async function connect(token) {
  const { ticket } = await ok(token, '/ws-ticket', 'POST', {});
  const socket = new WebSocket(base.replace('http', 'ws') + '/ws/v1?ticket=' + encodeURIComponent(ticket)); sockets.push(socket);
  const messages = [], waiters = [];
  socket.addEventListener('message', event => {
    const value = JSON.parse(event.data); if (waiters.length) waiters.shift()(value); else messages.push(value);
  });
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  return { send: (id, command) => socket.send(JSON.stringify({ type: 'match.command', payload: { matchId: id, ...command } })),
    next: () => messages.length ? Promise.resolve(messages.shift()) : new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('WebSocket event timeout')), 5000);
      waiters.push(value => { clearTimeout(timeout); resolve(value); });
    }) };
}
try {
  const red = await player(), black = await player(), outsider = await player();
  await ok(red.token, '/matchmaking/tickets', 'POST', { mode: 'ranked' });
  const { matchId: id } = await ok(black.token, '/matchmaking/tickets', 'POST', { mode: 'ranked' });
  for (const p of [red, black]) {
    await ok(p.token, `/matches/${id}/selection`, 'PUT', { lineupId: p.lineup.id, expectedRevision: p.lineup.revision });
    await ok(p.token, `/matches/${id}/confirm`, 'POST');
  }
  const malformed = await api(red.token, `/matches/${id}/commands`, 'POST', { commandId: randomUUID(), expectedVersion: 0, action: [] });
  assert.equal(malformed.status, 400);
  const observer = await connect(outsider.token);
  const denied = { commandId: randomUUID(), expectedVersion: 0, action: { type: 'resign' } };
  observer.send(id, denied); assert.equal((await observer.next()).payload.code, 'MATCH_NOT_FOUND');
  const mover = await connect(red.token), moves = await ok(red.token, `/matches/${id}/legal-actions`), move = moves[0];
  const command = { commandId: randomUUID(), expectedVersion: 0, action: { type: 'move', pieceId: move.pieceId, to: { x: move.toX, y: move.toY } } };
  mover.send(id, command); assert.equal((await mover.next()).type, 'match.changed');
  assert.equal((await mover.next()).type, 'match.command_accepted');
  const retry = await connect(red.token); retry.send(id, command);
  const duplicate = await retry.next(); assert.equal(duplicate.payload.duplicate, true); assert.equal(duplicate.payload.sequenceNo, 1);
  await ok(black.token, `/matches/${id}/commands`, 'POST', { commandId: randomUUID(), expectedVersion: 1, action: { type: 'resign' } });
  observer.send(id, denied); assert.equal((await observer.next()).type, 'match.command_rejected');
  const replay = await ok(red.token, `/matches/${id}/replay?afterSequence=-1&limit=100`);
  assert.deepEqual(replay.entries.map(x => x.kind), ['start', 'move', 'resign']);
  const next = await ok(red.token, '/matchmaking/tickets', 'POST', { mode: 'bot' });
  await ok(red.token, `/matches/${next.matchId}/selection`, 'DELETE');
  console.log('PASS: real Kestrel + PostgreSQL + native WebSocket: outsider blocked, duplicate ACK, invalid action 400, replay, requeue and selection cancel.');
} finally { for (const socket of sockets) socket.close(); }
