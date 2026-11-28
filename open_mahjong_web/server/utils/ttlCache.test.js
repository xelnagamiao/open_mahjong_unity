'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createTtlCache } = require('./ttlCache');

test('ttlCache caches values until expiry and coalesces concurrent misses', async () => {
  let clock = 1000;
  const cache = createTtlCache({ maxEntries: 3, now: () => clock });
  let calls = 0;

  const compute = async () => {
    calls += 1;
    await new Promise((r) => setTimeout(r, 15));
    return { v: calls };
  };

  const results = await Promise.all([
    cache.getOrCompute('k1', 100, compute),
    cache.getOrCompute('k1', 100, compute),
    cache.getOrCompute('k1', 100, compute),
  ]);
  assert.equal(calls, 1);
  assert.deepEqual(results, [{ v: 1 }, { v: 1 }, { v: 1 }]);

  // Still fresh at clock=1050.
  clock = 1050;
  const hit = await cache.getOrCompute('k1', 100, compute);
  assert.equal(calls, 1);
  assert.deepEqual(hit, { v: 1 });

  // Expired at clock=1100.
  clock = 1100;
  const refreshed = await cache.getOrCompute('k1', 100, compute);
  assert.equal(calls, 2);
  assert.deepEqual(refreshed, { v: 2 });
});

test('ttlCache enforces maxEntries LRU eviction and does not cache rejections', async () => {
  const cache = createTtlCache({ maxEntries: 2 });
  cache.set('a', 1, 10_000);
  cache.set('b', 2, 10_000);
  assert.equal(cache.get('a'), 1); // touch 'a', making 'b' oldest
  cache.set('c', 3, 10_000);
  assert.equal(cache.get('b'), undefined);
  assert.equal(cache.get('a'), 1);
  assert.equal(cache.get('c'), 3);

  await assert.rejects(
    cache.getOrCompute('err', 10_000, async () => {
      throw new Error('boom');
    }),
    /boom/,
  );
  assert.equal(cache.get('err'), undefined);
});
