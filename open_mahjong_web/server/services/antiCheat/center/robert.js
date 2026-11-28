"use strict";

const path = require("node:path");
const { spawn } = require("node:child_process");
const { replayRecord } = require("../replay");
const { selectPython } = require("../python");
const { getSharedWorkerPool } = require("../pythonWorkerPool");

const ROBERT_VERSION = "robert-visible-discard-v1";
const DEFAULT_THRESHOLDS = Object.freeze({
  shanten: 1,
  legal_ukeire: 4,
  ukeire: 4,
  fan: 8,
  ron_ukeire: 4,
  reshape_progress: 24,
});
const THRESHOLD_LIMITS = Object.freeze({
  shanten: Object.freeze({
    min: 1,
    max: 8,
    integer: true,
    unit: "shanten_steps",
  }),
  legal_ukeire: Object.freeze({
    min: 1,
    max: 136,
    integer: false,
    unit: "weighted_qualifying_draw_copies",
  }),
  ukeire: Object.freeze({
    min: 1,
    max: 136,
    integer: false,
    unit: "weighted_live_tile_copies",
  }),
  fan: Object.freeze({
    min: 1,
    max: 1000,
    integer: false,
    unit: "max_qualifying_fan",
  }),
  ron_ukeire: Object.freeze({
    min: 1,
    max: 136,
    integer: false,
    unit: "ron_live_tile_copies",
  }),
  reshape_progress: Object.freeze({
    min: 1,
    max: 20000,
    integer: false,
    unit: "weighted_future_tile_exposure",
  }),
});
function visibleContext(decision) {
  const seat = decision.donor_seat,
    hand = [...decision.hands[seat]],
    visible = { ...decision.public_counts };
  for (const tile of hand) visible[tile] = (visible[tile] || 0) + 1;
  decision.melds.forEach((group, index) =>
    group.forEach((meld) => {
      // Other players' concealed kongs disclose no tile identity to this policy.
      if (meld[0] === "g" || (meld[0] === "G" && index === seat)) {
        const tile = Number(meld.slice(1));
        visible[tile] = (visible[tile] || 0) + 4;
      }
    }),
  );
  return {
    id: decision.id,
    hand,
    combination_tiles: [...decision.melds[seat]],
    visible,
    wall_left: decision.remaining_tiles,
    min_fan: decision.hepai_limit,
    round_wind: Math.floor((decision.current_round - 1) / 4),
    seat_wind: (seat - decision.dealer_index + 4) % 4,
    flower_count: decision.flowers[seat],
    actual_tile: decision.actual_tile,
  };
}

async function runRobertBridge(decisions, options = {}) {
  if (!decisions.length)
    return {
      results: [],
      thresholds: { ...DEFAULT_THRESHOLDS, ...options.thresholds },
    };
  const pythonBinary = selectPython(options);
  if (
    options.persistent !== false &&
    process.env.ANTI_CHEAT_PERSISTENT_WORKERS !== "0"
  ) {
    const pool = getSharedWorkerPool(`robert:${pythonBinary}`, {
      scriptPath: path.join(__dirname, "robertBridge.py"),
      pythonBinary,
      args: ["--worker"],
      maxWorkers: 1,
      maxJobsPerWorker: 200,
      idleTimeoutMs: 60_000,
      maxLineBytes: 32 * 1024 * 1024,
      errorPrefix: "robert_policy",
    });
    const result = await pool.exec(
      {
        decisions,
        thresholds: { ...DEFAULT_THRESHOLDS, ...options.thresholds },
      },
      {
        timeoutMs: options.timeout_ms || 120000,
        signal: options.signal,
        abortMessage: "罗伯特对比已取消",
      },
    );
    if (!result || !Array.isArray(result.results)) {
      throw new Error("robert_policy_invalid_output");
    }
    return result;
  }
  return new Promise((resolve, reject) => {
    const child = spawn(
      pythonBinary,
      [path.join(__dirname, "robertBridge.py")],
      { stdio: ["pipe", "pipe", "pipe"], windowsHide: true },
    );
    let output = "",
      settled = false;
    const finish = (error, result) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      options.signal?.removeEventListener("abort", abort);
      error ? reject(error) : resolve(result);
    };
    const abort = () => {
      child.kill();
      finish(
        Object.assign(new Error("罗伯特对比已取消"), { name: "AbortError" }),
      );
    };
    const timer = setTimeout(() => {
      child.kill();
      finish(new Error("robert_policy_timeout"));
    }, options.timeout_ms || 120000);
    child.on("error", (e) =>
      finish(new Error(`robert_policy_unavailable:${e.code || "spawn"}`)),
    );
    child.stdout.on("data", (part) => {
      output += part.toString("utf8");
      if (output.length > 32 * 1024 * 1024) {
        child.kill();
        finish(new Error("robert_output_limit"));
      }
    });
    child.stderr.on("data", () => {});
    child.stdin.on("error", () => {});
    child.on("close", (code) => {
      if (code !== 0) return finish(new Error(`robert_policy_exit_${code}`));
      try {
        const result = JSON.parse(output);
        if (!Array.isArray(result.results)) throw new Error();
        finish(null, result);
      } catch {
        finish(new Error("robert_policy_invalid_output"));
      }
    });
    if (options.signal?.aborted) return abort();
    options.signal?.addEventListener("abort", abort, { once: true });
    child.stdin.end(
      JSON.stringify({
        decisions,
        thresholds: { ...DEFAULT_THRESHOLDS, ...options.thresholds },
      }),
    );
  });
}

function extractRobertDecisions(row, options = {}) {
  const game = replayRecord(row);
  if (game.error)
    return {
      status: "unavailable",
      reason: game.error,
      decisions: [],
      metadata: [],
    };
  if (!game.standard)
    return {
      status: "unavailable",
      reason: "robert_adapter_supports_standard_four_player_guobiao_only",
      game_id: game.game_id,
      decisions: [],
      metadata: [],
    };
  const donor = options.donor_id == null ? null : String(options.donor_id);
  if (donor && !game.players.some((p) => p.user_id === donor))
    return {
      status: "unavailable",
      reason: "donor_not_in_game",
      game_id: game.game_id,
      decisions: [],
      metadata: [],
    };
  const decisions = [],
    metadata = [];
  let eligible = 0,
    unreconstructed = 0;
  const limit =
    options.max_decisions == null
      ? Infinity
      : Math.max(0, Math.floor(Number(options.max_decisions) || 0));
  for (const hand of game.hands) {
    if (!hand.reconstructed || !hand.source_verified) {
      if (hand.completed) unreconstructed++;
      continue;
    }
    for (const d of hand.decisions) {
      if (d.kind !== "discard" || (donor && d.donor_id !== donor)) continue;
      eligible++;
      if (decisions.length >= limit) continue;
      decisions.push(visibleContext(d));
      const events = hand.events
        .filter(
          (e) => e.source_event_id === d.id && e.source_kind === "discard",
        )
        .map((e) => ({
          recipient_id: e.recipient_id,
          type: e.type,
          family: e.family,
          tick_index: e.tick_index,
        }));
      metadata.push({
        id: d.id,
        game_id: game.game_id,
        hand_index: d.hand_index,
        tick_index: d.tick_index,
        donor_id: d.donor_id,
        actual_tile: d.actual_tile,
        events,
        recipient_ids: [...new Set(events.map((e) => e.recipient_id))],
      });
    }
  }
  return {
    status: "available",
    game_id: game.game_id,
    rule: game.rule,
    sub_rule: game.sub_rule,
    donor_id: donor,
    decisions,
    metadata,
    eligible_decisions: eligible,
    skipped_unreconstructed_hands: unreconstructed,
    truncated: decisions.length < eligible,
  };
}

async function analyzeRobertDiscards(row, options = {}) {
  const input = extractRobertDecisions(row, options);
  if (input.status !== "available")
    return { ...input, version: ROBERT_VERSION };
  const started = performance.now();
  let output;
  try {
    output = await runRobertBridge(input.decisions, options);
  } catch (e) {
    if (e.name === "AbortError") throw e;
    return {
      status: "unavailable",
      reason: e.message,
      version: ROBERT_VERSION,
      game_id: input.game_id,
      donor_id: input.donor_id,
      eligible_decisions: input.eligible_decisions,
      decisions: [],
    };
  }
  const byId = new Map(output.results.map((r) => [r.id, r]));
  const comparisons = input.metadata.map((m) => ({
    ...m,
    ...(byId.get(m.id) || {
      status: "unavailable",
      reason: "missing_policy_result",
    }),
  }));
  const policyAvailable = comparisons.filter((r) => r.status === "available");
  const available = policyAvailable.filter((r) => r.rate_comparable !== false);
  return {
    status: "available",
    version: ROBERT_VERSION,
    game_id: input.game_id,
    donor_id: input.donor_id,
    thresholds: output.thresholds,
    utility_note: output.utility_note,
    policy_source_sha256: output.policy_source_sha256,
    eligible_decisions: input.eligible_decisions,
    analyzed_decisions: available.length,
    policy_evaluated_decisions: policyAvailable.length,
    calculator_unavailable_decisions:
      comparisons.length - policyAvailable.length,
    unavailable_decisions: comparisons.length - available.length,
    truncated: input.truncated,
    skipped_unreconstructed_hands: input.skipped_unreconstructed_hands,
    obviously_worse: available.filter((r) => r.obviously_worse).length,
    recommended: available.filter((r) => r.is_recommended).length,
    elapsed_ms: Math.round(performance.now() - started),
    decisions: comparisons,
  };
}

function summarizeRobertPartner(comparisons, recipientId) {
  const recipient = String(recipientId),
    rows = Array.isArray(comparisons)
      ? comparisons
      : comparisons.decisions || [];
  const eligible = rows.filter(
    (r) => r.status === "available" && r.rate_comparable !== false,
  );
  const isTarget = (r) =>
    (r.events || []).some((e) => String(e.recipient_id) === recipient);
  const summarize = (rs) => ({
    decisions: rs.length,
    obviously_worse: rs.filter((r) => r.obviously_worse).length,
    recommended: rs.filter((r) => r.is_recommended).length,
    minor_difference: rs.filter((r) => !r.is_recommended && !r.obviously_worse)
      .length,
    obvious_rate: rs.length
      ? rs.filter((r) => r.obviously_worse).length / rs.length
      : null,
  });
  const normal = eligible.filter((r) => !isTarget(r));
  const channels = {};
  for (const channel of ["ALL", "MELD", "RON"])
    channels[channel] = summarize(
      eligible.filter((r) =>
        (r.events || []).some(
          (e) =>
            String(e.recipient_id) === recipient &&
            (channel === "ALL"
              ? ["MELD", "RON"].includes(e.family)
              : e.family === channel),
        ),
      ),
    );
  return {
    recipient_id: recipient,
    normal: summarize(normal),
    channels,
    unavailable_decisions: rows.length - eligible.length,
    control_definition:
      "same_analyzed_donor_game_batch_discards_with_no_actual_delivery_to_target_partner",
  };
}

module.exports = {
  ROBERT_VERSION,
  DEFAULT_THRESHOLDS,
  THRESHOLD_LIMITS,
  selectPython,
  visibleContext,
  runRobertBridge,
  extractRobertDecisions,
  analyzeRobertDiscards,
  summarizeRobertPartner,
};
