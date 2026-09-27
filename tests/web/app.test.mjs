// Vai trò file: Node test chạy app.js trong VM với DOM/socket giả; kiểm tra race/reconnect/replay, không phải test trình duyệt thật.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { webcrypto } from 'node:crypto';

const source = readFileSync(new URL('../../HeroChess.Api/wwwroot/app.js', import.meta.url), 'utf8');
// app: Tạo harness với fake DOM, fetch và WebSocket rồi evaluate app.js.
function app() {
  const nodes = new Map();
  const node = () => ({ value: '', textContent: '', innerHTML: '', options: [], append() {}, classList: { add() {} } });
  const sockets = [];
  class Socket {
    static OPEN = 1;
    readyState = 1; sent = [];
    constructor() { sockets.push(this); }
    send(value) { this.sent.push(JSON.parse(value)); }
    close() { this.readyState = 3; }
    emit(type, payload) { return this.onmessage?.({ data: JSON.stringify({ type, payload }) }); }
  }
  const context = vm.createContext({
    document: { getElementById(id) { if (!nodes.has(id)) nodes.set(id, node()); return nodes.get(id); }, createElement: node },
    window: { addEventListener() {} }, location: { protocol: 'http:', host: 'localhost' },
    crypto: webcrypto, WebSocket: Socket, setInterval() {}, setTimeout() { return 1; }, clearTimeout() {},
    fetch() { throw new Error('Unexpected real fetch'); }
  });
  vm.runInContext(source, context);
  vm.runInContext(`renderBoard = renderSkills = buildUndoTargets = tickClock = () => {};
    token = 'test'; api = async () => ({ticket:'test-ticket'});`, context);
  const run = code => vm.runInContext(code, context);
  return { context, sockets, run, state: () => run('snapshot'), pending: () => run('[...pending.values()]'),
    api(fn) { context.mockApi = fn; run('api = mockApi'); },
    view(id, replay = false) { context.id = id; run(`setView(id, ${replay})`); },
    accept(value) { context.next = value; run('acceptSnapshot(next)'); } };
}
// state: Tạo snapshot tối thiểu cho test UI.
const state = (matchId, version, status = 'active') => ({ matchId, version, status, mode: 'ranked', state: { pieces: [] }, serverNow: new Date().toISOString() });

test('switching matches accepts lower versions and rejects old-match or stale events', () => {
  const a = app(); a.view('a'); a.accept(state('a', 10)); a.view('b'); a.accept(state('b', 0));
  assert.equal(a.state().matchId, 'b'); assert.equal(a.state().version, 0);
  a.accept(state('a', 50)); assert.equal(a.state().matchId, 'b');
  a.accept(state('b', 3)); a.accept(state('b', 1)); assert.equal(a.state().version, 3);
});

test('reconnect resends original command ID and duplicate ACK clears pending', async () => {
  const a = app(); a.view('a'); a.accept(state('a', 0)); await a.run('connect()');
  const first = a.sockets[0]; first.onopen(); a.run("send({type:'resign'})");
  const original = a.pending()[0];
  await a.run('connect(false)'); const second = a.sockets[1]; second.onopen();
  await second.emit('match.state', state('a', 1, 'completed'));
  const retried = second.sent.find(x => x.type === 'match.command').payload;
  assert.equal(retried.commandId, original.commandId); assert.equal(retried.expectedVersion, 0);
  await second.emit('match.command_accepted', { matchId: 'a', commandId: original.commandId, duplicate: true, snapshot: state('a', 1, 'completed') });
  assert.equal(a.pending().length, 0);
});

test('replay cannot be overwritten by an in-flight callback from old live socket', async () => {
  const a = app(); a.view('a'); a.accept(state('a', 4)); await a.run('connect()');
  const callback = a.sockets[0].onmessage;
  a.view('a', true);
  a.run(`replayEntries = [{sequenceNo:0,kind:'start',stateAfter:{sideToMove:'red',pieces:[]}}]; replayIndex = 0; showReplay();`);
  await callback({ data: JSON.stringify({ type: 'match.state', payload: state('a', 5) }) });
  assert.equal(a.state().mode, 'replay'); assert.equal(a.state().version, 0);
  a.accept(state('a', 100)); assert.equal(a.state().mode, 'replay');
  a.run("send({type:'resign'})"); assert.equal(a.pending().length, 0);
});

test('uncertain REST failure keeps command until reconnect resolves it', async () => {
  const a = app(); a.view('a'); a.accept(state('a', 0));
  a.api(async path => { if (path.endsWith('/commands')) throw new TypeError('Network interrupted'); return { ticket: 'new' }; });
  a.run("send({type:'resign'})"); await new Promise(resolve => setImmediate(resolve));
  assert.equal(a.pending().length, 1); assert.equal(a.sockets.length, 1);
  const id = a.pending()[0].commandId, socket = a.sockets[0]; socket.onopen();
  await socket.emit('match.state', state('a', 1, 'completed'));
  assert.equal(socket.sent.find(x => x.type === 'match.command').payload.commandId, id);
  await socket.emit('match.command_accepted', { matchId: 'a', commandId: id, snapshot: state('a', 1, 'completed') });
  assert.equal(a.pending().length, 0);
});

test('late legal-actions response does not restore moves from previous match', async () => {
  const a = app(); a.view('a'); a.accept(state('a', 1));
  let finish; a.api(() => new Promise(resolve => { finish = resolve; }));
  const loading = a.run('loadLegal()'); a.view('b'); a.accept(state('b', 0)); finish([{ pieceId: 'old' }]); await loading;
  assert.equal(a.run('legal.length'), 0);
});
