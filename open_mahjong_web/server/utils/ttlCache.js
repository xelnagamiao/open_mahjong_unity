'use strict';

/**
 * Bounded LRU + TTL cache with singleflight request coalescing.
 * Prevents thundering-herd DB scans when multiple clients request the same
 * heavy platform statistics or leaderboard snapshot simultaneously.
 */
function createTtlCache({ maxEntries = 256, now = () => Date.now() } = {}) {
  const store = new Map();
  const inFlight = new Map();

  function get(key) {
    const entry = store.get(key);
    if (!entry) return undefined;
    if (now() >= entry.expiresAt) {
      store.delete(key);
      return undefined;
    }
    // Refresh LRU order.
    store.delete(key);
    store.set(key, entry);
    return entry.value;
  }

  function set(key, value, ttlMs) {
    if (ttlMs <= 0) return value;
    if (store.has(key)) store.delete(key);
    store.set(key, { value, expiresAt: now() + ttlMs });
    while (store.size > maxEntries) {
      const oldestKey = store.keys().next().value;
      store.delete(oldestKey);
    }
    return value;
  }

  async function getOrCompute(key, ttlMs, computeFn) {
    const cached = get(key);
    if (cached !== undefined) return cached;
    if (inFlight.has(key)) return inFlight.get(key);

    const promise = Promise.resolve()
      .then(computeFn)
      .then((value) => {
        set(key, value, ttlMs);
        return value;
      })
      .finally(() => {
        inFlight.delete(key);
      });

    inFlight.set(key, promise);
    return promise;
  }

  function deletePrefix(prefix) {
    for (const key of store.keys()) {
      if (key.startsWith(prefix)) store.delete(key);
    }
  }

  function clear() {
    store.clear();
  }

  function size() {
    return store.size;
  }

  return { get, set, getOrCompute, deletePrefix, clear, size };
}

module.exports = { createTtlCache };
