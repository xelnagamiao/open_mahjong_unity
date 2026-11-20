"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const {
  buildExpectedPoints,
  analyzePair,
  compareRateRows,
  comparePointRows,
  matchSeatControls,
  compositeScore,
  adjustPValues,
  rankPairs,
  tTail,
  tCritical,
} = require("./statistics");

function game(index, score = 48) {
  return {
    game_id: `g-${index}`,
    created_at: new Date(Date.UTC(2026, 8, 1) + index * 3600000).toISOString(),
    rule_key: "mcr-ranked-four",
    players: [101, 102, 103, 104].map((user_id, i) => ({
      user_id,
      score: i ? -score / 3 : score,
      hands: 16,
    })),
  };
}
function sample(index, target = false) {
  const wobble = (index % 3) - 1,
    ron = (target ? 8 : 2) + wobble,
    all = 30 + wobble;
  const make = (events, received) => ({
    events,
    received,
    discards: 100,
    eligible_decisions: 100,
    opportunities: events + 2,
    realized_opportunities: events,
    choices: 90,
    choice_hits: events + 1,
  });
  return {
    game_id: `${target ? "t" : "c"}-${index}`,
    created_at: new Date(
      Date.UTC(2026, 8, 1 + (index % 5), index % 24),
    ).toISOString(),
    channels: {
      ALL: make(all, 100),
      MELD: make(all - ron, 90),
      RON: make(ron, 10),
    },
    effective_melds: target ? 7 + wobble : 2 + wobble,
    evaluated_melds: 10,
    donor_residual_per_hand: (target ? -3 : 0) + wobble / 2,
    recipient_residual_per_hand: (target ? 3 : 0) + wobble / 2,
  };
}
function pair(overrides = {}) {
  return {
    donor_id: "101",
    recipient_id: "102",
    player_count: 4,
    games: Array.from({ length: 24 }, (_, i) => sample(i, true)),
    donor_controls: Array.from({ length: 48 }, (_, i) => sample(i)),
    source_controls: Array.from({ length: 48 }, (_, i) => sample(i)),
    recipient_controls: Array.from({ length: 48 }, (_, i) => ({
      ...sample(i),
      residual_per_hand: sample(i).recipient_residual_per_hand,
    })),
    encounters: {
      shared_games: 24,
      donor_games: 100,
      recipient_games: 120,
      reference_rates: Array.from(
        { length: 40 },
        (_, i) => ((i % 10) + 1) / 100,
      ),
    },
    ...overrides,
  };
}

test("walk-forward predictions use earlier games, are zero-sum, and never use the target outcome", () => {
  const games = Array.from({ length: 40 }, (_, i) =>
    game(i, i % 3 === 0 ? -24 : 48),
  );
  const baseline = buildExpectedPoints(games);
  const changed = structuredClone(games);
  changed[25].players.forEach((p, i) => {
    p.score = i ? -100 : 300;
  });
  const rerun = buildExpectedPoints(changed);
  for (const uid of ["101", "102", "103", "104"]) {
    assert.equal(
      baseline.by_game["g-25"][uid].expected_points,
      rerun.by_game["g-25"][uid].expected_points,
    );
    assert.deepEqual(
      baseline.by_game["g-25"][uid].prediction_95,
      rerun.by_game["g-25"][uid].prediction_95,
    );
  }
  assert.notEqual(
    baseline.by_game["g-26"]["101"].expected_points,
    rerun.by_game["g-26"]["101"].expected_points,
  );
  for (const row of Object.values(baseline.by_game))
    assert.ok(
      Math.abs(Object.values(row).reduce((n, p) => n + p.expected_points, 0)) <
        1e-9,
    );
  assert.equal(baseline.by_game["g-0"]["101"].prediction_status, "cold_start");
  assert.equal(baseline.by_game["g-0"]["101"].prediction_95, null);
  assert.equal(baseline.by_game["g-30"]["101"].prediction_status, "estimated");
  assert.ok(baseline.by_game["g-30"]["101"].prediction_reference_games < 30);
  assert.ok(baseline.calibration[0].held_out_games > 0);
  assert.ok(
    baseline.calibration[0].prediction_band_coverage >= 0 &&
      baseline.calibration[0].prediction_band_coverage <= 1,
  );
});

test("timestamp ties do not leak outcomes through input order; unknown zones and non-net scores remain unavailable", () => {
  const a = game(1),
    b = game(2, -24);
  b.created_at = a.created_at;
  const first = buildExpectedPoints([a, b]),
    reversed = buildExpectedPoints([b, a]);
  assert.deepEqual(first.by_game, reversed.by_game);
  assert.equal(first.by_game["g-2"]["101"].prior_games, 0);
  const unknown = game(3);
  unknown.created_at = "2026-09-01 12:00:00";
  const nonNet = game(4);
  nonNet.players.forEach((p) => {
    p.score += 25000;
  });
  assert.equal(
    buildExpectedPoints([unknown]).rows[0].prediction_status,
    "unknown_timestamp",
  );
  assert.equal(buildExpectedPoints([nonNet]).rows[0].expected_points, null);
});

test("RON is independently abnormal while ALL is normal; effective advancement and adjusted benefit have real 95% bounds", () => {
  const result = analyzePair(pair());
  assert.equal(result.flag_ron, true);
  assert.equal(result.flag_all, false);
  assert.equal(result.flag_meld, false);
  assert.equal(result.channels.RON.source_share.value, 0.8);
  assert.equal(result.channels.ALL.source_share.value, 0.3);
  assert.ok(result.channels.RON.metrics.source_share.reference_upper_95 < 0.8);
  assert.ok(
    result.channels.RON.metrics.source_share.empirical.effect_95[0] > 0,
  );
  assert.ok(result.channels.RON.equal_share_test.effect_95[0] > 0);
  assert.equal(result.effective_delivery.flag, true);
  assert.equal(result.points.recipient.flag, true);
  assert.equal(result.points.donor_sacrifice.flag, true);
  assert.ok(result.composite.score > 40 && result.composite.score < 80);
  assert.equal(result.composite.coverage, 1);
});

test("complete games are the block: duplicating opponent rows does not create independent evidence or narrower bounds", () => {
  const p = pair(),
    base = compareRateRows(p.games, p.donor_controls, ["events", "discards"], {
      channel: "RON",
      seed: "same",
    });
  const doubled = compareRateRows(
    p.games.flatMap((x) => [x, structuredClone(x)]),
    p.donor_controls.flatMap((x) => [x, structuredClone(x)]),
    ["events", "discards"],
    { channel: "RON", seed: "same" },
  );
  assert.deepEqual(doubled, base);
  assert.equal(base.independent_games, 24);
  assert.equal(base.reference_games, 48);
});

test("8,000 discards in only two games do not produce a 95% line; missing and zero-variance residuals do not become zero-risk", () => {
  const p = pair(),
    result = analyzePair({
      ...p,
      games: p.games.slice(0, 2).map((r) => ({
        ...r,
        channels: { RON: { events: 1000, discards: 4000, received: 2000 } },
      })),
    });
  assert.equal(result.flag_ron, false);
  assert.equal(
    result.channels.RON.metrics.delivery_rate.reference_upper_95,
    null,
  );
  assert.equal(result.channels.RON.metrics.delivery_rate.independent_games, 2);
  const missing = comparePointRows(
    p.games.map((r) => ({ ...r, recipient_residual_per_hand: null })),
    p.recipient_controls,
    "recipient_residual_per_hand",
  );
  assert.equal(missing.actual.value, null);
  assert.equal(missing.p_value, null);
  const coldStart = comparePointRows(
    p.games.map((r) => ({ ...r, recipient_prediction_status: "cold_start" })),
    p.recipient_controls,
    "recipient_residual_per_hand",
  );
  assert.equal(coldStart.actual.value, null);
  assert.equal(coldStart.flag, false);
  const flat = comparePointRows(
    p.games.map((r) => ({ ...r, residual: 1 })),
    p.donor_controls.map((r) => ({ ...r, residual: 0 })),
    "residual",
  );
  assert.equal(flat.status, "insufficient");
  assert.equal(flat.reason, "degenerate_game_blocks");
});

test("saved reference bands are deterministic and unsupported delivery/team modes obey their capability gates", () => {
  const p = pair();
  assert.deepEqual(analyzePair(p), analyzePair(p));
  const unsupported = analyzePair({ ...p, delivery_supported: false });
  assert.equal(unsupported.channels.RON.status, "unavailable");
  assert.equal(unsupported.flag_ron, false);
  assert.equal(unsupported.effective_delivery.flag, false);
  const team = analyzePair({ ...p, competitive: false });
  assert.equal(team.anomaly, false);
  assert.equal(team.composite.score, null);
  assert.equal(team.ranking_eligible, false);
});

test("unverified channels and unavailable opportunity counters are missing, even when placeholder counters are zero", () => {
  const p = pair();
  const bad = p.games.map((g) => ({
    ...g,
    channels: Object.fromEntries(
      Object.entries(g.channels).map(([k, v]) => [
        k,
        { ...v, available: false },
      ]),
    ),
  }));
  const result = analyzePair({ ...p, games: bad });
  assert.equal(result.channels.RON.events, null);
  assert.equal(result.channels.RON.metrics.source_share.actual.value, null);
  assert.equal(result.channels.RON.status, "unavailable");
  assert.equal(result.flag_ron, false);
  const partial = analyzePair({
    ...p,
    games: p.games.map((g) => ({
      ...g,
      channels: Object.fromEntries(
        Object.entries(g.channels).map(([k, v]) => [
          k,
          { ...v, opportunities_available: false },
        ]),
      ),
    })),
  });
  assert.equal(
    partial.channels.RON.metrics.opportunity_creation_rate.actual.value,
    null,
  );
  assert.equal(partial.channels.RON.metrics.delivery_rate.actual.value, 0.08);
  assert.equal(partial.flag_ron, true);
});

test("composite uses a single delivery component and exposes missing weight rather than renormalizing it away", () => {
  const score = compositeScore({
    encounter: null,
    delivery: 100,
    points: null,
  });
  assert.equal(score.score, 80);
  assert.equal(score.coverage, 0.8);
  assert.deepEqual(score.score_range, [80, 100]);
  const p = pair(),
    a = analyzePair(p);
  assert.equal(
    a.composite.components.delivery.score,
    Math.max(
      a.channels.RON.score,
      a.channels.ALL.score ?? 0,
      a.channels.MELD.score ?? 0,
    ),
  );
  assert.equal(a.composite.components.delivery.weight, 0.8);
});

test("t thresholds match known small-sample values and Holm adjustment preserves ordering and nulls", () => {
  assert.ok(Math.abs(tCritical(7, 0.975) - 2.364624251) < 1e-7);
  assert.ok(Math.abs(tTail(0, 7) - 0.5) < 1e-12);
  assert.ok(Math.abs(tTail(2.364624251, 7) - 0.025) < 1e-8);
  const adjusted = adjustPValues([
    { p_value: 0.001 },
    { p_value: 0.03 },
    { p_value: 0.04 },
    { p_value: null },
  ]);
  assert.equal(adjusted[0].adjusted_p_value, 0.003);
  assert.equal(adjusted[1].adjusted_p_value, 0.06);
  assert.equal(adjusted[2].adjusted_p_value, 0.06);
  assert.equal(adjusted[3].adjusted_p_value, null);
  const ranked = rankPairs([
    analyzePair(pair()),
    analyzePair(pair({ donor_id: "other", games: pair().games.slice(0, 2) })),
  ]);
  assert.equal(ranked[0].donor_id, "101");
  assert.equal(ranked[0].rank, 1);
  assert.equal(ranked[0].adjusted_tests.RON.channel, "RON");
});

test("the displayed one-sided 95% line agrees with the raw flag while effect CI is separately two-sided", () => {
  const targets = Array.from({ length: 12 }, (_, i) => ({
    ...sample(i, true),
    residual: 0.57 + (i % 3) - 1,
  }));
  const controls = Array.from({ length: 24 }, (_, i) => ({
    ...sample(i),
    residual: (i % 3) - 1,
  }));
  const result = comparePointRows(targets, controls, "residual", {
    seed: "one-sided",
  });
  assert.ok(result.actual.value > result.reference_upper_95);
  assert.equal(result.flag, true);
  assert.ok(result.effect_95[0] < 0);
  assert.equal(result.alternative, "greater");
  assert.equal(result.effect_interval_sides, 2);
});

test("donor sacrifice preserves null rather than converting absent or cold-start residuals into zero", () => {
  const p = pair();
  const missing = analyzePair({
    ...p,
    games: p.games.map((g) => ({ ...g, donor_residual_per_hand: null })),
    donor_controls: p.donor_controls.map((g) => ({
      ...g,
      donor_residual_per_hand: null,
      residual_per_hand: 100,
    })),
  });
  assert.equal(missing.points.donor_sacrifice.actual.value, null);
  assert.equal(missing.points.donor_sacrifice.expected, null);
  assert.equal(missing.points.donor_sacrifice.flag, false);
  const cold = analyzePair({
    ...p,
    games: p.games.map((g) => ({
      ...g,
      donor_prediction_status: "cold_start",
    })),
  });
  assert.equal(cold.points.donor_sacrifice.actual.value, null);
  assert.equal(cold.points.donor_sacrifice.flag, false);
});

test("seat matching weights by actual exposed hands, excludes unknown controls, and never fabricates a fallback cohort", () => {
  const target = [
    { seat_exposure: { upstream: 9, opposite: 1, downstream: 0 } },
    { seat_exposure: { upstream: 0, opposite: 90, downstream: 0 } },
  ];
  const controls = [
    {
      game_id: "match",
      seat_exposure: { upstream: 1, opposite: 9, downstream: 0 },
    },
    {
      game_id: "wrong",
      seat_exposure: { upstream: 5, opposite: 5, downstream: 0 },
    },
    { game_id: "unknown" },
  ];
  const matched = matchSeatControls(target, controls);
  assert.deepEqual(
    matched.map((r) => r.game_id),
    ["match"],
  );
  assert.equal(matched.matching.target_exposure.upstream, 0.09);
  assert.ok(Math.abs(matched.matching.control_exposure.upstream - 0.1) < 1e-12);
  assert.equal(matchSeatControls([{}], controls).length, 0);
  assert.equal(
    matchSeatControls(target, controls.slice(1)).matching.status,
    "no_comparable_seat_exposure",
  );
});

test("ineligible channels and descriptive pairs cannot re-enter adjusted anomalies through small p-values", () => {
  const p = pair();
  const team = analyzePair({ ...p, competitive: false });
  assert.equal(team.channels.RON.p_value, null);
  const smallEvents = analyzePair(p, {
    channels: { RON: { min_events: 10000 } },
  });
  assert.equal(smallEvents.channels.RON.eligible, false);
  assert.equal(smallEvents.channels.RON.p_value, null);
  const injected = {
    donor_id: "team",
    recipient_id: "B",
    ranking_eligible: false,
    anomaly: false,
    channels: { RON: { p_value: 1e-12, eligible: true } },
    composite: { score: null },
  };
  const insufficient = {
    donor_id: "sparse",
    recipient_id: "B",
    ranking_eligible: true,
    anomaly: false,
    channels: { RON: { p_value: 1e-12, eligible: false } },
    composite: { score: null },
  };
  for (const r of rankPairs([injected, insufficient])) {
    assert.equal(r.adjusted_anomaly, false);
    assert.equal(r.adjusted_tests.RON.adjusted_p_value, null);
  }
});

test("point headline means use exactly the estimated-game cohort and expose cold-start descriptive means separately", () => {
  const p = pair();
  p.games = p.games.map((g, i) => {
    const warm = i >= 8,
      expected = warm ? 1 : -20,
      actual = warm ? 4 + ((i % 3) - 1) : 40;
    return {
      ...g,
      recipient_prediction_status: warm ? "estimated" : "cold_start",
      recipient_actual_per_hand: actual,
      recipient_expected_per_hand: expected,
      recipient_residual_per_hand: warm ? actual - expected : null,
    };
  });
  const r = analyzePair(p).points;
  assert.equal(r.inference_games, 16);
  assert.equal(r.inference_cohort, "estimated_predictions_only");
  assert.equal(r.display_cohort_complete, true);
  assert.ok(
    Math.abs(r.actual_mean - r.expected_mean - r.residual_mean) < 1e-12,
  );
  assert.equal(r.all_games, 24);
  assert.equal(r.all_descriptive_games, 24);
  assert.ok(r.all_actual_mean > r.actual_mean);
  assert.ok(
    Math.abs(r.all_actual_mean - r.all_expected_mean - r.all_residual_mean) <
      1e-12,
  );
});

function fullEffectiveSample(
  index,
  effective,
  total = 1,
  evaluated = total,
  target = false,
) {
  const row = sample(index, target);
  return {
    ...row,
    delivery_supported: true,
    effective_melds: effective,
    meld_events: total,
    evaluated_melds: evaluated,
    channels: {
      ...row.channels,
      MELD: {
        ...row.channels.MELD,
        events: total,
        discards: 100,
        available: true,
      },
    },
  };
}

test("fully evaluated zero melds are a genuine zero effective-per-discard exposure, not missing feature data", () => {
  const p = pair({
    games: Array.from({ length: 12 }, (_, i) =>
      fullEffectiveSample(i, 0, 0, 0, true),
    ),
    donor_controls: Array.from({ length: 24 }, (_, i) =>
      fullEffectiveSample(i, i % 2),
    ),
  });
  const result = analyzePair(p);
  assert.equal(result.effective_delivery.actual.value, null);
  assert.equal(result.effective_delivery_rate.actual.numerator, 0);
  assert.equal(result.effective_delivery_rate.actual.denominator, 1200);
  assert.equal(result.effective_delivery_rate.actual.value, 0);
  assert.equal(result.effective_delivery_rate.independent_games, 12);
  assert.equal(result.effective_delivery_rate.coverage.eligible_games, 12);
  assert.equal(
    result.effective_delivery_rate.denominator_definition,
    "verified_discard_exposure_in_fully_evaluated_games",
  );
});

test("partial evaluation excludes the entire game from effective-per-discard denominators and keeps conditional quality separate", () => {
  const games = Array.from({ length: 12 }, (_, i) =>
    fullEffectiveSample(i, 1, 1, 1, true),
  );
  games[0] = fullEffectiveSample(0, 1, 2, 1, true);
  const controls = Array.from({ length: 24 }, (_, i) =>
    fullEffectiveSample(i, i % 2),
  );
  const result = analyzePair(pair({ games, donor_controls: controls }));
  assert.equal(result.effective_delivery.actual.numerator, 12);
  assert.equal(result.effective_delivery_rate.actual.numerator, 11);
  assert.equal(result.effective_delivery_rate.actual.denominator, 1100);
  assert.equal(
    result.effective_delivery_rate.coverage.excluded_partial_games,
    1,
  );
  const missing = analyzePair(
    pair({
      games: games.map((g) => ({ ...g, meld_events: 2 })),
      donor_controls: controls,
    }),
  );
  assert.equal(missing.effective_delivery_rate.actual.value, null);
  assert.equal(missing.effective_delivery_rate.status, "unavailable");
  assert.equal(
    missing.effective_delivery_rate.reason,
    "no_fully_evaluated_meld_games",
  );
});

test("unsupported and unknown meld coverage do not become effective-delivery zeros", () => {
  const controls = Array.from({ length: 24 }, (_, i) =>
    fullEffectiveSample(i, i % 2),
  );
  const unsupported = analyzePair(
    pair({
      games: Array.from({ length: 12 }, (_, i) => ({
        ...fullEffectiveSample(i, 0, 0, 0, true),
        delivery_supported: false,
      })),
      donor_controls: controls,
    }),
  );
  assert.equal(unsupported.effective_delivery_rate.actual.value, null);
  assert.equal(unsupported.effective_delivery_rate.flag, false);
  assert.equal(
    unsupported.effective_delivery_rate.coverage.excluded_unsupported_games,
    12,
  );
  const unknown = analyzePair(
    pair({
      games: Array.from({ length: 12 }, (_, i) => ({
        ...fullEffectiveSample(i, 0, 0, 0, true),
        evaluated_melds: null,
      })),
      donor_controls: controls,
    }),
  );
  assert.equal(unknown.effective_delivery_rate.actual.value, null);
  assert.equal(
    unknown.effective_delivery_rate.coverage.excluded_unknown_games,
    12,
  );
});

test("competitive net-point models remain usable when only legal-delivery rules are unsupported", () => {
  const p = pair({ delivery_supported: false, competitive: true });
  const result = analyzePair(p);
  assert.equal(result.ranking_eligible, true);
  assert.equal(result.channels.RON.status, "unavailable");
  assert.equal(result.channels.RON.p_value, null);
  assert.equal(result.flag_ron, false);
  assert.equal(result.effective_delivery.flag, false);
  assert.equal(result.points.recipient.flag, true);
  assert.equal(result.points.donor_sacrifice.flag, true);
  assert.equal(result.independent_flags.POINTS, true);
  assert.equal(result.independent_flags.ENCOUNTER, false);
  assert.equal(result.anomaly, false);
  assert.equal(result.composite.coverage, 0.2);
  assert.equal(result.composite.complete, false);
  assert.ok(Math.abs(result.composite.score - 4.8) < 1e-12);
});

test("encounter empirical reference is independently visible, without manufacturing a hypothesis-test p-value", () => {
  const p = pair({
    games: pair().games.map((g) => ({
      ...g,
      donor_residual_per_hand: null,
      recipient_residual_per_hand: null,
      channels: Object.fromEntries(
        ["ALL", "MELD", "RON"].map((c) => [c, { available: false }]),
      ),
      evaluated_melds: null,
    })),
  });
  const result = analyzePair(p);
  assert.equal(result.encounter.flag, false);
  assert.equal(result.encounter.p_value, null);
  assert.equal(result.independent_flags.ENCOUNTER, false);
  assert.equal(result.anomaly, false);
  assert.equal(result.flag_all, false);
  const team = analyzePair({ ...p, competitive: false });
  assert.equal(team.independent_flags.ENCOUNTER, false);
  assert.equal(team.anomaly, false);
});

test("point controls are independent of missing seat exposure needed by delivery matching", () => {
  const p = pair();
  const result = analyzePair({
    ...p,
    donor_point_controls: p.donor_controls,
    donor_controls: [],
  });
  assert.equal(result.channels.RON.metrics.delivery_rate.reference_games, 0);
  assert.equal(result.points.donor.reference_games, 48);
  assert.equal(result.points.donor_sacrifice.flag, true);
});

test("source-share headline follows receiver denominator and reference, never discard exposure or donor history", () => {
  const p = pair();
  const first = analyzePair(p);
  assert.deepEqual(first.channels.RON.actual, {
    numerator: 192,
    denominator: 240,
    value: 0.8,
  });
  assert.equal(first.channels.RON.observed_rate, 0.8);
  assert.equal(first.channels.RON.discard_rate.value, 0.08);
  assert.equal(first.channels.RON.baseline_rate, 1 / 3);
  assert.equal(first.channels.RON.metrics.source_share.empirical.expected, 0.2);
  assert.equal(
    first.channels.RON.reference_upper_95,
    first.channels.RON.metrics.source_share.reference_upper_95,
  );
  const changedDonor = analyzePair({ ...p, donor_controls: p.games });
  assert.deepEqual(changedDonor.channels.RON.actual, first.channels.RON.actual);
  assert.equal(
    changedDonor.channels.RON.baseline_rate,
    first.channels.RON.baseline_rate,
  );
  assert.equal(
    changedDonor.channels.RON.reference_upper_95,
    first.channels.RON.reference_upper_95,
  );
  const missingReceiver = analyzePair({ ...p, source_controls: [] });
  assert.equal(missingReceiver.channels.RON.observed_rate, 0.8);
  assert.equal(missingReceiver.channels.RON.reference_upper_95, null);
  assert.equal(missingReceiver.channels.RON.score, null);
  assert.equal(missingReceiver.channels.RON.flag, false);
});

test("normal recipient source shares cannot become primary anomalies through discard or structural diagnostics", () => {
  const p = pair();
  p.games = p.games.map((row, i) => ({
    ...row,
    channels: Object.fromEntries(
      Object.entries(sample(i).channels).map(([c, v]) => [
        c,
        {
          ...v,
          discards: 40,
        },
      ]),
    ),
  }));
  const result = analyzePair(p);
  assert.equal(result.channels.ALL.flag, false);
  assert.equal(result.channels.RON.flag, false);
  assert.equal(result.channels.ALL.diagnostic_flag, true);
  assert.equal(result.effective_delivery.flag, true);
  assert.equal(
    result.composite.components.delivery.score,
    Math.max(...Object.values(result.channels).map((c) => c.score)),
  );
  assert.ok(
    result.composite.components.delivery.score <
      result.effective_delivery.score,
  );
});

test("balanced thirds retain the empirical estimate separately from the conservative equal-share floor", () => {
  const p = pair();
  p.games = p.games.map((row, i) => ({
    ...row,
    channels: {
      ALL: {
        ...row.channels.ALL,
        events: 30 + (i % 3),
        received: 3 * (30 + (i % 3)),
      },
    },
  }));
  const result = analyzePair(p);
  assert.equal(result.channels.ALL.observed_rate, 1 / 3);
  assert.equal(result.channels.ALL.baseline_rate, 1 / 3);
  assert.equal(
    result.channels.ALL.metrics.source_share.empirical.expected,
    0.3,
  );
});

test("less than equal share cannot be a delivery anomaly even when the empirical reference is much lower", () => {
  const p = pair();
  const withRon = (rows, center) =>
    rows.map((r, i) => ({
      ...r,
      channels: {
        ...r.channels,
        RON: {
          ...r.channels.RON,
          events: center + ((i % 3) - 1),
          received: 100,
        },
      },
    }));
  p.games = withRon(p.games, 30);
  p.source_controls = withRon(p.source_controls, 10);
  const ron = analyzePair(p).channels.RON;
  assert.equal(ron.metrics.source_share.empirical.flag, true);
  assert.ok(ron.metrics.source_share.empirical.reference_upper_95 < 1 / 3);
  assert.ok(ron.reference_upper_95 >= 1 / 3);
  assert.equal(ron.flag, false);
  assert.equal(ron.score, 0);
  assert.ok(ron.p_value > 0.5);
  assert.equal(
    ron.p_value,
    Math.max(ron.empirical.p_value, ron.equal_share_test.p_value),
  );
  assert.deepEqual(ron.reliability, {
    games: 24,
    dates: 6,
    reference_games: 48,
    status: "estimated",
  });
});

test("the matched high-source baseline protects seat-specific supply and all normal sources score zero", () => {
  const p = pair();
  const convert = (rows) =>
    rows.map((r, i) => ({
      ...r,
      channels: {
        ALL: { ...r.channels.ALL, events: 98 + (i % 3), received: 100 },
        MELD: { ...r.channels.MELD, events: 98 + (i % 3), received: 100 },
        RON: { ...r.channels.RON, events: 98 + (i % 3), received: 100 },
      },
    }));
  p.games = convert(p.games);
  p.source_controls = convert(p.source_controls);
  const result = analyzePair(p);
  assert.ok(result.channels.ALL.reference_upper_95 > 0.99);
  assert.equal(result.channels.ALL.baseline_rate, 0.99);
  assert.equal(result.channels.ALL.score, 0);
  assert.equal(result.anomaly, false);
  assert.equal(result.composite.components.delivery.score, 0);
  assert.ok(result.composite.score <= 20);
});

test("weights remain 80/20, point-only effects never make a primary anomaly, and coplay uses the larger observed direction", () => {
  const p = pair();
  p.games = p.games.map((r, i) => ({ ...r, channels: sample(i).channels }));
  p.encounters = {
    shared_games: 24,
    donor_games: 24,
    recipient_games: 240,
    reference_rates: [],
  };
  const result = analyzePair(p, {
    weights: { encounter: 0.1, delivery: 0.1, points: 0.8 },
  });
  assert.equal(result.points.flag, true);
  assert.equal(result.anomaly, false);
  assert.equal(result.encounter.flag, false);
  assert.equal(result.encounter.score, 100);
  assert.equal(result.composite.score, 20);
  assert.equal(result.composite.components.delivery.weight, 0.8);
  assert.equal(result.composite.components.encounter.contribution, 20);
  assert.equal(result.composite.components.points.contribution, 0);
});

test("delivery priority measures excess above the actual displayed line and insufficient shared games stay unknown", () => {
  const p = pair(),
    result = analyzePair(p),
    ron = result.channels.RON;
  assert.equal(
    ron.score,
    (100 * (ron.observed_rate - ron.reference_upper_95)) /
      (1 - ron.reference_upper_95),
  );
  assert.equal(
    ron.flag,
    ron.observed_rate > ron.reference_upper_95 && ron.p_value <= 0.05,
  );
  const small = analyzePair({ ...p, games: p.games.slice(0, 2) }).channels.RON;
  assert.equal(small.score, null);
  assert.equal(small.reference_upper_95, null);
  assert.equal(small.reliability.games, 2);
  const unknown = analyzePair({ ...p, player_count: undefined }).channels.RON;
  assert.equal(unknown.reference_upper_95, null);
  assert.equal(unknown.reason, "unknown_player_count");
});
