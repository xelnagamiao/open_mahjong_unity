"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict");
const { buildExpectedPoints } = require("./expectedPoints");
function game(i, score = 48, overrides = {}) {
  return {
    game_id: `g${i}`,
    created_at: new Date(Date.UTC(2026, 8, 1) + i * 3600000).toISOString(),
    rule_key: "mcr-ranked-four",
    competitive: true,
    players: ["a", "b", "c", "d"].map((user_id, j) => ({
      user_id,
      hands: 16,
      score: j ? -score / 3 : score,
    })),
    ...overrides,
  };
}
const history = () =>
  Array.from({ length: 50 }, (_, i) => game(i, i % 3 ? 48 : -24));

test("same-game and future outcomes never affect prior expected values or prediction bands", () => {
  const games = history(),
    original = structuredClone(games),
    baseline = buildExpectedPoints(games);
  const changed = structuredClone(games);
  changed[30] = game(30, 3000);
  changed[45] = game(45, -9000);
  const rerun = buildExpectedPoints(changed);
  assert.deepEqual(games, original);
  for (const uid of ["a", "b", "c", "d"]) {
    assert.equal(
      baseline.by_game.g30[uid].expected_points,
      rerun.by_game.g30[uid].expected_points,
    );
    assert.deepEqual(
      baseline.by_game.g30[uid].prediction_95,
      rerun.by_game.g30[uid].prediction_95,
    );
  }
  assert.deepEqual(baseline.by_game.g29, rerun.by_game.g29);
  assert.notEqual(
    baseline.by_game.g31.a.expected_points,
    rerun.by_game.g31.a.expected_points,
  );
  for (const prediction of Object.values(baseline.by_game)) {
    const rows = Object.values(prediction);
    assert.ok(Math.abs(rows.reduce((n, p) => n + p.expected_points, 0)) < 1e-8);
    for (const p of rows)
      assert.ok(
        Math.abs(p.actual_points - p.expected_points - p.residual_points) <
          1e-8,
      );
  }
});

test("timestamp ties are predicted as a batch before either result trains history", () => {
  const early = history().slice(0, 30),
    a = game(30, 48),
    b = game(31, -24, { created_at: a.created_at });
  const first = buildExpectedPoints([...early, a, b]);
  const reversed = buildExpectedPoints([...early, b, a]);
  assert.deepEqual(first, reversed);
  assert.equal(first.by_game.g30.a.prior_games, 30);
  assert.equal(first.by_game.g31.a.prior_games, 30);
  const changed = buildExpectedPoints([...early, game(30, 9000), b]);
  assert.equal(
    first.by_game.g31.a.expected_points,
    changed.by_game.g31.a.expected_points,
  );
  assert.deepEqual(
    first.by_game.g31.a.prediction_95,
    changed.by_game.g31.a.prediction_95,
  );
});

test("cold starts and empirical band coverage expose actual supported observations", () => {
  const result = buildExpectedPoints(history());
  assert.equal(result.by_game.g0.a.point_status, "cold_start");
  assert.equal(result.by_game.g0.a.coverage.minimum_table_history, 0);
  assert.equal(result.by_game.g0.a.prediction_95, null);
  assert.equal(result.by_game.g8.a.point_status, "estimated");
  assert.equal(result.by_game.g8.a.prediction_95, null);
  assert.ok(result.by_game.g30.a.coverage.prediction_band_available);
  assert.equal(result.coverage.input_games, 50);
  assert.equal(result.coverage.player_observations, 200);
  assert.equal(result.coverage.cold_start_observations, 32);
  assert.equal(result.coverage.estimated_observations, 168);
  assert.equal(result.calibration[0].independent_games, 42);
  assert.ok(
    result.calibration[0].prediction_band_coverage >= 0 &&
      result.calibration[0].prediction_band_coverage <= 1,
  );
});

test("unknown timestamps, unknown pools and non-net points do not train later predictions", () => {
  const unknown = game(0, 9000, { created_at: "2026-09-01 12:00:00" });
  const poolless = game(1, 9000, { rule_key: undefined });
  const nonNet = game(2);
  nonNet.players.forEach((p) => {
    p.score += 25000;
  });
  const invalidHands = game(3);
  invalidHands.players[0].hands = 17;
  const result = buildExpectedPoints([
    unknown,
    poolless,
    nonNet,
    invalidHands,
    game(4),
  ]);
  assert.equal(result.by_game.g0.a.prediction_status, "unknown_timestamp");
  assert.equal(result.by_game.g1.a.prediction_status, "unknown_rule_pool");
  assert.equal(
    result.by_game.g2.a.prediction_status,
    "invalid_or_non_zero_sum_points",
  );
  assert.equal(result.by_game.g3.a.expected_points, null);
  assert.equal(result.by_game.g4.a.prior_games, 0);
});

test("cooperative games and different rule pools cannot contaminate competitive history", () => {
  const rows = Array.from({ length: 12 }, (_, i) =>
    game(i, 9000, { cooperative: true }),
  );
  rows.push(game(12, 9000, { competitive: false }));
  rows.push(game(13, 9000, { rule_key: "riichi-four" }));
  rows.push(game(14));
  const result = buildExpectedPoints(rows);
  assert.equal(result.by_game.g0.a.prediction_status, "descriptive_only");
  assert.equal(result.by_game.g0.a.actual_points, 9000);
  assert.equal(result.by_game.g0.a.expected_points, null);
  assert.equal(result.by_game.g14.a.prior_games, 0);
  assert.equal(result.by_game.g14.a.expected_points, 0);
});

test("a new opponent keeps the entire table prediction cold and duplicate games are rejected", () => {
  const games = history(),
    last = game(50);
  last.players[3].user_id = "new";
  const result = buildExpectedPoints([...games, last]);
  assert.equal(result.by_game.g50.a.prediction_status, "cold_start");
  assert.equal(result.by_game.g50.a.coverage.minimum_table_history, 0);
  assert.throws(
    () => buildExpectedPoints([games[0], games[0]]),
    /duplicate_game_id/,
  );
  assert.throws(
    () => buildExpectedPoints(games, { shrinkage_games: 0 }),
    /invalid_shrinkage/,
  );
});
