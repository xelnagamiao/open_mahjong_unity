"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict");
const {
  visibleContext,
  extractRobertDecisions,
  runRobertBridge,
  analyzeRobertDiscards,
  summarizeRobertPartner,
  selectPython,
} = require("./robert");
const {
  physicalWinningRecord,
  physicalDrawRecord,
} = require("../fixtures.test-helper");

function decision(actual = 46) {
  const hand = [11, 12, 13, 21, 22, 23, 31, 32, 33, 45, 45, 45, 46, 47],
    visible = {};
  for (const t of hand) visible[t] = (visible[t] || 0) + 1;
  return {
    id: "fixture-" + actual,
    hand,
    combination_tiles: [],
    visible,
    wall_left: 60,
    min_fan: 8,
    round_wind: 0,
    seat_wind: 0,
    flower_count: 0,
    actual_tile: actual,
  };
}

test("Python selection uses Windows python executable and honors explicit paths", () => {
  assert.equal(selectPython({}, {}, "win32"), "python");
  assert.equal(selectPython({}, {}, "linux"), "python3");
  assert.equal(
    selectPython({}, { ANTI_CHEAT_PYTHON: "configured-python" }, "win32"),
    "configured-python",
  );
  assert.equal(
    selectPython(
      { python_binary: "explicit" },
      { ANTI_CHEAT_PYTHON: "configured" },
      "win32",
    ),
    "explicit",
  );
});

test("redaction excludes opponents concealed hands, concealed kong identities and future draws", () => {
  const d = {
    id: "private",
    donor_seat: 1,
    dealer_index: 2,
    current_round: 5,
    hands: [[41], [11, 12, 13], [47], [46]],
    melds: [["G41"], [], ["g45"], []],
    public_counts: { 19: 2 },
    remaining_tiles: 31,
    hepai_limit: 8,
    flowers: [7, 2, 5, 4],
    actual_tile: 11,
    future_wall: [11, 12, 13],
  };
  const a = visibleContext(d),
    changed = structuredClone(d);
  changed.hands[0] = [12, 13, 14, 15];
  changed.hands[2] = [11, 11];
  changed.melds[0] = ["G47"];
  changed.future_wall = [45, 45, 45];
  changed.flowers[0] = 0;
  assert.deepEqual(visibleContext(changed), a);
  assert.deepEqual(a.hand, [11, 12, 13]);
  assert.equal(a.visible[41], undefined);
  assert.equal(a.visible[47], undefined);
  assert.equal(a.visible[45], 4);
  assert.equal(a.seat_wind, 3);
  assert.equal(a.round_wind, 1);
  assert.ok(!("hands" in a));
  assert.ok(!("future_wall" in a));
  assert.equal(a.flower_count, 2);
});

test("real repository policy tolerates tied best discards and uses an explicit native-unit gap", async () => {
  const out = await runRobertBridge([decision(46), decision(47), decision(45)]);
  const [a, b, worse] = out.results;
  assert.deepEqual(a.acceptable_tiles, [46, 47]);
  assert.equal(a.is_recommended, true);
  assert.equal(b.is_recommended, true);
  assert.equal(b.obviously_worse, false);
  assert.equal(worse.recommended_tile, 46);
  assert.equal(worse.obviously_worse, true);
  assert.equal(worse.gap.deciding_metric, "effective_shanten");
  assert.equal(worse.gap.unit, "shanten_steps");
  assert.equal(worse.gap.value, 1);
  assert.equal(worse.gap.threshold, 1);
  const strict = (
    await runRobertBridge([decision(45)], { thresholds: { shanten: 2 } })
  ).results[0];
  assert.equal(strict.is_recommended, false);
  assert.equal(
    strict.obviously_worse,
    false,
    "A one-step loss is below a configured two-step threshold",
  );
});

test("hidden extra input has no influence on recommendation, score and policy gap", async () => {
  const a = decision(45),
    b = {
      ...decision(45),
      opponents_hands: [
        [11, 11],
        [47, 47],
      ],
      future_draws: [45, 45, 45],
    };
  const [x, y] = (await runRobertBridge([a, b])).results;
  const stable = (r) => {
    const { elapsed_ms, ...rest } = r;
    return rest;
  };
  assert.deepEqual(stable(x), stable(y));
});

test("actual receiving events attach to their source discard and all other same-game cuts stay available as controls", async () => {
  const record = physicalWinningRecord("robert-feed");
  const extracted = extractRobertDecisions(record, { donor_id: "101" });
  assert.equal(extracted.decisions.length, 1);
  assert.equal(extracted.metadata[0].events.length, 1);
  assert.equal(extracted.metadata[0].events[0].recipient_id, "102");
  assert.equal(extracted.metadata[0].events[0].family, "RON");
  const input = physicalDrawRecord({ id: "robert-normal" });
  const result = await analyzeRobertDiscards(input, {
    donor_id: "101",
    max_decisions: 2,
  });
  assert.equal(result.status, "available");
  assert.equal(result.analyzed_decisions, 2);
  assert.equal(result.truncated, true);
  assert.ok(result.eligible_decisions > 2);
  assert.ok(result.decisions.every((d) => d.events.length === 0));
  assert.ok(result.decisions.every((d) => d.donor_id === "101"));
});

test("other rules and missing source reconstruction are not counted as normal Robert decisions", () => {
  const unsupported = physicalDrawRecord();
  unsupported.record.game_title.rule = "riichi";
  const no = extractRobertDecisions(unsupported, { donor_id: "101" });
  assert.equal(no.status, "unavailable");
  assert.equal(no.decisions.length, 0);
  const corrupt = physicalDrawRecord();
  corrupt.record.game_round.round_index_1.tiles_list[0] = 47;
  const skipped = extractRobertDecisions(corrupt, { donor_id: "101" });
  assert.equal(skipped.decisions.length, 0);
  assert.equal(skipped.skipped_unreconstructed_hands, 1);
  assert.equal(skipped.eligible_decisions, 0);
});

test("normal reference excludes every actual delivery to the target in the same analyzed batch", () => {
  const rows = [
    {
      status: "available",
      events: [{ recipient_id: "B", family: "RON" }],
      obviously_worse: true,
      is_recommended: false,
    },
    {
      status: "available",
      events: [{ recipient_id: "B", family: "MELD" }],
      obviously_worse: false,
      is_recommended: false,
    },
    {
      status: "available",
      events: [{ recipient_id: "C", family: "MELD" }],
      obviously_worse: true,
      is_recommended: false,
    },
    {
      status: "available",
      events: [],
      obviously_worse: false,
      is_recommended: true,
    },
    { status: "unavailable", events: [] },
  ];
  const x = summarizeRobertPartner(rows, "B");
  assert.equal(x.channels.ALL.decisions, 2);
  assert.equal(x.channels.RON.decisions, 1);
  assert.equal(x.channels.MELD.decisions, 1);
  assert.equal(x.normal.decisions, 2);
  assert.equal(x.normal.obviously_worse, 1);
  assert.equal(x.unavailable_decisions, 1);
  assert.equal(summarizeRobertPartner([], "B").normal.obvious_rate, null);
});
