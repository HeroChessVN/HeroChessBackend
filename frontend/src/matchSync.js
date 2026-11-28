// Keep optional HUD metadata omitted by command ACKs, never merge old board contents.
export function mergeMatchSnapshot(previous, next, matchId) {
  if (!next?.state || next.matchId !== matchId) return previous;
  if (previous?.matchId === matchId) {
    if (next.version < previous.version || (next.version === previous.version &&
      (Date.parse(next.serverNow) < Date.parse(previous.serverNow) || (previous.settledAt && !next.settledAt)))) return previous;
    return { ...next, sides: next.sides ?? previous.sides, bot: next.bot ?? previous.bot };
  }
  return next;
}

// One read at a time; an event arriving during a read must survive its completion.
export function createMatchRefresh(load, current, accept, onError) {
  const controller = new AbortController();
  let flight, timer, wanted = -1, forced = 0, stopped = false, attempts = 0, retryAt = 0;
  function needed(epoch) { return forced > epoch || (current()?.version ?? -1) < wanted; }
  function refresh({ version, force = false } = {}) {
    if (stopped) return Promise.resolve();
    if (Number.isInteger(version)) wanted = Math.max(wanted, version);
    if (!Number.isInteger(version) || force) forced++;
    else if (!flight && (current()?.version ?? -1) >= wanted) return Promise.resolve();
    if (Date.now() < retryAt) return flight ?? Promise.resolve();
    clearTimeout(timer);
    if (flight) return flight;
    flight = (async () => {
      let epoch;
      let retryAfter = 0;
      try {
        for (let i = 0; i < 2; i++) {
          epoch = forced;
          const state = await Promise.resolve().then(() => load(controller.signal));
          if (stopped) return;
          if (state) accept(state);
          if (!needed(epoch)) { attempts = 0; return; }
        }
      } catch (error) {
        retryAfter = error?.retryAfterMs || 0;
        retryAt = Math.max(retryAt, Date.now() + retryAfter);
        if (!stopped) throw error;
      } finally {
        flight = null;
        if (!stopped && (needed(epoch) || retryAfter > 0)) {
          // Bounded backoff also covers a server response behind the newest event.
          timer = setTimeout(() => refresh({ version: wanted, force: forced > epoch }).catch(onError),
            Math.max(retryAfter, Math.min(15000, 1000 * 2 ** Math.min(attempts++, 4))));
        }
      }
    })();
    return flight;
  }
  return { refresh, dispose() { stopped = true; clearTimeout(timer); controller.abort(); } };
}

export const syncDelay = (socketReady, failures = 0, retryAfter = 0) => Math.max(retryAfter,
  (failures ? Math.min(15000, 2000 * 2 ** Math.min(failures - 1, 3)) : socketReady ? 10000 : 2000) * (1 + Math.random() * .2));
