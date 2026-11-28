import test from 'node:test';
import assert from 'node:assert/strict';
import { createMatchRefresh, mergeMatchSnapshot, syncDelay } from './matchSync.js';
import { api } from './api.js';

const flush = () => new Promise(resolve => setImmediate(resolve));

const state = (version, extra = {}) => ({ matchId: 'a', version, serverNow: '2026-11-26T00:00:00Z', state: { pieces: [] }, ...extra });
test('ACK keeps optional HUD metadata but replaces the board; older branches and settled regressions are rejected', () => {
  const before = state(3, { sides: ['red'], state: { pieces: ['captured'] } });
  const ack = mergeMatchSnapshot(before, state(4), 'a');
  assert.deepEqual(ack.sides, ['red']); assert.deepEqual(ack.state.pieces, []);
  assert.equal(mergeMatchSnapshot(ack, state(2), 'a'), ack);
  assert.equal(mergeMatchSnapshot(ack, { ...state(8), matchId: 'b' }, 'a'), ack);
  const settled = mergeMatchSnapshot(ack, state(4, { settledAt: '2026-11-26T00:00:00Z' }), 'a');
  assert.ok(settled.settledAt);
  assert.equal(mergeMatchSnapshot(settled, state(4), 'a'), settled);
});
test('an event received during GET survives; simultaneous requests share a flight and late ACK avoids another GET', async () => {
  const reads = []; let current = state(0);
  const sync = createMatchRefresh(() => new Promise(resolve => reads.push(resolve)), () => current,
    next => { current = mergeMatchSnapshot(current, next, 'a'); }, assert.fail);
  try {
    const first = sync.refresh();
    assert.equal(first, sync.refresh({ version: 2 }));
    await flush();
    assert.equal(reads.length, 1);
    reads[0](state(1)); await flush();
    assert.equal(reads.length, 2);
    reads[1](state(2)); await first;
    await sync.refresh({ version: 2 }); assert.equal(reads.length, 2);
    const next = sync.refresh({ version: 3 });
    await flush();
    current = state(4); reads[2](state(3)); await next;
    assert.equal(current.version, 4);
    assert.equal(reads.length, 3);
  } finally { sync.dispose(); }
});
test('same-version metadata notice during GET forces a follow-up and disposal ignores late responses', async () => {
  const reads = []; let current = state(5);
  const sync = createMatchRefresh(() => new Promise(resolve => reads.push(resolve)), () => current, next => { current = next; }, assert.fail);
  const first = sync.refresh(); sync.refresh({ force: true });
  await flush(); reads[0](state(5)); await flush();
  assert.equal(reads.length, 2);
  sync.dispose(); reads[1](state(6)); await first;
  assert.equal(current.version, 5);
  await sync.refresh(); assert.equal(reads.length, 2);
});
test('synchronous failures can retry and an intentional abort does not report an error after unmount', async () => {
  let calls = 0, current;
  const sync = createMatchRefresh(() => { if (++calls === 1) throw new Error('sync failure'); return state(1); },
    () => current, next => { current = next; }, assert.fail);
  try { await assert.rejects(sync.refresh(), /sync failure/); await sync.refresh(); assert.equal(current.version, 1); }
  finally { sync.dispose(); }
  const aborted = createMatchRefresh(signal => new Promise((_, reject) => signal.addEventListener('abort', () => reject(new Error('aborted')))),
    () => null, assert.fail, assert.fail);
  const pending = aborted.refresh(); await flush(); aborted.dispose(); await pending;
});
test('server Retry-After is respected instead of retrying sooner than its delay', async () => {
  const saved = globalThis.fetch;
  globalThis.fetch = async () => new Response('{"message":"busy"}', { status: 429, headers: { 'Retry-After': '30' } });
  try { await assert.rejects(api('token', '/test'), error => error.status === 429 && error.retryAfterMs === 30000); }
  finally { globalThis.fetch = saved; }
  assert.equal(syncDelay(false, 1, 30000), 30000);
  let calls = 0, current;
  const now = Date.now;
  const sync = createMatchRefresh(async () => {
    if (++calls === 1) throw Object.assign(new Error('busy'), { retryAfterMs: 30000 });
    return state(1);
  }, () => current, next => { current = next; }, assert.fail);
  try {
    await assert.rejects(sync.refresh(), /busy/);
    await sync.refresh(); await sync.refresh({ version: 1 });
    assert.equal(calls, 1); // heartbeat/manual refresh cannot bypass the event's Retry-After
    Date.now = () => now() + 31000;
    await sync.refresh(); assert.equal(calls, 2);
  } finally { Date.now = now; sync.dispose(); }
});
test('a failed GET releases the flight; retry and polling are bounded', async () => {
  let calls = 0, current;
  const sync = createMatchRefresh(async () => { if (++calls === 1) throw new Error('offline'); return state(1); },
    () => current, next => { current = next; }, assert.fail);
  try { await assert.rejects(sync.refresh(), /offline/); await sync.refresh(); assert.equal(current.version, 1); }
  finally { sync.dispose(); }
  for (let n = 0; n < 10; n++) {
    assert.ok(syncDelay(true) >= 10000 && syncDelay(true) < 12000);
    assert.ok(syncDelay(false, 99) >= 15000 && syncDelay(false, 99) < 18000);
  }
});
