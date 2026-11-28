"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");
const {
  createPythonWorkerPool,
  closeAllWorkerPools,
} = require("./pythonWorkerPool");
const { selectPython } = require("./python");

test("pythonWorkerPool reuses warm analysisWorker across decisions and features, and recycles after maxJobsPerWorker", async () => {
  const pool = createPythonWorkerPool({
    scriptPath: path.join(__dirname, "center", "analysisWorker.py"),
    pythonBinary: selectPython(),
    args: [],
    maxWorkers: 1,
    maxJobsPerWorker: 2,
    idleTimeoutMs: 5000,
    errorPrefix: "test_analysis",
  });

  try {
    const r1 = await pool.exec({ op: "decisions", decisions: [] });
    assert.deepEqual(r1.results, []);
    assert.equal(pool.stats().workers, 1);

    const hand = [11, 12, 13, 21, 22, 23, 31, 32, 33, 45, 45, 45, 46, 47];
    const r2 = await pool.exec({
      op: "features",
      payload: {
        melds: [],
        donors: [
          {
            id: "c1",
            before: {
              donor_seat: 0,
              hands: [hand],
              melds: [[]],
              candidates: [...new Set(hand)],
              actual_tile: 45,
            },
          },
        ],
      },
    });
    assert.equal(r2.donors.c1.status, "available");
    assert.equal(r2.donors.c1.shanten_cost, 1);
    // Worker retired after 2 jobs (maxJobsPerWorker=2).
    assert.equal(pool.stats().workers, 0);

    // Next job spawns a fresh worker transparently.
    const r3 = await pool.exec({ op: "decisions", decisions: [] });
    assert.deepEqual(r3.results, []);
    assert.equal(pool.stats().workers, 1);
  } finally {
    pool.close();
  }
});

test("robertBridge persistent worker handles requests and honors AbortSignal", async () => {
  const pool = createPythonWorkerPool({
    scriptPath: path.join(__dirname, "center", "robertBridge.py"),
    pythonBinary: selectPython(),
    args: ["--worker"],
    maxWorkers: 1,
    maxJobsPerWorker: 10,
    idleTimeoutMs: 5000,
    errorPrefix: "test_robert",
  });

  try {
    const out = await pool.exec({ decisions: [], thresholds: {} });
    assert.deepEqual(out.results, []);
    assert.match(out.policy_source_sha256, /^[a-f0-9]{64}$/);

    const ac = new AbortController();
    ac.abort();
    await assert.rejects(
      pool.exec({ decisions: [], thresholds: {} }, { signal: ac.signal }),
      (err) => err.name === "AbortError",
    );
  } finally {
    pool.close();
    closeAllWorkerPools();
  }
});
