"use strict";

const os = require("node:os");

const DEFAULT_SOFT_LOAD_RATIO = 0.5;
const DEFAULT_PAUSE_LOAD_RATIO = 0.8;

/**
 * Compute default anti-cheat analysis concurrency from host CPU count.
 * On a 4-core server this defaults to 2 (leaving headroom for live games, PG,
 * Node Web, and Go chat) instead of saturating all 4 cores.
 */
function resolveAnalysisConcurrency(
  explicit,
  env = process.env,
  cpuCount = os.cpus().length,
) {
  const configured = Number(explicit || env.ANTI_CHEAT_ANALYSIS_CONCURRENCY);
  if (Number.isFinite(configured) && configured >= 1) {
    return Math.max(1, Math.min(8, Math.floor(configured)));
  }
  const cpus = Math.max(1, Number(cpuCount) || 1);
  return Math.max(1, Math.min(2, Math.floor(cpus / 2)));
}

function sleep(ms, signal) {
  if (ms <= 0) return Promise.resolve();
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      return reject(
        Object.assign(new Error("已取消"), { name: "AbortError", cancelled: true }),
      );
    }
    const timer = setTimeout(() => {
      signal?.removeEventListener("abort", onAbort);
      resolve();
    }, ms);
    timer.unref?.();
    const onAbort = () => {
      clearTimeout(timer);
      reject(
        Object.assign(new Error("已取消"), { name: "AbortError", cancelled: true }),
      );
    };
    signal?.addEventListener("abort", onAbort, { once: true });
  });
}

/**
 * Adaptive load pacer that requires zero manual core pinning or cron windows.
 * Uses 1-minute OS load average relative to CPU count (on Linux) to shift
 * seamlessly across full-speed / companion / yield gears.
 */
function createLoadPacer({
  enabled = process.env.ANTI_CHEAT_LOAD_PACING !== "0",
  softRatio = Number(process.env.ANTI_CHEAT_SOFT_LOAD_RATIO) || DEFAULT_SOFT_LOAD_RATIO,
  pauseRatio = Number(process.env.ANTI_CHEAT_PAUSE_LOAD_RATIO) || DEFAULT_PAUSE_LOAD_RATIO,
  companionDelayMs = Number(process.env.ANTI_CHEAT_COMPANION_DELAY_MS) || 150,
  pauseCheckMs = Number(process.env.ANTI_CHEAT_PAUSE_CHECK_MS) || 3000,
  batchDelayMs = Number(process.env.ANTI_CHEAT_DB_BATCH_DELAY_MS) || 40,
  getLoadAvg = () => os.loadavg(),
  getCpuCount = () => os.cpus().length,
  platform = process.platform,
  sleepFn = sleep,
} = {}) {
  function sample() {
    if (!enabled || platform === "win32") {
      return { gear: "full", loadRatio: 0, delayMs: 0, maxActive: Infinity };
    }
    const cpus = Math.max(1, Number(getCpuCount()) || 1);
    const load1 = Number(getLoadAvg()?.[0]) || 0;
    const loadRatio = load1 / cpus;
    if (loadRatio >= pauseRatio) {
      return { gear: "yield", loadRatio, delayMs: pauseCheckMs, maxActive: 0 };
    }
    if (loadRatio >= softRatio) {
      return { gear: "companion", loadRatio, delayMs: companionDelayMs, maxActive: 1 };
    }
    return { gear: "full", loadRatio, delayMs: 0, maxActive: Infinity };
  }

  /**
   * Wait before starting the next unit of work if the host is under load.
   * Calls `onYield` (e.g., to refresh heartbeat / check cancellation) while
   * paused in yield gear.
   */
  async function waitBeforeItem({ activeIndex = 0, onYield, signal } = {}) {
    while (true) {
      const state = sample();
      if (state.gear === "yield") {
        if (onYield) await onYield(state);
        await sleepFn(state.delayMs, signal);
        continue;
      }
      if (state.gear === "companion") {
        // In companion gear, secondary workers pause briefly so effective
        // concurrency drops to 1 while the primary worker paces gently.
        const waitMs = activeIndex > 0 ? Math.max(state.delayMs * 4, 600) : state.delayMs;
        if (onYield && activeIndex === 0) await onYield(state);
        await sleepFn(waitMs, signal);
        if (activeIndex > 0) continue;
      }
      return state;
    }
  }

  async function paceDbBatch(signal) {
    if (!enabled || batchDelayMs <= 0) return;
    const state = sample();
    const delay = state.gear === "full" ? Math.min(batchDelayMs, 30) : Math.max(batchDelayMs, 80);
    await sleepFn(delay, signal);
  }

  return { sample, waitBeforeItem, paceDbBatch };
}

module.exports = {
  resolveAnalysisConcurrency,
  createLoadPacer,
  sleep,
};
