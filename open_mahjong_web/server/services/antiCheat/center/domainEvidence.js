"use strict";

const CHANNELS = ["ALL", "MELD", "RON"];
const num = (v) =>
  v === null || v === undefined || v === "" || typeof v === "boolean"
    ? null
    : Number.isFinite(Number(v))
      ? Number(v)
      : null;
const sum = (xs) => xs.reduce((a, b) => a + b, 0);
const mean = (xs) => (xs.length ? sum(xs) / xs.length : null);
const ratio = (n, d) => ({
  numerator: n,
  denominator: d,
  value: d > 0 ? n / d : null,
});
const id = (v) => (v == null ? "" : String(v));
function date(value) {
  if (typeof value !== "string" || !/(Z|[+-]\d\d:?\d\d)$/i.test(value))
    return null;
  const stamp = Date.parse(value);
  return Number.isFinite(stamp)
    ? new Date(stamp + 8 * 3600000).toISOString().slice(0, 10)
    : null;
}
function unique(rows, key) {
  const map = new Map();
  for (const row of rows) if (!map.has(key(row))) map.set(key(row), row);
  return [...map.values()];
}
function channelSummary(rows, channel) {
  const available = rows.filter(
    (r) =>
      r.delivery_supported !== false &&
      r.channels?.[channel]?.available === true,
  );
  const metric = (source, n, d) => {
    const covered = source.filter(
      (r) =>
        num(r.channels[channel][n]) !== null &&
        num(r.channels[channel][d]) !== null &&
        r.channels[channel][n] >= 0 &&
        r.channels[channel][d] >= r.channels[channel][n],
    );
    return {
      ...ratio(
        sum(covered.map((r) => Number(r.channels[channel][n]))),
        sum(covered.map((r) => Number(r.channels[channel][d]))),
      ),
      games: new Set(covered.map((r) => id(r.game_id))).size,
    };
  };
  const opportunities = available.filter(
    (r) => r.channels[channel].opportunities_available === true,
  );
  return {
    source_share: metric(available, "events", "received"),
    per_discard: metric(available, "events", "discards"),
    opportunity_creation: metric(
      opportunities,
      "opportunities",
      "eligible_decisions",
    ),
    opportunity_realization: metric(
      opportunities,
      "realized_opportunities",
      "opportunities",
    ),
    mixed_choice_hit: metric(opportunities, "choice_hits", "choices"),
    covered_pair_games: available.length,
    covered_games: new Set(available.map((r) => id(r.game_id))).size,
    missing_pair_games: rows.length - available.length,
    status: available.length ? "observed" : "unavailable",
  };
}
function structural(rows) {
  const known = rows.filter((r) => r.delivery_supported !== false);
  const counts = (n, d) => {
    const valid = known.filter(
      (r) =>
        num(r[n]) !== null && num(r[d]) !== null && r[n] >= 0 && r[d] >= r[n],
    );
    return ratio(
      sum(valid.map((r) => Number(r[n]))),
      sum(valid.map((r) => Number(r[d]))),
    );
  };
  const cost = known.filter(
    (r) =>
      num(r.structural_cost_sum) !== null && num(r.evaluated_sacrifices) > 0,
  );
  return {
    advancing_melds: counts("effective_melds", "evaluated_melds"),
    meld_evaluation_coverage: counts("evaluated_melds", "meld_events"),
    structural_cost_events: counts("sacrifice_events", "evaluated_sacrifices"),
    mean_shanten_cost: cost.length
      ? sum(cost.map((r) => Number(r.structural_cost_sum))) /
        sum(cost.map((r) => Number(r.evaluated_sacrifices)))
      : null,
    interpretation: "structural_change_only_not_intent_or_strategy_value",
  };
}
function cohort(rows) {
  return {
    games: new Set(rows.map((r) => id(r.game_id))).size,
    pair_games: rows.length,
    channels: Object.fromEntries(
      CHANNELS.map((c) => [c, channelSummary(rows, c)]),
    ),
    structural: structural(rows),
    interpretation:
      "descriptive_opponent_exposure_weighted_comparison_not_normal_policy",
  };
}

/** Pure summary of already extracted facts. No replay, fit, source read or write. */
function summarizeEvidence(input = {}) {
  const initial = input.targetRows || [],
    donor = id(input.donor_id ?? initial[0]?.donor_id),
    recipient = id(input.recipient_id ?? initial[0]?.recipient_id);
  const rule = input.rule_key ?? initial[0]?.rule_key ?? null;
  const gameMap = new Map((input.games || []).map((g) => [id(g.game_id), g]));
  const poolOf = (r) =>
    r.rule_key ?? gameMap.get(id(r.game_id))?.rule_key ?? null;
  const validPool = (r) =>
    rule === null ? poolOf(r) === null : poolOf(r) === rule;
  const normalize = (rows) =>
    unique(
      rows.filter((r) => id(r.game_id) && validPool(r)),
      (r) =>
        JSON.stringify([id(r.game_id), id(r.donor_id), id(r.recipient_id)]),
    );
  const target = normalize(initial).filter(
    (r) => id(r.donor_id) === donor && id(r.recipient_id) === recipient,
  );
  const other = normalize(input.otherRows || []);
  const shared = new Set(target.map((r) => id(r.game_id)));
  const absent = (row, uid) => {
    const players = gameMap.get(id(row.game_id))?.players;
    return (
      Array.isArray(players) &&
      players.length > 0 &&
      players.every((p) => id(p.user_id) !== uid)
    );
  };
  const cohorts = {
    same_table_donor_other: other.filter(
      (r) =>
        shared.has(id(r.game_id)) &&
        id(r.donor_id) === donor &&
        id(r.recipient_id) !== recipient,
    ),
    same_table_recipient_other: other.filter(
      (r) =>
        shared.has(id(r.game_id)) &&
        id(r.recipient_id) === recipient &&
        id(r.donor_id) !== donor,
    ),
    donor_without_recipient: other.filter(
      (r) =>
        !shared.has(id(r.game_id)) &&
        id(r.donor_id) === donor &&
        absent(r, recipient),
    ),
    recipient_without_donor: other.filter(
      (r) =>
        !shared.has(id(r.game_id)) &&
        id(r.recipient_id) === recipient &&
        absent(r, donor),
    ),
  };
  const dates = new Map();
  let unknownDates = 0;
  for (const row of target) {
    const day = date(
      row.created_at ?? gameMap.get(id(row.game_id))?.created_at,
    );
    if (!day) {
      unknownDates++;
      continue;
    }
    if (!dates.has(day))
      dates.set(day, { date: day, games: 0, ALL: 0, MELD: 0, RON: 0 });
    const d = dates.get(day);
    d.games++;
    for (const c of CHANNELS)
      if (
        row.delivery_supported !== false &&
        row.channels?.[c]?.available === true
      )
        d[c] += num(row.channels[c].events) ?? 0;
  }
  const events = unique(
    (input.evidence || []).filter(
      (e) =>
        shared.has(id(e.game_id)) &&
        id(e.donor_id) === donor &&
        id(e.recipient_id) === recipient,
    ),
    (e) =>
      e.id ||
      JSON.stringify([e.game_id, e.hand_index, e.tick_index, e.recipient_id]),
  );
  const decisionSummary = (c) => {
    const pairId = input.pair_id ?? target[0]?.pair_id;
    const cached = unique(
      (input.decisionCounts || []).filter((row) => shared.has(id(row.game_id)) && pairId && row.pair_id === pairId && row.payload?.channels?.[c]),
      (row) => id(row.game_id),
    );
    const cachedGames = new Set(cached.map((row) => id(row.game_id)));
    const selected = events.filter((e) =>
      c === "ALL" ? ["MELD", "RON"].includes(e.family) : e.family === c,
    );
    const counts = {
      with_nonhelpful_alternative: 0,
      sole_legal_candidate: 0,
      all_candidates_help: 0,
      unknown: 0,
    };
    let cachedSamples = 0;
    for (const row of cached) {
      const value = row.payload?.channels?.[c];
      if (!value) continue;
      for (const key of Object.keys(counts)) counts[key] += num(value[key]) ?? 0;
      cachedSamples += num(value.sampled_events) ?? 0;
    }
    for (const e of selected) {
      if (cachedGames.has(id(e.game_id))) continue;
      const legal = Array.isArray(e.legal_choices)
        ? [...new Set(e.legal_choices)]
        : null;
      const helpful = Array.isArray(e.helpful_choices)
        ? new Set(e.helpful_choices)
        : null;
      if (
        !legal?.length ||
        !helpful ||
        ![...legal, ...helpful, e.tile].every((v) => typeof v === "number" && Number.isFinite(v)) ||
        !legal.includes(e.tile) ||
        !helpful.has(e.tile)
      )
        counts.unknown++;
      else if (legal.length === 1) counts.sole_legal_candidate++;
      else if (legal.every((t) => helpful.has(t))) counts.all_candidates_help++;
      else counts.with_nonhelpful_alternative++;
    }
    const countedEvents = target.filter(
      (r) =>
        r.delivery_supported !== false &&
        r.channels?.[c]?.available === true &&
        num(r.channels[c].events) !== null,
    );
    return {
      ...counts,
      sampled_events: cachedSamples + selected.filter((e) => !cachedGames.has(id(e.game_id))).length,
      cached_games: cached.length,
      recorded_events: countedEvents.length
        ? sum(countedEvents.map((r) => Number(r.channels[c].events)))
        : null,
      scope: "provided_successful_event_evidence_only_not_all_decisions",
    };
  };
  const pointRows = unique(
    (input.pointRows || []).filter(
      (r) => id(r.game_id) && validPool(r) && id(r.user_id) === recipient,
    ),
    (r) => id(r.game_id),
  );
  const pointSummary = (rows) => {
    const eligible = rows.filter(
      (r) =>
        r.prediction_status === "estimated" &&
        [r.actual_per_hand, r.expected_per_hand, r.residual_per_hand].every(
          (v) => num(v) !== null,
        ) &&
        Math.abs(
          Number(r.actual_per_hand) -
            Number(r.expected_per_hand) -
            Number(r.residual_per_hand),
        ) < 1e-6,
    );
    return {
      games: rows.length,
      estimated_games: eligible.length,
      actual_mean: mean(eligible.map((r) => Number(r.actual_per_hand))),
      expected_mean: mean(eligible.map((r) => Number(r.expected_per_hand))),
      residual_mean: mean(eligible.map((r) => Number(r.residual_per_hand))),
    };
  };
  const together = pointSummary(
    pointRows.filter((r) => shared.has(id(r.game_id))),
  );
  const apart = pointSummary(
    pointRows.filter((r) => !shared.has(id(r.game_id)) && absent(r, donor)),
  );
  const paymentRows = target.filter(
    (r) =>
      r.delivery_supported !== false &&
      [r.ron_direct_paid, r.ron_recipient_gain, r.ron_other_paid].every(
        (v) => num(v) !== null,
      ),
  );
  return {
    version: "domain-evidence-v1",
    direction: { donor_id: donor, recipient_id: recipient, rule_key: rule },
    coverage: {
      target_games: target.length,
      excluded_target_rows: initial.length - target.length,
      provided_evidence_events: events.length,
      unknown_dates: unknownDates,
    },
    channels: Object.fromEntries(
      CHANNELS.map((c) => [c, channelSummary(target, c)]),
    ),
    comparisons: Object.fromEntries(
      Object.entries(cohorts).map(([name, rows]) => [name, cohort(rows)]),
    ),
    structural: structural(target),
    payments: {
      covered_games: paymentRows.length,
      missing_games: target.length - paymentRows.length,
      donor_paid: paymentRows.length
        ? sum(paymentRows.map((r) => Number(r.ron_direct_paid)))
        : null,
      recipient_gained: paymentRows.length
        ? sum(paymentRows.map((r) => Number(r.ron_recipient_gain)))
        : null,
      other_players_paid: paymentRows.length
        ? sum(paymentRows.map((r) => Number(r.ron_other_paid)))
        : null,
      scope: "ordinary_ron_only_net_points",
    },
    recurrence: {
      days: dates.size,
      daily: [...dates.values()].sort((a, b) => a.date.localeCompare(b.date)),
      days_with_events: Object.fromEntries(
        CHANNELS.map((c) => [
          c,
          [...dates.values()].filter((d) => d[c] > 0).length,
        ]),
      ),
      independent_sessions: null,
      interpretation:
        "dates_are_not_independent_sessions_or_cheating_confidence",
    },
    decisions: Object.fromEntries(CHANNELS.map((c) => [c, decisionSummary(c)])),
    points: {
      shared: together,
      apart,
      residual_difference:
        together.residual_mean !== null && apart.residual_mean !== null
          ? together.residual_mean - apart.residual_mean
          : null,
      unit: "net_points_per_hand",
      interpretation:
        "descriptive_same_pool_earlier_game_predictions_not_causal_partner_effect",
    },
    limitations: [
      "no_hidden_hand_normal_policy",
      "no_strategy_or_intent_inference_from_shanten",
      "no_fixed_equal_share_threshold",
      "control_groups_require_context_matching_before_inference",
      "point_predictions_require_upstream_no_future_leakage",
      "successful_event_samples_do_not_describe_all_decisions",
      "missing_participant_roster_excludes_apart_controls",
    ],
  };
}
module.exports = { summarizeEvidence };
