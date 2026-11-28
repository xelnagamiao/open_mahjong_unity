"use strict";

const os = require("node:os");
const { spawn } = require("node:child_process");

const activePools = new Set();

function lowerPriority(pid) {
  if (!pid) return;
  try {
    os.setPriority(pid, os.constants.priority.PRIORITY_LOW);
  } catch {
    // Best-effort only; some containers disallow changing priority.
  }
}

/**
 * Reusable pool of long-lived NDJSON Python worker processes.
 * Keeps in-process Python caches (shanten/tingpai/hepai/heuristic) warm across
 * games while enforcing per-worker job limits, idle reaping, and clean aborts.
 */
function setWorkerRef(w, active) {
  const fn = active ? "ref" : "unref";
  try {
    w.child[fn]?.();
    w.child.stdin?.[fn]?.();
    w.child.stdout?.[fn]?.();
    w.child.stderr?.[fn]?.();
  } catch {
    // ignore
  }
}

function createPythonWorkerPool({
  scriptPath,
  pythonBinary,
  args = ["--worker"],
  maxWorkers = 2,
  maxJobsPerWorker = 300,
  idleTimeoutMs = 60_000,
  maxLineBytes = 64 * 1024 * 1024,
  errorPrefix = "python_worker",
} = {}) {
  const workers = new Set();
  const idle = [];
  const waiters = [];
  let closed = false;
  let seq = 0;

  function removeWorker(w) {
    workers.delete(w);
    const idx = idle.indexOf(w);
    if (idx >= 0) idle.splice(idx, 1);
    if (w.idleTimer) {
      clearTimeout(w.idleTimer);
      w.idleTimer = null;
    }
  }

  function destroyWorker(w, reason) {
    if (w.dead) return;
    w.dead = true;
    removeWorker(w);
    if (w.current) {
      const pending = w.current;
      w.current = null;
      pending.finish(reason || new Error(`${errorPrefix}_closed`));
    }
    try {
      w.child.kill();
    } catch {
      // ignore
    }
    pump();
  }

  function retireWorker(w) {
    if (w.dead) return;
    w.dead = true;
    removeWorker(w);
    try {
      w.child.stdin.end();
    } catch {
      try {
        w.child.kill();
      } catch {
        // ignore
      }
    }
    pump();
  }

  function scheduleIdle(w) {
    setWorkerRef(w, false);
    if (w.idleTimer) clearTimeout(w.idleTimer);
    if (idleTimeoutMs <= 0) return;
    w.idleTimer = setTimeout(() => {
      if (!w.current && !w.dead) retireWorker(w);
    }, idleTimeoutMs);
    w.idleTimer.unref();
  }

  function spawnWorker() {
    const child = spawn(pythonBinary, [scriptPath, ...args], {
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    });
    // Allow host process / test runners to exit cleanly when workers are idle.
    child.unref();
    child.stdin?.unref?.();
    child.stdout?.unref?.();
    child.stderr?.unref?.();
    lowerPriority(child.pid);

    const w = {
      child,
      buffer: "",
      stderr: "",
      jobs: 0,
      current: null,
      dead: false,
      idleTimer: null,
    };
    workers.add(w);

    child.on("error", (err) => {
      destroyWorker(
        w,
        new Error(`${errorPrefix}_unavailable:${err.code || "spawn"}`),
      );
    });

    child.stdin.on("error", () => {
      // Handled via close/error events.
    });

    child.stderr.on("data", (chunk) => {
      if (w.stderr.length < 2000) w.stderr += chunk.toString("utf8");
    });

    child.stdout.on("data", (chunk) => {
      w.buffer += chunk.toString("utf8");
      if (w.buffer.length > maxLineBytes) {
        destroyWorker(w, new Error(`${errorPrefix}_output_limit`));
        return;
      }
      let nl;
      while ((nl = w.buffer.indexOf("\n")) >= 0) {
        const line = w.buffer.slice(0, nl).trim();
        w.buffer = w.buffer.slice(nl + 1);
        if (!line) continue;
        const pending = w.current;
        if (!pending) continue;
        w.current = null;
        try {
          const msg = JSON.parse(line);
          if (msg.id !== pending.id) {
            destroyWorker(w, new Error(`${errorPrefix}_protocol_mismatch`));
            pending.finish(new Error(`${errorPrefix}_protocol_mismatch`));
            return;
          }
          if (msg.ok) {
            pending.finish(null, msg.result);
          } else {
            pending.finish(new Error(msg.error || `${errorPrefix}_failed`));
          }
        } catch {
          destroyWorker(w, new Error(`${errorPrefix}_invalid_json`));
          return;
        }
        w.jobs += 1;
        if (closed || w.jobs >= maxJobsPerWorker) {
          retireWorker(w);
        } else {
          idle.push(w);
          scheduleIdle(w);
          pump();
        }
      }
    });

    child.on("close", (code) => {
      const tail = w.stderr
        .split("\n")
        .filter(Boolean)
        .slice(-1)
        .join("")
        .slice(0, 120);
      const err =
        code === 0
          ? new Error(`${errorPrefix}_eof`)
          : new Error(
              tail ? `${errorPrefix}_exit_${code}:${tail}` : `${errorPrefix}_exit_${code}`,
            );
      destroyWorker(w, err);
    });

    return w;
  }

  function dispatch(w, job) {
    setWorkerRef(w, true);
    if (w.idleTimer) {
      clearTimeout(w.idleTimer);
      w.idleTimer = null;
    }
    if (job.signal?.aborted) {
      idle.push(w);
      scheduleIdle(w);
      job.reject(
        Object.assign(new Error(job.abortMessage || "已取消"), {
          name: "AbortError",
        }),
      );
      pump();
      return;
    }

    const id = ++seq;
    let settled = false;
    const finish = (err, result) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      job.signal?.removeEventListener("abort", onAbort);
      if (err) job.reject(err);
      else job.resolve(result);
    };

    const onAbort = () => {
      const abortErr = Object.assign(new Error(job.abortMessage || "已取消"), {
        name: "AbortError",
      });
      destroyWorker(w, abortErr);
      finish(abortErr);
    };

    const timer = setTimeout(() => {
      const timeoutErr = new Error(`${errorPrefix}_timeout`);
      destroyWorker(w, timeoutErr);
      finish(timeoutErr);
    }, job.timeoutMs || 60_000);
    timer.unref();

    job.signal?.addEventListener("abort", onAbort, { once: true });
    w.current = { id, finish };

    try {
      w.child.stdin.write(`${JSON.stringify({ id, ...job.request })}\n`);
    } catch (err) {
      destroyWorker(w, new Error(`${errorPrefix}_write_failed:${err.code || "io"}`));
    }
  }

  function pump() {
    while (waiters.length > 0 && !closed) {
      const job = waiters[0];
      if (job.signal?.aborted) {
        waiters.shift();
        job.reject(
          Object.assign(new Error(job.abortMessage || "已取消"), {
            name: "AbortError",
          }),
        );
        continue;
      }
      let w = idle.pop();
      if (!w && workers.size < maxWorkers) {
        w = spawnWorker();
      }
      if (!w) break;
      waiters.shift();
      dispatch(w, job);
    }
  }

  function exec(request, { timeoutMs = 60_000, signal, abortMessage } = {}) {
    if (closed) {
      return Promise.reject(new Error(`${errorPrefix}_pool_closed`));
    }
    if (signal?.aborted) {
      return Promise.reject(
        Object.assign(new Error(abortMessage || "已取消"), {
          name: "AbortError",
        }),
      );
    }
    return new Promise((resolve, reject) => {
      const job = { request, timeoutMs, signal, abortMessage, resolve, reject };
      if (signal) {
        const onQueuedAbort = () => {
          const idx = waiters.indexOf(job);
          if (idx >= 0) {
            waiters.splice(idx, 1);
            reject(
              Object.assign(new Error(abortMessage || "已取消"), {
                name: "AbortError",
              }),
            );
          }
        };
        signal.addEventListener("abort", onQueuedAbort, { once: true });
        const origResolve = resolve;
        const origReject = reject;
        job.resolve = (v) => {
          signal.removeEventListener("abort", onQueuedAbort);
          origResolve(v);
        };
        job.reject = (e) => {
          signal.removeEventListener("abort", onQueuedAbort);
          origReject(e);
        };
      }
      waiters.push(job);
      pump();
    });
  }

  function close() {
    closed = true;
    while (waiters.length > 0) {
      const job = waiters.shift();
      job.reject(new Error(`${errorPrefix}_pool_closed`));
    }
    for (const w of [...workers]) {
      retireWorker(w);
    }
    activePools.delete(poolApi);
  }

  function stats() {
    return {
      workers: workers.size,
      idle: idle.length,
      queued: waiters.length,
      closed,
    };
  }

  const poolApi = { exec, close, stats };
  activePools.add(poolApi);
  return poolApi;
}

const namedPools = new Map();

function getSharedWorkerPool(key, factoryOptions) {
  let pool = namedPools.get(key);
  if (!pool || pool.stats().closed) {
    pool = createPythonWorkerPool(factoryOptions);
    namedPools.set(key, pool);
  }
  return pool;
}

function closeAllWorkerPools() {
  for (const pool of [...activePools]) {
    pool.close();
  }
  namedPools.clear();
}

process.once("beforeExit", closeAllWorkerPools);
process.once("exit", closeAllWorkerPools);

module.exports = {
  createPythonWorkerPool,
  getSharedWorkerPool,
  closeAllWorkerPools,
  lowerPriority,
};
