"use strict";
const MODEL_VERSION = "prefix-opponent-shrinkage-v2-standalone";
const DEFAULTS = Object.freeze({
  min_history_games: 8,
  shrinkage_games: 12,
  min_reference_games: 12,
  confidence: 0.95,
});
const number = (value) =>
  value === null ||
  value === undefined ||
  value === "" ||
  typeof value === "boolean"
    ? null
    : Number.isFinite(Number(value))
      ? Number(value)
      : null;
const sum = (rows) => rows.reduce((a, b) => a + b, 0);
const mean = (rows) => (rows.length ? sum(rows) / rows.length : null);
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
function opts(options) {
  const value = { ...DEFAULTS, ...options };
  for (const key of ["min_history_games", "min_reference_games"])
    if (!Number.isInteger(value[key]) || value[key] < 1)
      throw new Error("invalid_" + key);
  if (!Number.isFinite(value.shrinkage_games) || value.shrinkage_games <= 0)
    throw new Error("invalid_shrinkage_games");
  if (value.confidence !== 0.95)
    throw new Error("prediction_confidence_must_be_95");
  return value;
}
function time(value) {
  if (value instanceof Date) return Number.isFinite(+value) ? +value : null;
  if (typeof value === "number") return Number.isFinite(value) ? value : null;
  if (typeof value !== "string" || !/(?:Z|[+-]\d\d:?\d\d)$/i.test(value))
    return null;
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}
function day(value) {
  const t = time(value);
  return t === null
    ? null
    : new Date(t + 8 * 3600000).toISOString().slice(0, 10);
}
function quantile(sorted, q) {
  if (!sorted.length) return null;
  const i = clamp(q, 0, 1) * (sorted.length - 1),
    left = Math.floor(i);
  return sorted[left] + (sorted[Math.ceil(i)] - sorted[left]) * (i - left);
}
function insert(sorted, value) {
  let low = 0,
    high = sorted.length;
  while (low < high) {
    const m = (low + high) >>> 1;
    if (sorted[m] < value) low = m + 1;
    else high = m;
  }
  sorted.splice(low, 0, value);
}
function buildExpectedPoints(games, options = {}) {
  const idsSeen = new Set();
  for (const game of games) {
    if (game.game_id == null || String(game.game_id) === "")
      throw new Error("missing_game_id");
    if (idsSeen.has(String(game.game_id))) throw new Error("duplicate_game_id");
    idsSeen.add(String(game.game_id));
  }
  const settings = opts(options),
    history = new Map(),
    calibration = new Map(),
    calibrationGames = new Map(),
    calibrationCoverage = new Map(),
    by_game = Object.create(null),
    rows = [];
  const sorted = games
    .map((game) => ({ game, at: time(game.created_at ?? game.ended_at) }))
    .sort(
      (a, b) =>
        (a.at ?? Infinity) - (b.at ?? Infinity) ||
        String(a.game.game_id).localeCompare(String(b.game.game_id)),
    );
  for (let offset = 0; offset < sorted.length; ) {
    let end = offset + 1;
    while (end < sorted.length && sorted[end].at === sorted[offset].at)
      end += 1;
    const updates = [],
      calibrationUpdates = [];
    for (const { game, at } of sorted.slice(offset, end)) {
      const pool = String(game.rule_key ?? ""),
        players = game.players || [];
      const ids = players.map((p) => String(p.user_id));
      const hands = players.map((p) =>
        number(p.hands ?? p.completed_hands ?? game.hands),
      );
      const scores = players.map((p) => number(p.score ?? p.net_points));
      const dataValid =
        at !== null &&
        pool.length > 0 &&
        players.length >= 2 &&
        players.every(
          (p) =>
            p.user_id !== null &&
            p.user_id !== undefined &&
            String(p.user_id) !== "",
        ) &&
        new Set(ids).size === players.length &&
        scores.every((v) => v !== null) &&
        hands.every((v) => v > 0 && v === hands[0]) &&
        Math.abs(sum(scores)) < 1e-7;
      const competitive =
        game.competitive !== false && game.cooperative !== true;
      const valid = dataValid && competitive;
      const records = players.map(
        (p) => history.get(`${pool}\0${p.user_id}`) || { games: 0, total: 0 },
      );
      const strengths = records.map(
        (h) => h.total / (h.games + settings.shrinkage_games),
      );
      const tableStrength = mean(strengths) || 0;
      const priorErrors = calibration.get(pool) || [];
      const priorCalibrationGames = calibrationGames.get(pool)?.size || 0;
      by_game[String(game.game_id)] = Object.create(null);
      players.forEach((player, index) => {
        const expected = valid ? strengths[index] - tableStrength : null;
        const actual = dataValid ? scores[index] / hands[index] : null;
        const warm =
          valid && records.every((h) => h.games >= settings.min_history_games);
        const lower =
          priorCalibrationGames >= settings.min_reference_games
            ? quantile(priorErrors, (1 - settings.confidence) / 2)
            : null;
        const upper =
          priorCalibrationGames >= settings.min_reference_games
            ? quantile(priorErrors, 1 - (1 - settings.confidence) / 2)
            : null;
        const result = {
          game_id: String(game.game_id),
          user_id: String(player.user_id),
          model_version: MODEL_VERSION,
          expected_points: expected === null ? null : expected * hands[index],
          expected_per_hand: expected,
          actual_points: dataValid ? scores[index] : null,
          actual_per_hand: actual,
          residual_points:
            expected === null ? null : scores[index] - expected * hands[index],
          residual_per_hand: expected === null ? null : actual - expected,
          prior_games: records[index].games,
          opponent_prior_games: records
            .filter((_, i) => i !== index)
            .map((h) => h.games),
          strength_before: valid ? strengths[index] : null,
          prediction_status: !valid
            ? !competitive
              ? "descriptive_only"
              : !pool
                ? "unknown_rule_pool"
                : at === null
                  ? "unknown_timestamp"
                  : "invalid_or_non_zero_sum_points"
            : warm
              ? "estimated"
              : "cold_start",
          prediction_95:
            expected !== null && lower !== null
              ? [
                  (expected + lower) * hands[index],
                  (expected + upper) * hands[index],
                ]
              : null,
          prediction_95_per_hand:
            expected !== null && lower !== null
              ? [expected + lower, expected + upper]
              : null,
          prediction_reference_games: priorCalibrationGames,
          prediction_reference_observations: priorErrors.length,
          method:
            "earlier_games_only_opponent_adjusted_shrinkage; empirical_out_of_time_residual_band",
        };
        result.point_status = result.prediction_status;
        result.coverage = {
          prior_games: records[index].games,
          minimum_table_history: Math.min(...records.map((r) => r.games)),
          required_history: settings.min_history_games,
          prediction_band_available: result.prediction_95 !== null,
          status: result.prediction_status,
        };
        by_game[String(game.game_id)][String(player.user_id)] = result;
        rows.push(result);
        if (warm && lower !== null) {
          if (!calibrationCoverage.has(pool))
            calibrationCoverage.set(pool, new Map());
          const covered = calibrationCoverage.get(pool),
            item = covered.get(String(game.game_id)) || {
              covered: 0,
              observations: 0,
            };
          item.observations += 1;
          item.covered += Number(
            actual >= expected + lower && actual <= expected + upper,
          );
          covered.set(String(game.game_id), item);
        }
        if (valid)
          updates.push({
            key: `${pool}\0${player.user_id}`,
            performance: actual + tableStrength,
          });
        if (warm)
          calibrationUpdates.push({
            pool,
            game_id: String(game.game_id),
            error: result.residual_per_hand,
          });
      });
    }
    // Timestamp ties are a simultaneous prediction block, so array order cannot leak outcomes.
    for (const u of updates) {
      const h = history.get(u.key) || { games: 0, total: 0 };
      h.games += 1;
      h.total += u.performance;
      history.set(u.key, h);
    }
    for (const u of calibrationUpdates) {
      if (!calibration.has(u.pool)) calibration.set(u.pool, []);
      insert(calibration.get(u.pool), u.error);
      if (!calibrationGames.has(u.pool))
        calibrationGames.set(u.pool, new Set());
      calibrationGames.get(u.pool).add(u.game_id);
    }
    offset = end;
  }
  const summaries = [...calibration].map(([rule_key, errors]) => ({
    rule_key,
    observations: errors.length,
    independent_games: calibrationGames.get(rule_key)?.size || 0,
    residual_mean: mean(errors),
    residual_95: [quantile(errors, 0.025), quantile(errors, 0.975)],
    held_out_games: calibrationCoverage.get(rule_key)?.size || 0,
    prediction_band_coverage: mean(
      [...(calibrationCoverage.get(rule_key)?.values() || [])].map(
        (g) => g.covered / g.observations,
      ),
    ),
    coverage_sample_unit: "complete_games; predictions made before each game",
    method:
      "walk_forward_out_of_time_residuals; player residuals within a game are correlated",
  }));
  return {
    model_version: MODEL_VERSION,
    by_game,
    rows,
    calibration: summaries,
    coverage: {
      input_games: games.length,
      player_observations: rows.length,
      estimated_observations: rows.filter(
        (r) => r.prediction_status === "estimated",
      ).length,
      cold_start_observations: rows.filter(
        (r) => r.prediction_status === "cold_start",
      ).length,
      with_prediction_band: rows.filter((r) => r.prediction_95 !== null).length,
      by_status: rows.reduce((counts, row) => {
        counts[row.prediction_status] =
          (counts[row.prediction_status] || 0) + 1;
        return counts;
      }, {}),
    },
  };
}

module.exports = { buildExpectedPoints, MODEL_VERSION, DEFAULTS };
