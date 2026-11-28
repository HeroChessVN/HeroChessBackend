// Kestrel check against a disposable local API/DB; creates one test account, never prints credentials.
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
const origin = new URL(process.env.HERO_CHESS_SMOKE_URL || 'http://127.0.0.1:5197');
if (!['127.0.0.1', 'localhost', '[::1]'].includes(origin.hostname)) throw new Error('This security check is local-only.');
const credentials = { email: `traffic-${randomUUID()}@hero.local`, password: `Test!A1-${randomUUID()}` };
const endpoint = path => new URL('/api/v1' + path, origin);
async function auth(path) {
  for (let attempt = 0; attempt < 6; attempt++) {
    const response = await fetch(endpoint(path), { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(credentials), signal: AbortSignal.timeout(10000) });
    if (response.status === 429) {
      const seconds = Number(response.headers.get('Retry-After'));
      assert.ok(seconds > 0 && seconds <= 5, 'Unexpected auth retry delay');
      await response.arrayBuffer(); await new Promise(resolve => setTimeout(resolve, seconds * 1000)); continue;
    }
    assert.equal(response.status, 200); return response.json().catch(() => ({}));
  }
  throw new Error('Auth did not recover within the retry budget');
}
await auth('/auth/register');
const token = (await auth('/auth/login?useCookies=false')).accessToken;
const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` };
const large = await fetch(endpoint(`/matches/${randomUUID()}/commands`), { method: 'POST', headers,
  body: JSON.stringify({ commandId: randomUUID(), expectedVersion: 0, action: { type: 'move', padding: 'x'.repeat(70000) } }),
  signal: AbortSignal.timeout(10000) });
assert.equal(large.status, 413);
assert.equal((await large.json()).code, 'REQUEST_TOO_LARGE');
let retryAfter;
for (let attempt = 0; attempt < 24; attempt++) {
  const response = await fetch(endpoint(`/matches/${randomUUID()}/state`), { headers, signal: AbortSignal.timeout(10000) });
  const value = await response.json();
  if (response.status === 429) {
    assert.equal(value.code, 'RATE_LIMITED'); retryAfter = Number(response.headers.get('Retry-After')); break;
  }
  assert.equal(response.status, 404);
}
assert.ok(retryAfter > 0, 'State reads were not limited with Retry-After');
assert.equal((await fetch(endpoint('/me'), { headers, signal: AbortSignal.timeout(10000) })).status, 200);
console.log(JSON.stringify({ commandOver64KiB: 413, stateLimit: 429, retryAfterSeconds: retryAfter, profileStillAvailable: true }));
