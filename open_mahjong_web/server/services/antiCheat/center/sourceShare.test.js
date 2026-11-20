"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const { createWindowState, accumulateGame } = require("../engine");
const { replayRecord } = require("../replay");
const { simpleRecord, concentrationFixture } = require("../fixtures.test-helper");

// Exercise the exact accumulator used to persist center facts, without the
// legacy report builder or a statistics implementation reproducing the formula.
function facts(rows, transform = () => {}) {
  const state = createWindowState(3650);
  for (const row of rows) {
    const game = replayRecord(row);
    game.time_ms = Date.parse(row.created_at);
    transform(game);
    accumulateGame(state, game, { results: new Map() }, {
      as_of_ms: Date.parse("2026-11-14T00:00:00Z"), max_evidence_per_pair: 1000,
    });
  }
  return [...state.pairMap.values()];
}
const inbound = (rows) => rows.filter((r) => r.recipient_id === "102");

test("equal reception sources are each one third, not events per discard", () => {
  const rows = facts([0, 2, 3].map((donor) => simpleRecord(`equal-${donor}`, donor)));
  for (const pair of inbound(rows)) {
    assert.equal(pair.channels.RON.events, 1);
    assert.equal(pair.channels.RON.received, 3);
    assert.equal(pair.channels.ALL.events / pair.channels.ALL.received, 1 / 3);
    assert.equal(pair.channels.RON.discards, 1);
  }
  assert.equal(inbound(rows).reduce((sum, p) => sum + p.channels.RON.events, 0), 3);
  // Each directed row repeats the same recipient total: never SUM these
  // denominators to construct a player's overall reception count.
  assert.equal(inbound(rows)[0].channels.RON.received, 3);
});

test("center fact counters preserve ALL 45/150 and RON 12/15 independently", () => {
  const rows = facts(concentrationFixture());
  const pair = inbound(rows).find((p) => p.donor_id === "101");
  assert.equal(pair.channels.ALL.events, 45);
  assert.equal(pair.channels.ALL.received, 150);
  assert.equal(pair.channels.MELD.events, 33);
  assert.equal(pair.channels.MELD.received, 135);
  assert.equal(pair.channels.RON.events, 12);
  assert.equal(pair.channels.RON.received, 15);
});

test("each partner's source denominator excludes games where that partner was absent", () => {
  const absent = simpleRecord("other-partner", 0);
  absent.record.game_title.p0_uid = 105;
  const rows = inbound(facts([simpleRecord("shared", 0), absent]));
  const first = rows.find((p) => p.donor_id === "101");
  assert.equal(first.channels.RON.events, 1);
  assert.equal(first.channels.RON.received, 1);
  const persistent = rows.find((p) => p.donor_id === "103");
  assert.equal(persistent.channels.RON.events, 0);
  assert.equal(persistent.channels.RON.received, 2);
});

test("uncertain source attribution removes numerator and denominator together", () => {
  const rows = facts([simpleRecord("good", 0), simpleRecord("bad", 2)], (game) => {
    if (game.game_id === "bad") game.hands[0].source_verified = false;
  });
  const pair = inbound(rows).find((p) => p.donor_id === "101");
  assert.equal(pair.shared_games, 2);
  assert.equal(pair.channels.RON.events, 1);
  assert.equal(pair.channels.RON.received, 1);
});
