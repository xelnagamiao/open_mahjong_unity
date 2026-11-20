"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const {
  extractGameFacts,
  inputFingerprint,
  runFeatureBridge,
} = require("./facts");
const {
  physicalDrawRecord,
  physicalWinningRecord,
} = require("../fixtures.test-helper");

function meldRecord(id, advances) {
  const hand = advances
    ? [11, 12, 14, 16, 21, 22, 24, 26, 31, 31, 45, 46, 46]
    : [11, 12, 14, 16, 21, 22, 24, 26, 31, 32, 45, 46, 46];
  const row = physicalDrawRecord({ id, recipientHand: hand, firstDiscard: 46 });
  row.record.game_round.round_index_1.action_ticks = [
    ["reset", 0],
    ["c", 46, "F"],
    ["p", 46, 1, 46, 46],
    ["c", 45, "F"],
    ["liuju"],
    ["end"],
  ];
  return row;
}
const pair = (f) =>
  f.pairs.find((p) => p.donor_id === "101" && p.recipient_id === "102");

test("one game yields durable directed counters, qualified RON and actual payments without window duplication", async () => {
  const input = physicalWinningRecord("center-qualified-win");
  input.record.game_title.rating_result = {
    102: { rank_before: "5级", score_before: 230, rank_after: "4级" },
  };
  const before = structuredClone(input);
  const facts = await extractGameFacts(input);
  assert.deepEqual(input, before);
  assert.equal(facts.pairs.length, 12);
  assert.equal(pair(facts).channels.RON.events, 1);
  assert.equal(pair(facts).channels.MELD.events, 0);
  assert.equal(pair(facts).channels.ALL.events, 1);
  assert.equal(pair(facts).channels.RON.realized_opportunities, 1);
  assert.equal(pair(facts).ron_direct_paid, 56);
  assert.equal(pair(facts).ron_recipient_gain, 72);
  assert.equal(pair(facts).ron_other_paid, 16);
  assert.equal(facts.players.find((p) => p.user_id === "102").score, 72);
  assert.equal(
    facts.players.find((p) => p.user_id === "102").points_per_hand,
    72,
  );
  assert.equal(
    facts.players.find((p) => p.user_id === "102").score_per_hand,
    72,
  );
  assert.equal(facts.status, "ready");
  assert.equal(
    facts.players.find((p) => p.user_id === "102").rank_before,
    "5级",
  );
  assert.equal(facts.quality.opportunity_verified_hands, 1);
  assert.ok(!("window_days" in pair(facts)));
  assert.equal(facts.evidence.length, 1);
});

test("a legal but nonadvancing pung is not classified as effective delivery", async () => {
  const advanced = await extractGameFacts(
    meldRecord("center-advancing-meld", true),
  );
  const neutral = await extractGameFacts(
    meldRecord("center-neutral-meld", false),
  );
  assert.equal(pair(advanced).channels.MELD.events, 1);
  assert.equal(pair(neutral).channels.MELD.events, 1);
  assert.equal(pair(advanced).evaluated_melds, 1);
  assert.equal(pair(neutral).evaluated_melds, 1);
  assert.equal(pair(advanced).effective_melds, 1);
  assert.equal(pair(neutral).effective_melds, 0);
  assert.equal(pair(neutral).nonadvancing_melds, 1);
  assert.equal(advanced.evidence[0].effective_feature.shanten_before, 3);
  assert.equal(advanced.evidence[0].effective_feature.shanten_after, 2);
  assert.equal(neutral.evidence[0].effective_feature.shanten_after, 3);
});

test("donor structural cost compares the actual cut with its available alternatives", async () => {
  const hand = [11, 12, 13, 21, 22, 23, 31, 32, 33, 45, 45, 45, 46, 47];
  const before = {
    donor_seat: 0,
    hands: [hand],
    melds: [[]],
    candidates: [...new Set(hand)],
    actual_tile: 45,
  };
  const output = await runFeatureBridge({
    melds: [],
    donors: [{ id: "cost", before }],
  });
  assert.equal(output.donors.cost.status, "available");
  assert.equal(output.donors.cost.structural_sacrifice, true);
  assert.equal(output.donors.cost.shanten_cost, 1);
  assert.deepEqual(output.donors.cost.best_choices, [46, 47]);
});

test("missing calculators expose unavailable feature coverage, not zero-risk qualified opportunity counts", async () => {
  const facts = await extractGameFacts(
    physicalWinningRecord("missing-calculator"),
    { python_binary: "__missing_center_python__" },
  );
  assert.equal(facts.quality.calculator_available, false);
  assert.equal(facts.quality.opportunity_verified_hands, 0);
  assert.equal(pair(facts).channels.RON.opportunities_available, false);
  assert.equal(pair(facts).evaluated_sacrifices, 0);
  assert.equal(facts.evidence[0].donor_cost.status, "unavailable");
  assert.ok(facts.quality.feature_calculator_errors.length);
});

test("other rules retain encounters and normalized saved net points, never final starting-score totals", async () => {
  const input = physicalDrawRecord({ id: "other-rule-points" });
  input.record.game_title.rule = "riichi";
  input.record.game_title.sub_rule = "riichi/standard";
  input.players = [101, 102, 103, 104].map((uid, i) => ({
    user_id: uid,
    score: 25000 + [1200, -600, -300, -300][i],
    total_round_score: [1200, -600, -300, -300][i],
    total_rounds: 4,
  }));
  const facts = await extractGameFacts(input);
  assert.equal(facts.pairs.length, 12);
  assert.equal(facts.players[0].historical_score, 26200);
  assert.equal(facts.players[0].score, 1200);
  assert.equal(facts.players[0].points_per_hand, 300);
  assert.equal(pair(facts).delivery_supported, false);
  assert.equal(facts.status, "unsupported");
  assert.equal(pair(facts).channels.RON.available, false);
  assert.equal(facts.players[0].rank_before, null);
});

test("input fingerprints change for historical analytical corrections but not display name changes", () => {
  const input = physicalWinningRecord("fingerprint");
  input.players = [{ user_id: 101, score: -56, username: "before" }];
  const changedName = structuredClone(input);
  changedName.players[0].username = "after";
  assert.equal(inputFingerprint(input), inputFingerprint(changedName));
  const changedScore = structuredClone(input);
  changedScore.players[0].score = -55;
  assert.notEqual(inputFingerprint(input), inputFingerprint(changedScore));
  const changedAction = structuredClone(input);
  changedAction.record.game_round.round_index_1.action_ticks[1][1] = 34;
  assert.notEqual(inputFingerprint(input), inputFingerprint(changedAction));
});

test("unknown timestamps and metadata conflicts fail explicitly before publication", async () => {
  await assert.rejects(
    extractGameFacts({
      ...physicalWinningRecord("bad-time"),
      created_at: null,
      record: {
        ...physicalWinningRecord().record,
        game_title: {
          ...physicalWinningRecord().record.game_title,
          end_time: null,
          start_time: null,
        },
      },
    }),
    { code: "unknown_timestamp_zone" },
  );
  await assert.rejects(
    extractGameFacts({
      ...physicalWinningRecord("bad-identity"),
      metadata_conflict: true,
    }),
    { code: "metadata_conflict" },
  );
});
