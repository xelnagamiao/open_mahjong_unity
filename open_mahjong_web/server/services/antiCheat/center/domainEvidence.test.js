"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict");
const { summarizeEvidence } = require("./domainEvidence");
function row(game, donor = "a", recipient = "b", patch = {}) {
  const counts = (events, received) => ({
    available: true,
    opportunities_available: true,
    events,
    received,
    discards: 100,
    eligible_decisions: 80,
    opportunities: 20,
    realized_opportunities: events,
    choices: 30,
    choice_hits: 10,
  });
  return {
    game_id: game,
    donor_id: donor,
    recipient_id: recipient,
    rule_key: "mcr",
    created_at: "2026-11-10T17:00:00Z",
    delivery_supported: true,
    channels: {
      ALL: counts(45, 150),
      MELD: counts(33, 135),
      RON: counts(12, 15),
    },
    effective_melds: 2,
    evaluated_melds: 3,
    meld_events: 4,
    sacrifice_events: 1,
    evaluated_sacrifices: 4,
    structural_cost_sum: 2,
    ron_direct_paid: 16,
    ron_recipient_gain: 32,
    ron_other_paid: 16,
    ...patch,
  };
}
const roster = (game, players, rule = "mcr") => ({
  game_id: game,
  rule_key: rule,
  players: players.map((user_id) => ({ user_id })),
});

test("decision counts use complete cached event aggregates without duplicating supplied examples", () => {
  const counts = { with_nonhelpful_alternative: 8, sole_legal_candidate: 2, all_candidates_help: 3, unknown: 1, sampled_events: 14 };
  const cached = (game_id, pair_id = "ab") => ({ game_id, pair_id, payload: { channels: { ALL: counts, MELD: counts } } });
  const event = (game_id) => ({ game_id, donor_id: "a", recipient_id: "b", family: "MELD", tile: 11, legal_choices: [11, 12], helpful_choices: [11] });
  const result = summarizeEvidence({ pair_id: "ab", targetRows: [row("one"), row("two")],
    decisionCounts: [cached("one"), cached("one"), cached("one", "ba"), cached("outside")],
    evidence: [event("one"), event("two")] });
  assert.equal(result.decisions.ALL.sampled_events, 15);
  assert.equal(result.decisions.ALL.with_nonhelpful_alternative, 9);
  assert.equal(result.decisions.ALL.cached_games, 1);
  assert.equal(result.decisions.ALL.recorded_events, 90);
  assert.equal(result.decisions.RON.sampled_events, 0);
  assert.match(result.decisions.ALL.scope, /not_all_decisions/);
});

test("source shares keep ALL and RON independent and do not replace them with discard rates", () => {
  const r = row("one"),
    input = { targetRows: [r] },
    original = structuredClone(input);
  const result = summarizeEvidence(input);
  assert.deepEqual(input, original);
  assert.equal(result.channels.ALL.source_share.value, 0.3);
  assert.equal(result.channels.RON.source_share.value, 0.8);
  assert.equal(result.channels.RON.per_discard.value, 0.12);
  assert.equal(result.channels.RON.mixed_choice_hit.value, 1 / 3);
  assert.ok(!("score" in result));
  assert.ok(!("flag" in result.channels.ALL));
});

test("both directions and rules remain separate, and duplicate pair rows are not new evidence", () => {
  const a = row("one");
  const result = summarizeEvidence({
    donor_id: "a",
    recipient_id: "b",
    rule_key: "mcr",
    targetRows: [
      a,
      a,
      row("one", "b", "a"),
      row("foreign", "a", "b", { rule_key: "riichi" }),
    ],
  });
  assert.equal(result.coverage.target_games, 1);
  assert.equal(result.coverage.excluded_target_rows, 3);
  assert.equal(result.channels.RON.source_share.numerator, 12);
});

test("descriptive controls distinguish same-table other recipients from absent-partner games", () => {
  const result = summarizeEvidence({
    targetRows: [row("shared")],
    games: [
      roster("shared", ["a", "b", "c", "d"]),
      roster("a-only", ["a", "c", "d", "e"]),
      roster("b-only", ["b", "c", "d", "e"]),
      roster("both-omitted", ["a", "b", "c", "d"]),
    ],
    otherRows: [
      row("shared", "a", "c"),
      row("shared", "c", "b"),
      row("a-only", "a", "c"),
      row("b-only", "c", "b"),
      row("unknown", "a", "c"),
      row("both-omitted", "a", "c"),
    ],
  });
  for (const c of Object.values(result.comparisons)) assert.equal(c.games, 1);
  assert.equal(result.comparisons.donor_without_recipient.pair_games, 1);
  assert.match(
    result.comparisons.same_table_recipient_other.interpretation,
    /not_normal_policy/,
  );
});

test("forced help and chosen help remain distinct and incomplete successful evidence stays unknown", () => {
  const e = {
    game_id: "one",
    donor_id: "a",
    recipient_id: "b",
    family: "RON",
    tile: 11,
  };
  const result = summarizeEvidence({
    targetRows: [row("one")],
    evidence: [
      { ...e, id: "choice", legal_choices: [11, 12], helpful_choices: [11] },
      { ...e, id: "sole", legal_choices: [11], helpful_choices: [11] },
      { ...e, id: "all", legal_choices: [11, 12], helpful_choices: [11, 12] },
      { ...e, id: "unknown" },
      { ...e, id: "choice", legal_choices: [11, 12], helpful_choices: [11] },
      { ...e, id: "reverse", donor_id: "b", recipient_id: "a" },
    ],
  });
  const d = result.decisions.RON;
  assert.equal(d.sampled_events, 4);
  assert.equal(d.with_nonhelpful_alternative, 1);
  assert.equal(d.sole_legal_candidate, 1);
  assert.equal(d.all_candidates_help, 1);
  assert.equal(d.unknown, 1);
  assert.match(d.scope, /not_all_decisions/);
});

test("structural improvement is not labelled intention and donor payment is not recipient total gain", () => {
  const result = summarizeEvidence({ targetRows: [row("one")] });
  assert.equal(result.structural.advancing_melds.value, 2 / 3);
  assert.equal(result.structural.meld_evaluation_coverage.value, 3 / 4);
  assert.equal(result.structural.structural_cost_events.value, 1 / 4);
  assert.match(result.structural.interpretation, /not_intent_or_strategy/);
  assert.equal(result.payments.donor_paid, 16);
  assert.equal(result.payments.recipient_gained, 32);
  assert.equal(result.payments.other_players_paid, 16);
});

test("one evening is one date, not independent repeated corroboration, and local ambiguous timestamps remain unknown", () => {
  const result = summarizeEvidence({
    targetRows: [
      row("one"),
      row("two"),
      row("three", "a", "b", { created_at: "2026-11-10 18:00:00" }),
    ],
  });
  assert.equal(result.recurrence.days, 1);
  assert.equal(result.recurrence.daily[0].date, "2026-11-11");
  assert.equal(result.recurrence.daily[0].games, 2);
  assert.equal(result.recurrence.independent_sessions, null);
  assert.equal(result.coverage.unknown_dates, 1);
});

test("net point comparison excludes cold starts, mismatched residuals and foreign rules", () => {
  const prediction = (game, actual, expected, extras = {}) => ({
    game_id: game,
    user_id: "b",
    rule_key: "mcr",
    prediction_status: "estimated",
    actual_per_hand: actual,
    expected_per_hand: expected,
    residual_per_hand: actual - expected,
    ...extras,
  });
  const result = summarizeEvidence({
    targetRows: [row("shared"), row("cold"), row("bad")],
    games: [roster("apart", ["b", "c", "d", "e"])],
    pointRows: [
      prediction("shared", 8, 3),
      prediction("cold", 100, 1, { prediction_status: "cold_start" }),
      prediction("bad", 2, 1, { residual_per_hand: 50 }),
      prediction("apart", 4, 3),
      prediction("foreign", 1000, 10, { rule_key: "riichi" }),
    ],
  });
  assert.equal(result.points.shared.estimated_games, 1);
  assert.equal(result.points.shared.actual_mean, 8);
  assert.equal(result.points.apart.residual_mean, 1);
  assert.equal(result.points.residual_difference, 4);
});

test("unavailable opportunity and rule coverage stays missing, never a measured zero", () => {
  const r = row("one", "a", "b", { delivery_supported: false });
  const result = summarizeEvidence({ targetRows: [r] });
  assert.equal(result.channels.ALL.source_share.value, null);
  assert.equal(result.channels.RON.opportunity_creation.value, null);
  assert.equal(result.structural.advancing_melds.value, null);
  assert.equal(result.payments.donor_paid, null);
});
