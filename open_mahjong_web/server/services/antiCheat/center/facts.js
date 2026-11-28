"use strict";

const path = require("node:path");
const { spawn } = require("node:child_process");
const { replayRecord, hash, relation } = require("../replay");
const { evaluateDecisions } = require("../rules");
const { selectPython } = require("../python");
const { getSharedWorkerPool } = require("../pythonWorkerPool");
const { parseTimestamp } = require("../time");
const {
  createWindowState,
  accumulateGame,
  verifyOpportunities,
} = require("../engine");

const FEATURE_VERSION = "center-facts-v3";
const CHANNELS = ["ALL", "MELD", "RON"];
const number = (value) =>
  value == null || value === "" || !Number.isFinite(Number(value))
    ? null
    : Number(value);
const emptyChannel = () => ({
  events: 0,
  received: 0,
  discards: 0,
  eligible_decisions: 0,
  opportunities: 0,
  realized_opportunities: 0,
  choices: 0,
  choice_hits: 0,
  priority_censored: 0,
  subtypes: { CHI: 0, PENG: 0, GANG: 0, RON: 0 },
  available: false,
  opportunities_available: false,
});

function inputFingerprint(row) {
  // Include analytical metadata changes, but not mutable display names or cosmetics.
  return hash({
    record:
      typeof row.record === "string" ? JSON.parse(row.record) : row.record,
    created_at:
      row.created_at instanceof Date
        ? row.created_at.toISOString()
        : row.created_at,
    timezone: row.created_at_timezone ?? null,
    rule: row.rule,
    sub_rule: row.sub_rule,
    match_type: row.match_type,
    room_type: row.room_type,
    match_tier: row.match_tier,
    event_id: row.event_id,
    metadata_conflict: row.metadata_conflict,
    players: (row.players || [])
      .map((p) =>
        Object.fromEntries(
          [
            "user_id",
            "original_player_index",
            "score",
            "rank",
            "total_round_score",
            "total_rounds",
            "pt_change",
          ].map((k) => [k, p[k] ?? null]),
        ),
      )
      .sort((a, b) => String(a.user_id).localeCompare(String(b.user_id))),
  });
}

async function runFeatureBridge(payload, options = {}) {
  if (!payload.melds.length && !payload.donors.length)
    return { melds: {}, donors: {} };
  const pythonBinary = selectPython(options);
  if (
    options.persistent !== false &&
    process.env.ANTI_CHEAT_PERSISTENT_WORKERS !== "0"
  ) {
    const maxWorkers = Math.max(
      1,
      Math.min(
        8,
        Number(options.concurrency || process.env.ANTI_CHEAT_ANALYSIS_CONCURRENCY) || 4,
      ),
    );
    const pool = getSharedWorkerPool(`analysis:${pythonBinary}`, {
      scriptPath: path.join(__dirname, "analysisWorker.py"),
      pythonBinary,
      args: [],
      maxWorkers,
      maxJobsPerWorker: 300,
      idleTimeoutMs: 60_000,
      maxLineBytes: 64 * 1024 * 1024,
      errorPrefix: "feature_calculator",
    });
    const result = await pool.exec(
      { op: "features", payload },
      { timeoutMs: options.calculator_timeout_ms || 60000 },
    );
    if (!result || !result.melds || !result.donors) {
      throw new Error("feature_calculator_invalid_output");
    }
    return result;
  }
  return new Promise((resolve, reject) => {
    const child = spawn(
      pythonBinary,
      [path.join(__dirname, "featureBridge.py")],
      { stdio: ["pipe", "pipe", "pipe"], windowsHide: true },
    );
    let output = "",
      errorOutput = "",
      settled = false;
    const finish = (error, result) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      error ? reject(error) : resolve(result);
    };
    const timer = setTimeout(() => {
      child.kill();
      finish(new Error("feature_calculator_timeout"));
    }, options.calculator_timeout_ms || 60000);
    child.on("error", (error) =>
      finish(
        new Error(`feature_calculator_unavailable:${error.code || "spawn"}`),
      ),
    );
    child.stdout.on("data", (data) => {
      output += data.toString("utf8");
      if (output.length > 16 * 1024 * 1024) {
        child.kill();
        finish(new Error("feature_calculator_output_limit"));
      }
    });
    child.stderr.on("data", (data) => {
      if (errorOutput.length < 300) errorOutput += data.toString("utf8");
    });
    child.stdin.on("error", () => {});
    child.on("close", (code) => {
      if (code !== 0)
        return finish(new Error(`feature_calculator_exit_${code}`));
      try {
        const result = JSON.parse(output);
        if (!result.melds || !result.donors) throw new Error();
        finish(null, result);
      } catch {
        finish(new Error("feature_calculator_invalid_output"));
      }
    });
    child.stdin.end(JSON.stringify(payload));
  });
}

function featureInputs(game) {
  const melds = [],
    donors = new Map();
  for (const hand of game.hands) {
    if (!hand.reconstructed || !hand.source_verified) continue;
    const byId = new Map(hand.decisions.map((d) => [d.id, d]));
    for (const event of hand.events) {
      const before = byId.get(event.source_event_id);
      if (!before || before.kind !== "discard") continue;
      donors.set(before.id, { id: before.id, before });
      if (event.family !== "MELD" || !hand.opportunity_valid) continue;
      const after = hand.decisions.find(
        (d) =>
          d.tick_index > event.tick_index &&
          d.kind === "discard" &&
          d.donor_id === event.recipient_id,
      );
      if (!after) continue;
      melds.push({
        id: `${event.source_event_id}:${event.tick_index}:${event.recipient_id}`,
        before,
        after,
        recipient_seat: event.recipient_seat,
        type: event.type,
      });
    }
  }
  return { melds, donors: [...donors.values()] };
}

function blankPair(game, donor, recipient, hands) {
  const seat = { upstream: 0, opposite: 0, downstream: 0 };
  for (const hand of game.hands.filter((h) => h.completed && h.seats)) {
    seat[
      relation(
        hand.seats[donor.original_index],
        hand.seats[recipient.original_index],
        game.count,
      )
    ] += 1;
  }
  return {
    donor_id: donor.user_id,
    recipient_id: recipient.user_id,
    donor_name: donor.name,
    recipient_name: recipient.name,
    shared_games: 1,
    shared_hands: hands,
    seat_exposure: seat,
    channels: Object.fromEntries(CHANNELS.map((c) => [c, emptyChannel()])),
    delivery_supported: false,
    source_verified_hands: 0,
    special_events: 0,
    actual_by_seat: Object.fromEntries(
      Object.keys(seat).map((k) => [k, { ALL: 0, MELD: 0, RON: 0 }]),
    ),
  };
}

async function extractGameFacts(row, options = {}) {
  const game = replayRecord(row);
  if (game.error)
    throw Object.assign(new Error(`牌谱无效：${game.error}`), {
      code: game.error,
    });
  const at = parseTimestamp(
    game.created_at,
    game.created_at_timezone ||
      options.source_timezone ||
      process.env.ANTI_CHEAT_SOURCE_TIMEZONE,
  );
  if (!at)
    throw Object.assign(new Error("牌谱时间缺失或时区不明确"), {
      code: "unknown_timestamp_zone",
    });
  game.time_ms = Date.parse(at);
  const completed = game.hands.filter((h) => h.completed);
  if (!completed.length)
    throw Object.assign(new Error("牌谱没有已结束小局"), {
      code: "no_completed_hands",
    });
  const decisions = game.hands.flatMap((h) => h.decisions);
  const calculation = await evaluateDecisions(decisions, options);
  verifyOpportunities([game], calculation);
  // The legacy reducer's flag means no detected contradiction, not complete
  // calculator coverage. Count full RON/ALL verification only when all responses exist.
  const opportunityHands = game.hands.filter(
    (h) =>
      h.opportunity_valid &&
      h.decisions.some((d) => d.kind === "discard") &&
      h.decisions
        .filter((d) => d.kind === "discard")
        .every((d) => {
          const result = calculation.results.get(d.id);
          return (
            result?.status === "available" &&
            result.recipients?.length === 3 &&
            result.recipients.every(
              (r) =>
                Array.isArray(r.helpful?.ALL) && Array.isArray(r.helpful?.RON),
            )
          );
        }),
  ).length;
  for (const hand of game.hands) {
    if (
      game.issues.some(
        (i) =>
          i.hand_index === hand.hand_index &&
          i.code === "recorded_action_legality_mismatch",
      )
    )
      hand.source_verified = false;
  }
  const state = createWindowState(3650);
  accumulateGame(state, game, calculation, {
    as_of_ms: game.time_ms,
    max_evidence_per_pair: 100000,
  });
  const players = game.players.map((p) => {
    const metadata =
      (row.players || []).find((m) => String(m.user_id) === p.user_id) || {};
    const rating = game.title.rating_result?.[p.user_id] || {};
    const metadataHands = number(metadata.total_rounds);
    const hands =
      !game.standard && metadataHands !== null && metadataHands > 0
        ? metadataHands
        : completed.length;
    let score = null,
      scoreSource = "unavailable";
    if (game.standard && game.scores_available) {
      score = game.score_deltas[p.user_id];
      scoreSource = "verified_replay_net_points";
    } else if (
      !game.standard &&
      number(metadata.total_round_score) !== null &&
      metadataHands > 0
    ) {
      score = number(metadata.total_round_score);
      scoreSource = "saved_metric_net_points";
    }
    const rankBefore = rating.rank_before ?? metadata.rank_before ?? null;
    return {
      user_id: p.user_id,
      name: String(metadata.username ?? p.name),
      original_index: p.original_index,
      score,
      historical_score: number(metadata.score),
      hands,
      points_per_hand: score === null ? null : score / hands,
      score_per_hand: score === null ? null : score / hands,
      score_source: scoreSource,
      score_unit: "net_points",
      normalized_score_unit: "net_points_per_completed_hand",
      rank_before: typeof rankBefore === "string" ? rankBefore : null,
      score_before: number(rating.score_before),
      elo_before: number(rating.elo_before),
      rating_source:
        rankBefore != null || number(rating.elo_before) !== null
          ? "saved_pre_game"
          : "prior_performance_required",
    };
  });
  const playerById = new Map(players.map((p) => [p.user_id, p]));
  const rawPairs = [...state.pairMap.values()];
  const pairByIds = new Map(
    rawPairs.map((p) => [`${p.donor_id}:${p.recipient_id}`, p]),
  );
  for (const donor of game.players)
    for (const recipient of game.players) {
      if (donor.user_id === recipient.user_id) continue;
      const key = `${donor.user_id}:${recipient.user_id}`;
      if (!pairByIds.has(key)) {
        const pair = blankPair(
          game,
          donor,
          recipient,
          Math.min(
            playerById.get(donor.user_id).hands,
            playerById.get(recipient.user_id).hands,
          ),
        );
        rawPairs.push(pair);
        pairByIds.set(key, pair);
      }
    }
  let features = { melds: {}, donors: {} };
  const featureErrors = [];
  const inputs = featureInputs(game);
  if (options.compute_features !== false) {
    try {
      features = await (options.feature_runner || runFeatureBridge)(
        inputs,
        options,
      );
    } catch (error) {
      featureErrors.push(error.message);
    }
  } else featureErrors.push("effective_feature_calculation_disabled");
  const allEvidence = [];
  for (const hand of game.hands) {
    if (!hand.source_verified) continue;
    for (const event of hand.events) {
      const id = `${event.source_event_id}:${event.tick_index}:${event.recipient_id}`;
      const feature = event.family === "MELD" ? features.melds[id] : null;
      const cost = features.donors[event.source_event_id];
      allEvidence.push({
        ...event,
        id,
        effective_feature:
          feature ||
          (event.family === "MELD"
            ? {
                status: "unavailable",
                reason: "complete_post_claim_discard_or_calculator_missing",
              }
            : null),
        donor_cost: cost || {
          status: "unavailable",
          reason: "reconstructed_donor_decision_or_calculator_missing",
        },
      });
    }
  }
  const pairs = rawPairs.map((raw) => {
    const own = allEvidence.filter(
      (e) => e.donor_id === raw.donor_id && e.recipient_id === raw.recipient_id,
    );
    const evaluated = own.filter(
      (e) => e.family === "MELD" && e.effective_feature?.status === "available",
    );
    const evaluatedCosts = own.filter(
      (e) => e.donor_cost?.status === "available",
    );
    const ron = own.filter((e) => e.family === "RON");
    const paymentKnown =
      raw.delivery_supported &&
      raw.source_verified_hands > 0 &&
      ron.every((e) => Array.isArray(e.payments) && e.score_changes);
    const direct = paymentKnown
      ? ron.reduce(
          (n, e) =>
            n +
            e.payments
              .filter(
                (p) =>
                  p.payer_id === raw.donor_id &&
                  p.payee_id === raw.recipient_id,
              )
              .reduce((a, p) => a + p.amount, 0),
          0,
        )
      : null;
    const gain = paymentKnown
      ? ron.reduce((n, e) => n + e.score_changes[raw.recipient_id], 0)
      : null;
    const result = {
      donor_id: raw.donor_id,
      recipient_id: raw.recipient_id,
      donor_name: playerById.get(raw.donor_id).name,
      recipient_name: playerById.get(raw.recipient_id).name,
      shared_games: 1,
      shared_hands: raw.shared_hands,
      source_verified_hands: raw.source_verified_hands,
      delivery_supported: raw.delivery_supported,
      seat_exposure: raw.seat_exposure,
      actual_by_seat: raw.actual_by_seat,
      channels: Object.fromEntries(
        CHANNELS.map((c) => [
          c,
          {
            ...raw.channels[c],
            available:
              !!raw.delivery_supported && raw.source_verified_hands > 0,
            opportunities_available: raw.channels[c].eligible_decisions > 0,
          },
        ]),
      ),
      special_events: raw.special_events,
      donor_score: playerById.get(raw.donor_id).score,
      recipient_score: playerById.get(raw.recipient_id).score,
      effective_melds: evaluated.filter((e) => e.effective_feature.effective)
        .length,
      evaluated_melds: evaluated.length,
      nonadvancing_melds: evaluated.filter(
        (e) => !e.effective_feature.effective,
      ).length,
      meld_events: own.filter((e) => e.family === "MELD").length,
      sacrifice_events: evaluatedCosts.filter(
        (e) => e.donor_cost.structural_sacrifice,
      ).length,
      evaluated_sacrifices: evaluatedCosts.length,
      structural_cost_sum: evaluatedCosts.reduce(
        (n, e) => n + e.donor_cost.shanten_cost,
        0,
      ),
      ron_direct_paid: direct,
      ron_recipient_gain: gain,
      ron_other_paid: gain === null ? null : gain - direct,
      evidence_count: own.length,
    };
    return result;
  });
  const evidenceLimit = Math.max(
    1,
    Math.min(10000, Number(options.max_evidence_per_game) || 10000),
  );
  const quality = {
    completed_hands: completed.length,
    reconstructed_hands: game.hands.filter((h) => h.reconstructed).length,
    scored_hands: game.hands.filter((h) => h.score_verified).length,
    opportunity_verified_hands: opportunityHands,
    calculator_available: calculation.calculator_available,
    calculator_errors: calculation.errors,
    feature_calculator_errors: featureErrors,
    effective_melds_evaluated: pairs.reduce((n, p) => n + p.evaluated_melds, 0),
    issues: game.issues,
    scores_available: players.every((p) => p.score !== null),
    evidence_count: allEvidence.length,
    evidence_truncated: allEvidence.length > evidenceLimit,
    delivery_supported: game.standard,
  };
  return {
    feature_version: FEATURE_VERSION,
    status: game.standard ? "ready" : "unsupported",
    game_id: game.game_id,
    input_hash: inputFingerprint(row),
    created_at: at,
    rule_key: game.rule_key,
    rule: game.rule,
    sub_rule: game.sub_rule,
    room_type: game.room_type,
    match_type: game.match_type,
    match_tier: game.match_tier,
    standard: game.standard,
    competitive: game.competitive,
    cooperative: game.cooperative,
    completed_hands: completed.length,
    players,
    pairs,
    evidence: allEvidence.slice(0, evidenceLimit),
    quality,
  };
}

module.exports = {
  extractGameFacts,
  inputFingerprint,
  runFeatureBridge,
  featureInputs,
  FEATURE_VERSION,
};
