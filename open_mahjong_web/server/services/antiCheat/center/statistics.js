"use strict";

// The unit of uncertainty is a complete saved game, never a discard. All
// predictions are made before any outcome at the same timestamp is observed.
const MODEL_VERSION = "prefix-opponent-shrinkage-v1";
const CHANNELS = ["ALL", "MELD", "RON"];
const DEFAULTS = Object.freeze({
  min_games: 8,
  min_reference_games: 12,
  min_dates: 2,
  confidence: 0.95,
  bootstrap_samples: 300,
  shrinkage_games: 12,
  min_history_games: 8,
  weights: { encounter: 0.2, delivery: 0.8, points: 0 },
});
const number = (value) =>
  value === null || value === undefined || value === ""
    ? null
    : Number.isFinite(Number(value))
      ? Number(value)
      : null;
const sum = (rows) => rows.reduce((a, b) => a + b, 0);
const mean = (rows) => (rows.length ? sum(rows) / rows.length : null);
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
const divide = (n, d) => (d > 0 ? n / d : null);
const opts = (options) => ({
  ...DEFAULTS,
  ...options,
  weights: DEFAULTS.weights,
});
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
function seedOf(text) {
  let h = 2166136261;
  for (const ch of String(text)) h = Math.imul(h ^ ch.charCodeAt(0), 16777619);
  return h >>> 0;
}
function random(seed) {
  let s = seedOf(seed);
  return () => {
    s += 0x6d2b79f5;
    let t = s;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// Student's t tail is used for the cluster delta-method fallback, avoiding a
// fixed 1.96 cutoff for eight-game samples. This is an approximate model-based
// test; it is not a probability that a player cheated.
function logGamma(z) {
  const p = [
    676.5203681218851, -1259.1392167224028, 771.3234287776531,
    -176.6150291621406, 12.507343278686905, -0.13857109526572012,
    9.984369578019572e-6, 1.5056327351493116e-7,
  ];
  if (z < 0.5)
    return (
      Math.log(Math.PI) - Math.log(Math.sin(Math.PI * z)) - logGamma(1 - z)
    );
  z -= 1;
  let x = 0.9999999999998099;
  for (let i = 0; i < p.length; i += 1) x += p[i] / (z + i + 1);
  const t = z + 7.5;
  return (
    0.5 * Math.log(2 * Math.PI) + (z + 0.5) * Math.log(t) - t + Math.log(x)
  );
}
function betaFraction(a, b, x) {
  const tiny = 1e-300;
  let c = 1,
    d = 1 - ((a + b) * x) / (a + 1);
  if (Math.abs(d) < tiny) d = tiny;
  d = 1 / d;
  let h = d;
  for (let m = 1; m <= 160; m += 1) {
    const m2 = 2 * m;
    let aa = (m * (b - m) * x) / ((a + m2 - 1) * (a + m2));
    d = 1 + aa * d;
    if (Math.abs(d) < tiny) d = tiny;
    c = 1 + aa / c;
    if (Math.abs(c) < tiny) c = tiny;
    d = 1 / d;
    h *= d * c;
    aa = (-(a + m) * (a + b + m) * x) / ((a + m2) * (a + m2 + 1));
    d = 1 + aa * d;
    if (Math.abs(d) < tiny) d = tiny;
    c = 1 + aa / c;
    if (Math.abs(c) < tiny) c = tiny;
    d = 1 / d;
    const delta = d * c;
    h *= delta;
    if (Math.abs(delta - 1) < 3e-12) break;
  }
  return h;
}
function betaI(x, a, b) {
  if (x <= 0) return 0;
  if (x >= 1) return 1;
  const bt = Math.exp(
    logGamma(a + b) -
      logGamma(a) -
      logGamma(b) +
      a * Math.log(x) +
      b * Math.log1p(-x),
  );
  return x < (a + 1) / (a + b + 2)
    ? (bt * betaFraction(a, b, x)) / a
    : 1 - (bt * betaFraction(b, a, 1 - x)) / b;
}
function tTail(t, df) {
  if (!Number.isFinite(t)) return t > 0 ? 0 : 1;
  const p = 0.5 * betaI(df / (df + t * t), df / 2, 0.5);
  return t >= 0 ? p : 1 - p;
}
const criticalCache = new Map();
function tCritical(df, probability) {
  const key = `${df}:${probability}`;
  if (criticalCache.has(key)) return criticalCache.get(key);
  let lo = 0,
    hi = 1000;
  for (let i = 0; i < 48; i += 1) {
    const mid = (lo + hi) / 2;
    if (1 - tTail(mid, df) < probability) lo = mid;
    else hi = mid;
  }
  const result = (lo + hi) / 2;
  criticalCache.set(key, result);
  return result;
}

function buildExpectedPoints(games, options = {}) {
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
      const pool = String(game.rule_key ?? game.rule ?? "unknown"),
        players = game.players || [];
      const ids = players.map((p) => String(p.user_id));
      const hands = players.map((p) =>
        number(p.hands ?? p.completed_hands ?? game.hands),
      );
      const scores = players.map((p) => number(p.score ?? p.net_points));
      const valid =
        at !== null &&
        players.length >= 2 &&
        new Set(ids).size === players.length &&
        scores.every((v) => v !== null) &&
        hands.every((v) => v > 0 && v === hands[0]) &&
        Math.abs(sum(scores)) < 1e-7;
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
        const actual = valid ? scores[index] / hands[index] : null;
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
          actual_points: valid ? scores[index] : null,
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
            ? at === null
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
  };
}

function grouped(rows, extractor) {
  const groups = new Map();
  for (let index = 0; index < rows.length; index += 1) {
    const row = rows[index],
      values = extractor(row);
    if (!values) continue;
    const n = number(values[0]),
      d = number(values[1]);
    if (n === null || d === null || d <= 0) continue;
    const id = String(row.game_id ?? row.id ?? `unidentified-${index}`);
    if (!groups.has(id))
      groups.set(id, {
        id,
        n: 0,
        d: 0,
        rows: 0,
        date: day(row.created_at ?? row.ended_at),
      });
    const g = groups.get(id);
    g.n += n;
    g.d += d;
    g.rows += 1;
  }
  // Other-opponent rows in one game are averaged into one comparison-opponent
  // block. This neither treats them as independent games nor inflates the
  // donor's unique discard count by two/three in the exposed control summary.
  return [...groups.values()].map((g) => ({
    ...g,
    n: g.n / g.rows,
    d: g.d / g.rows,
  }));
}
function totals(rows) {
  const n = sum(rows.map((r) => r.n)),
    d = sum(rows.map((r) => r.d));
  return { numerator: n, denominator: d, value: divide(n, d) };
}
function metricUnavailable(actual, reference, target, controls, reason) {
  return {
    actual,
    expected: reference.value,
    deviation:
      actual.value === null || reference.value === null
        ? null
        : actual.value - reference.value,
    effect_95: null,
    reference_95: null,
    reference_upper_95: null,
    standardized_deviation: null,
    p_value: null,
    independent_games: target.length,
    reference_games: controls.length,
    independent_dates: new Set(target.map((r) => r.date).filter(Boolean)).size,
    confidence: 0.95,
    status: "insufficient",
    reason,
    flag: false,
    score: null,
  };
}
function clusterDifference(target, controls, actual, reference) {
  const influences = new Map();
  for (const [rows, center, denom, sign] of [
    [target, actual.value, actual.denominator, 1],
    [controls, reference.value, reference.denominator, -1],
  ]) {
    for (const row of rows)
      influences.set(
        row.id,
        (influences.get(row.id) || 0) +
          (sign * (row.n - center * row.d)) / denom,
      );
  }
  const n = influences.size;
  return n > 1
    ? Math.sqrt((n / (n - 1)) * sum([...influences.values()].map((v) => v * v)))
    : 0;
}
function compareBlocks(target, controls, settings, seed, bounded = false) {
  const actual = totals(target),
    reference = totals(controls);
  const unavailable = (reason) => ({
    ...metricUnavailable(actual, reference, target, controls, reason),
    confidence: settings.confidence,
  });
  if (actual.value === null || reference.value === null)
    return unavailable("missing_exposure_or_reference");
  if (
    target.length < settings.min_games ||
    controls.length < settings.min_reference_games
  )
    return unavailable("insufficient_independent_games");
  const dates = new Set(target.map((r) => r.date).filter(Boolean)).size;
  if (dates < settings.min_dates)
    return unavailable("insufficient_independent_dates");
  const delta = actual.value - reference.value,
    se = clusterDifference(target, controls, actual, reference);
  if (!(se > 1e-12)) return unavailable("degenerate_game_blocks");
  const df = Math.max(1, Math.min(target.length, controls.length) - 1),
    alpha = 1 - settings.confidence;
  const critical = tCritical(df, 1 - alpha / 2),
    upperCritical = tCritical(df, settings.confidence);
  let interval = [delta - critical * se, delta + critical * se];
  let method = "game_cluster_delta_t";
  // Small groups use paired whole-game resampling, preserving shared source
  // games between target and controls. Larger groups use a bounded-memory
  // cluster delta approximation rather than O(all history * every pair).
  const draws = Math.max(
    100,
    Math.min(2000, Number(settings.bootstrap_samples) || 300),
  );
  if (target.length <= 64 && controls.length <= 128) {
    const ids = [
      ...new Set([...target.map((r) => r.id), ...controls.map((r) => r.id)]),
    ];
    const t = new Map(target.map((r) => [r.id, r])),
      c = new Map(controls.map((r) => [r.id, r]));
    const rng = random(seed),
      samples = [];
    for (let b = 0; b < draws; b += 1) {
      let tn = 0,
        td = 0,
        cn = 0,
        cd = 0;
      for (let j = 0; j < ids.length; j += 1) {
        const id = ids[Math.floor(rng() * ids.length)],
          a = t.get(id),
          z = c.get(id);
        if (a) {
          tn += a.n;
          td += a.d;
        }
        if (z) {
          cn += z.n;
          cd += z.d;
        }
      }
      if (td > 0 && cd > 0) samples.push(tn / td - cn / cd);
    }
    samples.sort((a, b) => a - b);
    if (samples.length >= draws * 0.9) {
      const lo = quantile(samples, alpha / 2),
        hi = quantile(samples, 1 - alpha / 2);
      // Never replace a conservative t interval by a spuriously narrow bootstrap
      // interval when the discrete samples have little observed variation.
      interval = [Math.min(interval[0], lo), Math.max(interval[1], hi)];
      method = "whole_game_block_bootstrap_with_cluster_t_guard";
    }
  }
  let boundary = reference.value + upperCritical * se;
  if (bounded && controls.every((r) => r.n === 0))
    boundary = Math.max(boundary, 1 - Math.pow(alpha, 1 / controls.length));
  const reference95 = [
    reference.value - critical * se,
    reference.value + critical * se,
  ];
  if (bounded) {
    boundary = clamp(boundary, 0, 1);
    reference95[0] = clamp(reference95[0], 0, 1);
    reference95[1] = clamp(reference95[1], 0, 1);
  }
  const p = tTail(delta / se, df);
  // The displayed upper boundary and flag use the same one-sided 95% test.
  // The effect interval is separately labeled two-sided, so it may cross zero
  // for effects between the one-sided 95% and two-sided 95% thresholds.
  const flag = delta > 0 && actual.value > boundary && p <= alpha;
  return {
    actual,
    expected: reference.value,
    deviation: delta,
    effect_95: interval,
    reference_95: reference95,
    reference_upper_95: boundary,
    standardized_deviation: delta / se,
    p_value: p,
    p_value_method: "one_sided_game_cluster_t_approximation",
    independent_games: target.length,
    reference_games: controls.length,
    independent_dates: dates,
    confidence: settings.confidence,
    alternative: "greater",
    effect_interval_sides: 2,
    status: "estimated",
    reason: null,
    method,
    flag,
    score: clamp(100 * (1 - p), 0, 100),
    interpretation:
      "conditional_model_reference; not a probability of cheating",
  };
}
const counters = (row, channel) =>
  row.channels?.[channel] || row[channel] || {};
const RATE_SPECS = {
  delivery_rate: ["events", "discards"],
  source_share: ["events", "received"],
  opportunity_creation_rate: ["opportunities", "eligible_decisions"],
  opportunity_realization_rate: ["realized_opportunities", "opportunities"],
  choice_hit_rate: ["choice_hits", "choices"],
};
function compareRateRows(targetRows, controls, spec, options = {}) {
  const settings = opts(options),
    extract = (row) => {
      const c = options.channel ? counters(row, options.channel) : row;
      if (
        c.available === false ||
        (["eligible_decisions", "opportunities", "choices"].includes(spec[1]) &&
          c.opportunities_available === false)
      )
        return null;
      const n = number(c[spec[0]]),
        d = number(c[spec[1]]);
      return n !== null && d !== null && n >= 0 && n <= d ? [n, d] : null;
    };
  return compareBlocks(
    grouped(targetRows, extract),
    grouped(controls, extract),
    settings,
    options.seed || spec.join(":"),
    true,
  );
}
function comparePointRows(targetRows, controls, targetField, options = {}) {
  const settings = opts(options);
  const field = (row) => {
    const side = targetField.includes("donor")
      ? "donor"
      : targetField.includes("recipient")
        ? "recipient"
        : null;
    const status =
      (side ? row[`${side}_prediction_status`] : null) ?? row.prediction_status;
    if (status && status !== "estimated") return null;
    return Object.hasOwn(row, targetField)
      ? number(row[targetField])
      : number(row.residual_per_hand ?? row.residual);
  };
  return compareBlocks(
    grouped(targetRows, (row) =>
      field(row) === null ? null : [field(row), 1],
    ),
    grouped(controls, (row) => (field(row) === null ? null : [field(row), 1])),
    settings,
    options.seed || targetField,
    false,
  );
}
function sourceConcentration(empirical, games, channel, playerCount, settings) {
  const result = { ...empirical, empirical: { ...empirical } };
  const floor =
    Number.isInteger(playerCount) && playerCount >= 2
      ? 1 / (playerCount - 1)
      : null;
  result.equal_share = floor;
  result.expected =
    floor === null || empirical.expected === null
      ? empirical.expected
      : Math.max(floor, empirical.expected);
  result.deviation =
    empirical.actual.value === null || result.expected === null
      ? null
      : empirical.actual.value - result.expected;
  // Each component retains its own two-sided interval. The intersection-union
  // test supplies a one-sided decision line, not a combined two-sided 95% CI.
  result.effect_95 = null;
  result.reference_95 = null;
  result.reference_basis = "equal_share_and_seat_matched_receiver_reference";
  result.reliability = {
    games: empirical.independent_games,
    dates: empirical.independent_dates,
    reference_games: empirical.reference_games,
    status: empirical.status,
  };
  if (empirical.status !== "estimated" || floor === null) {
    if (floor === null) {
      result.status = "insufficient";
      result.reason = "unknown_player_count";
      result.reference_upper_95 = null;
      result.p_value = null;
      result.flag = false;
      result.score = null;
      result.reliability.status = result.status;
    }
    return result;
  }
  const blocks = grouped(games, (row) => {
    const c = counters(row, channel);
    return c.available === false ? null : [c.events, c.received];
  });
  const actual = totals(blocks),
    se = clusterDifference(blocks, [], actual, {
      value: floor,
      denominator: 1,
    });
  if (!(se > 1e-12)) {
    return {
      ...result,
      status: "insufficient",
      reason: "degenerate_equal_share_blocks",
      reference_upper_95: null,
      p_value: null,
      flag: false,
      score: null,
      reliability: { ...result.reliability, status: "insufficient" },
    };
  }
  const df = blocks.length - 1,
    alpha = 1 - settings.confidence;
  const fixedUpper = clamp(
    floor + tCritical(df, settings.confidence) * se,
    0,
    1,
  );
  const fixedP = tTail((actual.value - floor) / se, df);
  const upper = Math.max(empirical.reference_upper_95, fixedUpper);
  const p = Math.max(empirical.p_value, fixedP);
  const flag = actual.value > upper && p <= alpha;
  return {
    ...result,
    reference_upper_95: upper,
    p_value: p,
    flag,
    deviation: actual.value - result.expected,
    standardized_deviation: Math.min(
      empirical.standardized_deviation,
      (actual.value - floor) / se,
    ),
    equal_share_test: {
      expected: floor,
      reference_upper_95: fixedUpper,
      p_value: fixedP,
      independent_games: blocks.length,
      standard_error: se,
      effect_95: [
        actual.value - floor - tCritical(df, 1 - alpha / 2) * se,
        actual.value - floor + tCritical(df, 1 - alpha / 2) * se,
      ],
    },
    method: "intersection_union_equal_share_and_empirical_game_cluster_t",
    p_value_method: "max_of_equal_share_and_empirical_one_sided_p_values",
    score:
      flag && upper < 1
        ? clamp((100 * (actual.value - upper)) / (1 - upper), 0, 100)
        : 0,
  };
}
function matchSeatControls(target, controls, tolerance = 0.15) {
  if (!Number.isFinite(tolerance) || tolerance < 0 || tolerance > 1)
    throw new Error("invalid_seat_matching_tolerance");
  const keys = ["upstream", "opposite", "downstream"];
  const exposure = (rows) => {
    const counts = Object.fromEntries(
      keys.map((k) => [
        k,
        sum(rows.map((r) => Math.max(0, number(r.seat_exposure?.[k]) || 0))),
      ]),
    );
    const total = sum(Object.values(counts));
    return total > 0
      ? Object.fromEntries(keys.map((k) => [k, counts[k] / total]))
      : null;
  };
  const targetExposure = exposure(target);
  const rows = targetExposure
    ? controls.filter((row) => {
        const candidate = exposure([row]);
        return (
          candidate &&
          keys.every(
            (k) =>
              Math.abs(candidate[k] - targetExposure[k]) <= tolerance + 1e-12,
          )
        );
      })
    : [];
  const controlExposure = exposure(rows);
  Object.defineProperty(rows, "matching", {
    enumerable: false,
    value: {
      method: "target_hand_weighted_seat_exposure_matching",
      tolerance,
      target_exposure: targetExposure,
      control_exposure: controlExposure,
      before_rows: controls.length,
      matched_rows: rows.length,
      matched_games: new Set(rows.map((r) => String(r.game_id))).size,
      max_component_gap:
        targetExposure && controlExposure
          ? Math.max(
              ...keys.map((k) =>
                Math.abs(targetExposure[k] - controlExposure[k]),
              ),
            )
          : null,
      status: targetExposure
        ? rows.length
          ? "matched"
          : "no_comparable_seat_exposure"
        : "unknown_target_seat_exposure",
      interpretation:
        "conditional empirical reference; does not imply random matchmaking or causal adjustment",
    },
  });
  return rows;
}
function encounterMetric(input, settings) {
  const shared = number(input.shared_games) || 0,
    donor = number(input.donor_games) || 0,
    recipient = number(input.recipient_games) || 0;
  const actual = divide(shared, donor),
    refs = (input.reference_rates || [])
      .map(number)
      .filter((v) => v !== null && v >= 0 && v <= 1)
      .sort((a, b) => a - b);
  const enough =
    refs.length >= settings.min_reference_games &&
    shared >= settings.min_games &&
    actual !== null;
  const boundary = enough ? quantile(refs, settings.confidence) : null;
  const percentile = enough
    ? (refs.filter((v) => v < actual).length +
        0.5 * refs.filter((v) => v === actual).length) /
      refs.length
    : null;
  return {
    shared_games: shared,
    donor_games: donor,
    recipient_games: recipient,
    donor_coplay_rate: actual,
    recipient_coplay_rate: divide(shared, recipient),
    jaccard: divide(shared, donor + recipient - shared),
    expected: refs.length ? mean(refs) : null,
    reference_upper_95: boundary,
    reference_pairs: refs.length,
    empirical_percentile: percentile,
    score:
      actual === null || recipient <= 0
        ? null
        : 100 * Math.max(actual, divide(shared, recipient)),
    status: enough ? "empirical_reference" : "descriptive",
    flag: false,
    p_value: null,
    method:
      "matched_cohort_empirical_percentile; not random_matchmaking_probability",
  };
}
function compositeScore(components, options = {}) {
  const settings = opts(options),
    weights = settings.weights,
    total = sum(Object.values(weights));
  const known = Object.entries(weights).filter(
    ([key, weight]) => weight > 0 && number(components[key]) !== null,
  );
  const coverage = divide(sum(known.map(([, weight]) => weight)), total) || 0;
  const score =
    total > 0
      ? sum(
          known.map(([key, weight]) => weight * clamp(components[key], 0, 100)),
        ) / total
      : 0;
  return {
    score: known.length ? score : null,
    coverage,
    score_range: known.length
      ? [score, Math.min(100, score + (1 - coverage) * 100)]
      : [0, 100],
    complete: coverage > 0.999999,
    components: Object.fromEntries(
      Object.entries(weights).map(([key, weight]) => [
        key,
        {
          score: number(components[key]),
          weight: weight / total,
          contribution:
            number(components[key]) === null
              ? null
              : (weight * components[key]) / total,
        },
      ]),
    ),
    interpretation:
      "review_priority_score; missing dimensions remain unknown; not a cheating probability",
  };
}
function analyzePair(input, options = {}) {
  const settings = opts(options),
    games = input.games || input.sharedGames || input.shared_games_rows || [];
  const donorControls = input.donor_controls || input.donorOtherGames || [],
    recipientControls =
      input.recipient_controls || input.recipientOtherGames || [];
  const donorPointControls = input.donor_point_controls || donorControls;
  // Source concentration is a property of the receiver. Donor-to-other-player
  // rates answer a different question and must never silently supply its line.
  const sourceControls = input.source_controls || [];
  const label = `${input.donor_id}->${input.recipient_id}:${input.window_days ?? 0}`;
  const supported =
    input.delivery_supported !== false &&
    !games.some((g) => g.delivery_supported === false);
  const competitive =
    input.competitive !== false && !games.some((g) => g.competitive === false);
  const channels = {};
  for (const channel of CHANNELS) {
    const controls = input.references?.[channel] || donorControls,
      metrics = {};
    for (const [name, spec] of Object.entries(RATE_SPECS))
      metrics[name] = compareRateRows(
        games,
        name === "source_share"
          ? input.source_references?.[channel] || sourceControls
          : controls,
        spec,
        {
          ...settings,
          min_games: supported ? settings.min_games : Infinity,
          channel,
          seed: `${label}:${channel}:${name}`,
        },
      );
    const actual = grouped(games, (row) =>
      counters(row, channel).available === false
        ? null
        : [counters(row, channel).events, 1],
    );
    const events = actual.length ? sum(actual.map((r) => r.n)) : null,
      minimum =
        options.channels?.[channel]?.min_events ?? (channel === "RON" ? 4 : 10);
    const tested = Object.values(metrics).filter(
      (m) => m.status === "estimated",
    );
    const minP = tested.length
      ? Math.min(...tested.map((m) => m.p_value))
      : null;
    const eligible = events !== null && events >= minimum;
    const enabled = options.channels?.[channel]?.enabled !== false;
    const source = sourceConcentration(
      metrics.source_share,
      games,
      channel,
      Number(input.player_count),
      settings,
    );
    metrics.source_share = source;
    source.rate_definition =
      "received_from_named_player_over_all_received_in_shared_games";
    const flag = enabled && supported && competitive && eligible && source.flag;
    channels[channel] = {
      events,
      min_events: minimum,
      metrics,
      ...source,
      source_share: metrics.source_share.actual,
      discard_rate: metrics.delivery_rate.actual,
      baseline_rate: source.expected,
      observed_rate: source.actual.value,
      primary_metric: "source_share",
      reference_name: "其他同桌来源参考",
      diagnostic_flag:
        enabled &&
        supported &&
        competitive &&
        eligible &&
        Object.entries(metrics).some(
          ([name, m]) => name !== "source_share" && m.flag,
        ),
      diagnostic_p_value:
        minP === null ? null : Math.min(1, minP * tested.length),
      p_value:
        !enabled || !supported || !competitive || !eligible
          ? null
          : source.p_value,
      p_value_method: source.p_value_method,
      flag,
      eligible: enabled && eligible && supported && competitive,
      status: !enabled
        ? "disabled"
        : !supported || !actual.length
          ? "unavailable"
          : !competitive
            ? "descriptive"
            : source.status === "estimated"
              ? "estimated"
              : "insufficient",
      score:
        enabled && supported && competitive && eligible ? source.score : null,
    };
    if (!supported) {
      channels[channel].events = null;
      channels[channel].reason = "unsupported_delivery_rules";
    }
  }
  const effective = compareRateRows(
    games,
    donorControls,
    ["effective_melds", "evaluated_melds"],
    { ...settings, seed: `${label}:effective` },
  );
  effective.denominator_definition = "evaluated_meld_events";
  effective.rate_definition = "effective_fraction_among_evaluated_melds";
  const effectiveExposure = (rows) => {
    const coverage = {
      source_games: new Set(rows.map((r) => String(r.game_id))).size,
      eligible_games: 0,
      excluded_partial_games: 0,
      excluded_unsupported_games: 0,
      excluded_unknown_games: 0,
    };
    const selected = [],
      seen = new Map();
    for (const row of rows) {
      const id = String(row.game_id),
        meld = counters(row, "MELD");
      const evaluated = number(row.evaluated_melds),
        total = number(row.meld_events),
        count = number(row.effective_melds),
        discards = number(meld.discards);
      let reason = null;
      if (row.delivery_supported === false || meld.available === false)
        reason = "unsupported";
      else if (
        evaluated === null ||
        total === null ||
        count === null ||
        discards === null ||
        !(discards > 0) ||
        count < 0 ||
        count > evaluated
      )
        reason = "unknown";
      else if (evaluated !== total) reason = "partial";
      if (!seen.has(id)) seen.set(id, new Set());
      seen.get(id).add(reason ? `excluded_${reason}_games` : "eligible_games");
      if (!reason)
        selected.push({
          game_id: row.game_id,
          created_at: row.created_at,
          ended_at: row.ended_at,
          fully_evaluated_effective_events: count,
          fully_evaluated_effective_discards: discards,
        });
    }
    for (const categories of seen.values())
      for (const key of categories) coverage[key] += 1;
    coverage.note =
      "reference games can contain both eligible and excluded comparison opponents";
    return { rows: selected, coverage };
  };
  const effectiveTarget = effectiveExposure(games),
    effectiveReference = effectiveExposure(donorControls);
  const effectiveRate = compareRateRows(
    effectiveTarget.rows,
    effectiveReference.rows,
    ["fully_evaluated_effective_events", "fully_evaluated_effective_discards"],
    { ...settings, seed: `${label}:effective-per-discard` },
  );
  effectiveRate.denominator_definition =
    "verified_discard_exposure_in_fully_evaluated_games";
  effectiveRate.rate_definition = "effective_meld_delivery_per_discard";
  effectiveRate.coverage = effectiveTarget.coverage;
  effectiveRate.reference_coverage = effectiveReference.coverage;
  if (!effectiveTarget.rows.length || !effectiveReference.rows.length) {
    effectiveRate.status = "unavailable";
    effectiveRate.reason = !effectiveTarget.rows.length
      ? "no_fully_evaluated_meld_games"
      : "no_fully_evaluated_meld_reference_games";
  }
  const recipient = comparePointRows(
    games,
    recipientControls,
    "recipient_residual_per_hand",
    { ...settings, seed: `${label}:points-recipient` },
  );
  const donor = comparePointRows(
    games,
    donorPointControls,
    "donor_residual_per_hand",
    { ...settings, seed: `${label}:points-donor` },
  );
  const negative = (value) => (number(value) === null ? null : -Number(value));
  const donorResidual = (row) =>
    Object.hasOwn(row, "donor_residual_per_hand")
      ? row.donor_residual_per_hand
      : row.residual_per_hand;
  const sacrifice = comparePointRows(
    games.map((g) => ({
      ...g,
      negative_donor_residual: negative(donorResidual(g)),
    })),
    donorPointControls.map((g) => ({
      ...g,
      negative_donor_residual: negative(donorResidual(g)),
    })),
    "negative_donor_residual",
    { ...settings, seed: `${label}:points-sacrifice` },
  );
  const encounter = encounterMetric(
    {
      ...input.encounters,
      shared_games:
        input.encounters?.shared_games ??
        new Set(games.map((g) => g.game_id)).size,
    },
    settings,
  );
  if (!supported) {
    for (const metric of [effective, effectiveRate]) {
      metric.flag = false;
      metric.score = null;
      metric.status = "unavailable";
      metric.reason = "unsupported_delivery_rules";
    }
  }
  const deliveryScores = [...CHANNELS.map((c) => channels[c].score)].filter(
    (v) => v !== null,
  );
  const pointScores = [recipient.score, sacrifice.score].filter(
    (v) => v !== null,
  );
  const composite = compositeScore(
    {
      encounter: encounter.score,
      delivery: deliveryScores.length ? Math.max(...deliveryScores) : null,
      points: pointScores.length ? Math.max(...pointScores) : null,
    },
    settings,
  );
  if (!competitive) {
    for (const metric of [
      effective,
      effectiveRate,
      recipient,
      donor,
      sacrifice,
    ])
      metric.flag = false;
    composite.score = null;
    composite.complete = false;
    composite.status = "descriptive_only";
  }
  const actualPerHand = (g) =>
    number(
      g.recipient_actual_per_hand ??
        g.recipient_score_per_hand ??
        (number(g.recipient_score) !== null && number(g.shared_hands) > 0
          ? g.recipient_score / g.shared_hands
          : null),
    );
  const expectedPerHand = (g) =>
    number(
      g.recipient_expected_per_hand ??
        (number(g.recipient_expected_points) !== null &&
        number(g.shared_hands) > 0
          ? g.recipient_expected_points / g.shared_hands
          : null),
    );
  const inferenceRows = games.filter((g) => {
    const status = g.recipient_prediction_status ?? g.prediction_status;
    return (
      (!status || status === "estimated") &&
      number(g.recipient_residual_per_hand) !== null
    );
  });
  const inferenceIds = new Set(inferenceRows.map((g) => String(g.game_id)));
  const aligned = (rows) => {
    const byId = new Map();
    for (const g of rows) {
      const actual = actualPerHand(g),
        expected = expectedPerHand(g);
      if (actual !== null && expected !== null && !byId.has(String(g.game_id)))
        byId.set(String(g.game_id), { actual, expected });
    }
    return [...byId.values()];
  };
  const inferencePoints = aligned(inferenceRows),
    allPoints = aligned(games);
  // The three headline means describe one identical estimated-game cohort.
  // Cold-start descriptive means remain separately visible, rather than being
  // mixed with the warm-only residual used for statistical inference.
  const displayCohortComplete = inferencePoints.length === inferenceIds.size;
  const inferenceActualMean = displayCohortComplete
    ? mean(inferencePoints.map((p) => p.actual))
    : null;
  const inferenceExpectedMean = displayCohortComplete
    ? mean(inferencePoints.map((p) => p.expected))
    : null;
  const allGames = {
    games: new Set(games.map((g) => String(g.game_id))).size,
    descriptive_games: allPoints.length,
    actual_mean: mean(allPoints.map((p) => p.actual)),
    expected_mean: mean(allPoints.map((p) => p.expected)),
    residual_mean: mean(allPoints.map((p) => p.actual - p.expected)),
    cohort:
      "all_games_with_paired_actual_and_prediction; includes_labeled_cold_start",
  };
  return {
    donor_id: String(input.donor_id),
    recipient_id: String(input.recipient_id),
    window_days: input.window_days ?? 0,
    model_version: MODEL_VERSION,
    reference_matching:
      input.reference_matching || donorControls.matching || null,
    source_reference_matching:
      input.source_reference_matching || sourceControls.matching || null,
    channels,
    effective_delivery: effective,
    effective_delivery_rate: effectiveRate,
    points: {
      recipient,
      donor,
      donor_sacrifice: sacrifice,
      unit: "net_points_per_completed_hand",
      actual_mean: inferenceActualMean,
      expected_mean: inferenceExpectedMean,
      residual_mean: recipient.actual.value,
      inference_games: recipient.independent_games,
      inference_cohort: "estimated_predictions_only",
      display_cohort_complete: displayCohortComplete,
      all_games: allGames.games,
      all_actual_mean: allGames.actual_mean,
      all_expected_mean: allGames.expected_mean,
      all_residual_mean: allGames.residual_mean,
      all_descriptive_games: allGames.descriptive_games,
      control_residual_mean: recipient.expected,
      difference: recipient.deviation,
      ci95: recipient.effect_95,
      upper95: recipient.reference_upper_95,
      status: recipient.status,
      flag: recipient.flag,
    },
    encounter,
    composite,
    flag_all: channels.ALL.flag,
    flag_meld: channels.MELD.flag,
    flag_ron: channels.RON.flag,
    ranking_eligible: competitive,
    anomaly: competitive && CHANNELS.some((c) => channels[c].flag),
    independent_flags: {
      ENCOUNTER: competitive && encounter.flag,
      ALL: channels.ALL.flag,
      MELD: channels.MELD.flag,
      RON: channels.RON.flag,
      EFFECTIVE: effective.flag,
      EFFECTIVE_RATE: effectiveRate.flag,
      POINTS: recipient.flag || sacrifice.flag,
    },
  };
}

// Holm controls family-wise error without assuming independent pair tests.
// The adjustment is only as valid as the explicitly labeled reference model.
function adjustPValues(rows, options = {}) {
  const alpha = options.alpha ?? 1 - (options.confidence ?? 0.95),
    method = options.method || "holm";
  const valid = rows
      .map((row, index) => ({ row, index, p: number(row.p_value) }))
      .filter((x) => x.p !== null && x.p >= 0 && x.p <= 1)
      .sort((a, b) => a.p - b.p || a.index - b.index),
    adjusted = new Map();
  if (method === "holm") {
    let previous = 0;
    valid.forEach((x, i) => {
      previous = Math.max(previous, (valid.length - i) * x.p);
      adjusted.set(x.index, Math.min(1, previous));
    });
  } else if (method === "fdr_by" || method === "fdr_bh") {
    const correction =
      method === "fdr_by" ? sum(valid.map((_, i) => 1 / (i + 1))) : 1;
    let previous = 1;
    for (let i = valid.length - 1; i >= 0; i -= 1) {
      previous = Math.min(
        previous,
        (valid[i].p * valid.length * correction) / (i + 1),
      );
      adjusted.set(valid[i].index, Math.min(1, previous));
    }
  } else throw new Error("unsupported_multiple_testing_method");
  return rows.map((row, index) => ({
    ...row,
    adjusted_p_value: adjusted.get(index) ?? null,
    adjusted_flag: adjusted.has(index) && adjusted.get(index) <= alpha,
    adjustment_method: method,
    tested_hypotheses: valid.length,
  }));
}
function rankPairs(rows, options = {}) {
  const tests = [];
  for (let i = 0; i < rows.length; i += 1)
    for (const channel of CHANNELS) {
      const c = rows[i].channels?.[channel];
      tests.push({
        pair_index: i,
        channel,
        p_value:
          rows[i].ranking_eligible === true && c?.eligible === true
            ? c.p_value
            : null,
      });
    }
  const adjusted = adjustPValues(tests, options),
    byPair = new Map();
  for (const test of adjusted) {
    if (!byPair.has(test.pair_index)) byPair.set(test.pair_index, {});
    byPair.get(test.pair_index)[test.channel] = test;
  }
  return rows
    .map((row, i) => ({
      ...row,
      adjusted_tests: byPair.get(i),
      adjusted_anomaly: Object.values(byPair.get(i) || {}).some(
        (x) => x.adjusted_flag,
      ),
    }))
    .sort(
      (a, b) =>
        Number(b.anomaly) - Number(a.anomaly) ||
        Number(b.composite?.complete) - Number(a.composite?.complete) ||
        (b.composite?.score ?? -1) - (a.composite?.score ?? -1) ||
        String(a.donor_id).localeCompare(String(b.donor_id)),
    )
    .map((row, index) => ({ ...row, rank: index + 1 }));
}
module.exports = {
  MODEL_VERSION,
  DEFAULTS,
  buildExpectedPoints,
  analyzePair,
  compareRateRows,
  comparePointRows,
  matchSeatControls,
  compositeScore,
  adjustPValues,
  rankPairs,
  quantile,
  tTail,
  tCritical,
};
