"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict");
const {
  createRobertReviews,
  policyVersion,
  validate,
} = require("./robertReviews");
const request = {
  donor_id: "101",
  recipient_id: "102",
  rule_key: "guobiao|standard|match",
  channel: "RON",
  window_days: 0,
};
function fixture() {
  return {
    status: "available",
    version: "test",
    analyzed_decisions: 4,
    eligible_decisions: 4,
    unavailable_decisions: 0,
    skipped_unreconstructed_hands: 0,
    truncated: false,
    decisions: [
      {
        id: "ron",
        status: "available",
        events: [{ recipient_id: "102", family: "RON" }],
        is_recommended: false,
        gap: { deciding_metric: "effective_shanten", value: 1 },
      },
      {
        id: "meld",
        status: "available",
        events: [{ recipient_id: "102", family: "MELD" }],
        is_recommended: false,
        gap: { deciding_metric: "ukeire", value: 2 },
      },
      {
        id: "other",
        status: "available",
        events: [{ recipient_id: "103", family: "RON" }],
        is_recommended: false,
        gap: { deciding_metric: "effective_shanten", value: 1 },
      },
      {
        id: "normal",
        status: "available",
        events: [],
        is_recommended: true,
        gap: { deciding_metric: "tie", value: 0 },
      },
    ],
  };
}
function setup(extra = {}) {
  const values = new Map();
  let calls = 0,
    reads = 0,
    queries = 0;
  const cache = {
    get: async (k) => values.get(k),
    put: async (k, v) => values.set(k, structuredClone(v)),
  };
  const jobs = createRobertReviews({
    meta: async () => ({
      active_generation: "g1",
      as_of: "2026-11-18T00:00:00Z",
    }),
    findGames: async () => {
      queries++;
      return [
        { game_id: "real-id", source_hash: "h1" },
        { game_id: "real-id", source_hash: "h1" },
      ];
    },
    readRecord: async (entry) => {
      reads++;
      return { game_id: entry.game_id, source_hash: entry.source_hash };
    },
    analyzer: async () => {
      calls++;
      return fixture();
    },
    cache,
    version: "policy-test",
    ...extra,
  });
  return {
    jobs,
    values,
    calls: () => calls,
    reads: () => reads,
    queries: () => queries,
  };
}
async function wait(jobs, id) {
  for (let i = 0; i < 400; i++) {
    const job = await jobs.get(id);
    if (!["queued", "running"].includes(job.status)) return job;
    await new Promise((r) => setTimeout(r, 5));
  }
  throw new Error("test job timeout");
}

test("policy version reflects existing repository implementation and threshold validation stays bounded", () => {
  assert.match(policyVersion(), /^robert-visible-discard-v1:[a-f0-9]{24}$/);
  for (const thresholds of [
    { shanten: 0 },
    { shanten: 1.2 },
    { ukeire: -1 },
    { fan: Infinity },
    { unknown: 2 },
    [],
  ])
    assert.throws(() => validate({ ...request, thresholds }));
  assert.throws(() => validate({ ...request, donor_id: "101;DROP TABLE" }));
  assert.throws(() => validate({ ...request, recipient_id: "101" }));
  assert.throws(() => validate({ ...request, channel: "SPECIAL" }));
  assert.equal(
    validate({ ...request, thresholds: { shanten: 2 } }).thresholds.shanten,
    2,
  );
});

test("different feeding channels and thresholds reuse one donor-game policy cache; normal means no obvious gap", async () => {
  const x = setup();
  const first = await wait(x.jobs, (await x.jobs.start(request)).id);
  assert.equal(first.status, "completed");
  assert.deepEqual(first.result.feeding, {
    numerator: 1,
    denominator: 1,
    rate: 1,
  });
  assert.deepEqual(first.result.normal, {
    numerator: 1,
    denominator: 2,
    rate: 0.5,
  });
  assert.equal(x.calls(), 1);
  assert.equal(first.progress.total, 1);
  const second = await wait(
    x.jobs,
    (await x.jobs.start({ ...request, channel: "MELD" })).id,
  );
  assert.deepEqual(second.result.feeding, {
    numerator: 0,
    denominator: 1,
    rate: 0,
  });
  assert.equal(
    second.result.normal.denominator,
    2,
    "Target RON is never a MELD normal reference",
  );
  assert.equal(second.result.cache_hits, 1);
  assert.equal(x.calls(), 1);
  const strict = await wait(
    x.jobs,
    (await x.jobs.start({ ...request, thresholds: { shanten: 2 } })).id,
  );
  assert.equal(strict.result.feeding.numerator, 0);
  assert.equal(strict.result.normal.numerator, 2);
  assert.equal(x.calls(), 1);
  const before = x.queries();
  await x.jobs.get(first.id);
  assert.equal(x.queries(), before, "GET never queries source games");
  await assert.rejects(x.jobs.get("not-in-process"), (e) => e.status === 404);
  await x.jobs.close();
});

test("failed policy results do not become normal data or durable success caches and may retry", async () => {
  let fail = true,
    calls = 0;
  const x = setup({
    analyzer: async () => {
      calls++;
      if (fail) throw new Error("python failure");
      return fixture();
    },
  });
  const bad = await wait(x.jobs, (await x.jobs.start(request)).id);
  assert.equal(bad.status, "failed");
  assert.equal(bad.result.unavailable_games, 1);
  assert.equal(bad.result.normal.denominator, 0);
  assert.equal(bad.result.normal.rate, null);
  assert.equal(x.values.size, 0);
  fail = false;
  const good = await wait(x.jobs, (await x.jobs.start(request)).id);
  assert.equal(good.status, "completed");
  assert.equal(calls, 2);
  assert.equal(x.values.size, 1);
  await x.jobs.close();
});

test("partial reconstruction is excluded from denominators, not cached as complete, and can be retried", async () => {
  let calls = 0;
  const x = setup({
    analyzer: async () => {
      calls++;
      const r = fixture();
      r.skipped_unreconstructed_hands = 1;
      r.unavailable_decisions = 2;
      return r;
    },
  });
  const first = await wait(x.jobs, (await x.jobs.start(request)).id);
  assert.equal(first.result.unavailable_decisions, 2);
  assert.equal(first.result.skipped_unreconstructed_hands, 1);
  assert.equal(x.values.size, 0);
  await wait(x.jobs, (await x.jobs.start(request)).id);
  assert.equal(calls, 2);
  await x.jobs.close();
});

test("active duplicates are coalesced, queued cancellation does not launch work, and local CPU jobs serialize", async () => {
  let entered = 0,
    active = 0,
    maxActive = 0;
  const x = setup({
    analyzer: async (row, { signal }) => {
      entered++;
      active++;
      maxActive = Math.max(maxActive, active);
      try {
        return await new Promise((resolve, reject) => {
          const timer = setTimeout(() => resolve(fixture()), 70);
          signal.addEventListener(
            "abort",
            () => {
              clearTimeout(timer);
              reject(
                Object.assign(new Error("cancel"), { name: "AbortError" }),
              );
            },
            { once: true },
          );
        });
      } finally {
        active--;
      }
    },
  });
  const one = await x.jobs.start(request),
    dup = await x.jobs.start(request);
  assert.equal(one.id, dup.id);
  const queued = await x.jobs.start({ ...request, channel: "MELD" });
  await x.jobs.cancel(queued.id);
  await x.jobs.cancel(one.id);
  const done = await wait(x.jobs, one.id);
  assert.equal(done.status, "cancelled");
  assert.equal((await x.jobs.get(queued.id)).status, "cancelled");
  const retry = await wait(x.jobs, (await x.jobs.start(request)).id);
  assert.equal(retry.status, "completed");
  assert.equal(maxActive, 1);
  assert.ok(entered <= 2);
  await x.jobs.close();
});

test("source hash changes fail explicitly even when an older policy cache exists", async () => {
  let mismatch = false;
  const x = setup({
    readRecord: async (entry) => ({
      game_id: entry.game_id,
      source_hash: mismatch ? "new-fingerprint" : entry.source_hash,
    }),
  });
  await wait(x.jobs, (await x.jobs.start(request)).id);
  mismatch = true;
  const bad = await wait(x.jobs, (await x.jobs.start(request)).id);
  assert.equal(bad.status, "failed");
  assert.match(bad.error, /牌谱已变化/);
  assert.equal(x.calls(), 1);
  await x.jobs.close();
});

test("unquantified policy comparisons never become normal data, but stable evaluations can be cached", async () => {
  let calls = 0;
  const x = setup({
    analyzer: async () => {
      calls++;
      const r = fixture();
      r.decisions[3].is_recommended = false;
      r.decisions[3].comparison_status = "incomparable";
      r.decisions[3].rate_comparable = false;
      r.decisions[3].gap = {
        deciding_metric: "cheap_tenpai_preference",
        value: null,
      };
      r.policy_evaluated_decisions = 4;
      r.calculator_unavailable_decisions = 0;
      r.analyzed_decisions = 3;
      r.unavailable_decisions = 1;
      return r;
    },
  });
  const first = await wait(x.jobs, (await x.jobs.start(request)).id);
  assert.deepEqual(first.result.normal, {
    numerator: 0,
    denominator: 1,
    rate: 0,
  });
  assert.equal(first.result.unavailable_decisions, 1);
  assert.equal(x.values.size, 1);
  const second = await wait(
    x.jobs,
    (await x.jobs.start({ ...request, channel: "MELD" })).id,
  );
  assert.equal(second.result.normal.denominator, 1);
  assert.equal(second.result.cache_hits, 1);
  assert.equal(calls, 1);
  await x.jobs.close();
});
