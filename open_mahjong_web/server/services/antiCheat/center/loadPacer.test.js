"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const { resolveAnalysisConcurrency, createLoadPacer } = require("./loadPacer");

test("resolveAnalysisConcurrency caps 4-core hosts at 2 by default and honors explicit overrides", () => {
  assert.equal(resolveAnalysisConcurrency(undefined, {}, 1), 1);
  assert.equal(resolveAnalysisConcurrency(undefined, {}, 2), 1);
  assert.equal(resolveAnalysisConcurrency(undefined, {}, 4), 2);
  assert.equal(resolveAnalysisConcurrency(undefined, {}, 16), 2);
  assert.equal(resolveAnalysisConcurrency(3, {}, 4), 3);
  assert.equal(
    resolveAnalysisConcurrency(undefined, { ANTI_CHEAT_ANALYSIS_CONCURRENCY: "1" }, 8),
    1,
  );
});

test("createLoadPacer shifts smoothly between full, companion, and yield gears on Linux", async () => {
  let load1 = 1.0; // 1.0 / 4 = 0.25 -> full
  const sleeps = [];
  const yields = [];
  const pacer = createLoadPacer({
    platform: "linux",
    getCpuCount: () => 4,
    getLoadAvg: () => [load1, 0, 0],
    companionDelayMs: 120,
    pauseCheckMs: 500,
    sleepFn: async (ms) => {
      sleeps.push(ms);
      // Drop load after first yield check so waitBeforeItem resumes.
      if (load1 >= 3.2) load1 = 1.0;
    },
  });

  assert.equal(pacer.sample().gear, "full");
  const s1 = await pacer.waitBeforeItem({ activeIndex: 0 });
  assert.equal(s1.gear, "full");
  assert.deepEqual(sleeps, []);

  load1 = 2.4; // 2.4 / 4 = 0.60 -> companion
  assert.equal(pacer.sample().gear, "companion");
  const s2 = await pacer.waitBeforeItem({
    activeIndex: 0,
    onYield: (st) => yields.push(st.gear),
  });
  assert.equal(s2.gear, "companion");
  assert.deepEqual(sleeps, [120]);
  assert.deepEqual(yields, ["companion"]);

  load1 = 3.6; // 3.6 / 4 = 0.90 -> yield, then recovers to 1.0 -> full
  const s3 = await pacer.waitBeforeItem({
    activeIndex: 0,
    onYield: (st) => yields.push(st.gear),
  });
  assert.equal(s3.gear, "full");
  assert.deepEqual(sleeps, [120, 500]);
  assert.deepEqual(yields, ["companion", "yield"]);
});
