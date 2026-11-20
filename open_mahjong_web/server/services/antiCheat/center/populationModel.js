"use strict";

// A fixed reference-population model. Its prediction distribution consists of
// observed, complete-game relationship blocks, never draws from the target's
// observed rate. Model fitting and local incremental scoring are separate APIs.
const VERSION = "population-source-location-scale-v5";
const CHANNELS = ["ALL", "MELD", "RON"];
const TYPES = {
  ALL: ["CHI", "PENG", "GANG", "RON"],
  MELD: ["CHI", "PENG", "GANG"],
  RON: ["RON"],
};
const SEATS = ["upstream", "opposite", "downstream"];
const decodedModels = new WeakMap();
const BLOCK_COLUMNS = [
  "relation",
  "games",
  "dates",
  "numerator",
  "denominator",
  "seats",
  "mixture",
  "ratings",
  "nonconformity",
  "covered",
  "predicted_upper",
  "weight_squared_sum",
  "within_variance",
];
function packBuckets(buckets) {
  return Object.fromEntries(
    Object.entries(buckets).map(([key, rows]) => [
      key,
      rows.map((row) => BLOCK_COLUMNS.map((name) => row[name] ?? null)),
    ]),
  );
}
function decodeFold(model, index) {
  if (!decodedModels.has(model)) decodedModels.set(model, new Map());
  const cache = decodedModels.get(model);
  if (cache.has(index)) return cache.get(index);
  const packed = model.folds[index],
    fold = { index };
  for (const stage of ["training", "calibration", "validation"])
    fold[stage] = Object.fromEntries(
      Object.entries(packed[stage]).map(([key, rows]) => {
        const [rule_key, channel] = JSON.parse(key);
        return [
          key,
          rows.map((values) => {
            const row = Object.fromEntries(
              BLOCK_COLUMNS.map((name, i) => [name, values[i]]),
            );
            const [donor_id, recipient_id] = JSON.parse(row.relation);
            return {
              ...row,
              rule_key,
              channel,
              donor_id,
              recipient_id,
              value: ratio(row.numerator, row.denominator),
            };
          }),
        ];
      }),
    );
  cache.set(index, fold);
  return fold;
}
const DEFAULTS = Object.freeze({
  reference_split: "relation_full_history",
  folds: 10,
  train_fraction: 0.6,
  calibration_fraction: 0.2,
  min_training_relations: 40,
  min_calibration_relations: 40,
  min_validation_relations: 60,
  max_reference_relations: 128,
  max_validation_per_stratum: 160,
  confidence: 0.95,
  seat_tolerance: 0.2,
  mixture_tolerance: 0.25,
  exposure_ratio_limit: 4,
  max_game_band: 9,
  reservoir_per_stratum: 384,
  min_repeated_training_relations: 12,
  max_serialized_bytes: 24 * 1024 * 1024,
  rating_tolerance: 250,
  validation_min_coverage: 0.9,
});
const sum = (a) => a.reduce((s, x) => s + x, 0);
const ratio = (n, d) => (d > 0 ? n / d : null);
const finite = (v) =>
  v !== null && v !== undefined && Number.isFinite(Number(v));
const hash = (value) => {
  let h = 2166136261;
  for (const c of String(value)) h = Math.imul(h ^ c.charCodeAt(0), 16777619);
  return h >>> 0;
};
const relation = (a, b) => JSON.stringify([String(a), String(b)]);
const unordered = (a, b) => JSON.stringify([String(a), String(b)].sort());
const foldFor = (a, b, n) => hash(unordered(a, b)) % n;
const bandOf = (n) => Math.floor(Math.log2(Math.max(1, n)));
const keyOf = (rule, channel) => JSON.stringify([rule, channel]);
const broadBand = (n) => (n === 1 ? "1" : n < 8 ? "2-7" : "8+");
const day = (t) => new Date(t + 8 * 3600000).toISOString().slice(0, 10);
function quantile(values, p) {
  if (!values.length) return null;
  const a = [...values].sort((x, y) => x - y);
  // The finite-sample conformal order statistic can exceed the available data.
  const k = Math.ceil((a.length + 1) * p);
  return k > a.length ? null : a[Math.max(0, k - 1)];
}
function optionsOf(options = {}) {
  const o = { ...DEFAULTS, ...options };
  for (const key of ["min_games", "min_dates", "min_events"]) delete o[key];
  if (
    !["time_and_relation", "relation_full_history"].includes(o.reference_split)
  )
    throw new Error("invalid_reference_split");
  for (const k of [
    "folds",
    "min_training_relations",
    "min_calibration_relations",
    "min_validation_relations",
    "max_reference_relations",
    "max_validation_per_stratum",
    "max_game_band",
  ])
    if (!Number.isInteger(o[k]) || o[k] < 1) throw new Error(`invalid_${k}`);
  if (
    o.folds < 2 ||
    o.confidence !== 0.95 ||
    o.train_fraction <= 0 ||
    o.calibration_fraction <= 0 ||
    o.train_fraction + o.calibration_fraction >= 1
  )
    throw new Error("invalid_model_split_or_confidence");
  if (
    o.max_reference_relations <
    Math.max(o.min_training_relations, o.min_calibration_relations)
  )
    throw new Error("reference_cap_below_minimum");
  return o;
}
function normalizeGames(games) {
  const ids = new Set(),
    result = [];
  for (const game of games || []) {
    const id = String(game.game_id ?? game.id ?? "");
    const rawTime = game.created_at ?? game.ended_at;
    const t =
      rawTime instanceof Date
        ? +rawTime
        : typeof rawTime === "number"
          ? rawTime
          : typeof rawTime === "string" && /(?:Z|[+-]\d\d:?\d\d)$/.test(rawTime)
            ? Date.parse(rawTime)
            : NaN;
    if (!id || ids.has(id) || !Number.isFinite(t))
      throw new Error("invalid_or_duplicate_game_identity_or_timestamp");
    ids.add(id);
    const pairs = game.pairs ?? game.pair_facts ?? [];
    const people = [
      ...new Set(
        pairs.flatMap((p) => [String(p.donor_id), String(p.recipient_id)]),
      ),
    ];
    if (
      game.delivery_supported === false ||
      game.competitive === false ||
      people.length < 2
    )
      continue;
    const incoming = new Map();
    for (const p of pairs) {
      const k = String(p.recipient_id);
      if (!incoming.has(k)) incoming.set(k, []);
      incoming.get(k).push(p);
    }
    const samples = [];
    for (const p of pairs) {
      if (p.delivery_supported === false || p.donor_id === p.recipient_id)
        continue;
      const sources = incoming.get(String(p.recipient_id));
      if (
        sources.length !== people.length - 1 ||
        new Set(sources.map((x) => String(x.donor_id))).size !== sources.length
      )
        continue;
      for (const channel of CHANNELS) {
        const c = p.channels?.[channel];
        if (
          !c ||
          c.available === false ||
          !finite(c.events) ||
          !finite(c.received)
        )
          continue;
        const n = Number(c.events),
          d = Number(c.received);
        const sourceCounts = sources.map((s) => s.channels?.[channel]);
        if (
          n < 0 ||
          d < 0 ||
          n > d ||
          !Number.isInteger(n) ||
          !Number.isInteger(d) ||
          sourceCounts.some(
            (s) =>
              !s ||
              s.available === false ||
              !finite(s.events) ||
              Number(s.received) !== d,
          ) ||
          sum(sourceCounts.map((s) => Number(s.events))) !== d
        )
          continue;
        const subtypeTotals = TYPES[channel].map((type) =>
          sum(
            sources.map((s) => Number(s.channels?.ALL?.subtypes?.[type]) || 0),
          ),
        );
        if (sum(subtypeTotals) !== d) continue;
        const seats = SEATS.map((s) => Number(p.seat_exposure?.[s]) || 0);
        if (seats.some((x) => x < 0) || sum(seats) <= 0) continue;
        const donor = (game.players || []).find(
          (x) => String(x.user_id) === String(p.donor_id),
        );
        const recipient = (game.players || []).find(
          (x) => String(x.user_id) === String(p.recipient_id),
        );
        const ratings =
          finite(donor?.elo_before) && finite(recipient?.elo_before)
            ? [Number(donor.elo_before), Number(recipient.elo_before)]
            : null;
        samples.push({
          game_id: id,
          t,
          date: day(t),
          relation: relation(p.donor_id, p.recipient_id),
          ratings,
          donor_id: String(p.donor_id),
          recipient_id: String(p.recipient_id),
          channel,
          n,
          d,
          seats,
          types: subtypeTotals,
        });
      }
    }
    if (!samples.length) continue;
    result.push({
      game_id: id,
      t,
      rule_key: String(game.rule_key || "unknown"),
      people,
      samples,
    });
  }
  return result.sort((a, b) => a.t - b.t || a.game_id.localeCompare(b.game_id));
}
function aggregate(rows, rule, channel) {
  const first = rows[0],
    n = sum(rows.map((r) => r.n)),
    d = sum(rows.map((r) => r.d));
  const seats = SEATS.map((_, i) => sum(rows.map((r) => r.seats[i])));
  const types = TYPES[channel].map((_, i) => sum(rows.map((r) => r.types[i])));
  const w2 = d > 0 ? sum(rows.map((r) => (r.d / d) ** 2)) : null;
  const within =
    d > 0 && w2 < 1
      ? sum(
          rows
            .filter((r) => r.d > 0)
            .map((r) => (r.d / d) * (r.n / r.d - n / d) ** 2),
        ) /
        (1 - w2)
      : null;
  return {
    relation: first.relation,
    donor_id: first.donor_id,
    recipient_id: first.recipient_id,
    rule_key: rule,
    channel,
    games: rows.length,
    dates: new Set(rows.map((r) => r.date)).size,
    numerator: n,
    denominator: d,
    value: ratio(n, d),
    weight_squared_sum: w2,
    within_variance: within,
    seats: seats.map((x) => ratio(x, sum(seats)) || 0),
    mixture: types.map((x) => ratio(x, d) || 0),
    ratings: rows.every((r) => r.ratings)
      ? [0, 1].map((i) => sum(rows.map((r) => r.ratings[i])) / rows.length)
      : null,
    game_ids: rows.map((r) => r.game_id),
    first_at: new Date(rows[0].t).toISOString(),
    last_at: new Date(rows.at(-1).t).toISOString(),
  };
}
function blocksFrom(games, o, stage) {
  const groups = new Map(),
    buckets = {};
  for (const game of games)
    for (const s of game.samples) {
      const k = JSON.stringify([game.rule_key, s.channel, s.relation]);
      if (!groups.has(k))
        groups.set(k, { rule: game.rule_key, channel: s.channel, rows: [] });
      groups.get(k).rows.push(s);
    }
  for (const { rule, channel, rows } of groups.values()) {
    const partition =
      hash(`holdout:${unordered(rows[0].donor_id, rows[0].recipient_id)}`) % 5;
    if (
      (stage === "training" && partition >= 3) ||
      (stage === "calibration" && partition !== 3) ||
      (stage === "validation" && partition !== 4)
    )
      continue;
    {
      const b = aggregate(rows, rule, channel);
      // Target-fold exclusion already removes every game shared by the target
      // relationship; time and relationship partitions separate reference roles.
      if (o.reference_split !== "relation_full_history") delete b.game_ids;
      if (b.denominator === 0) continue;
      const key = keyOf(rule, channel);
      (buckets[key] ??= []).push(b);
    }
  }
  for (const [key, values] of Object.entries(buckets)) {
    values.sort(
      (a, b) =>
        hash(a.relation) - hash(b.relation) ||
        a.relation.localeCompare(b.relation),
    );
    if (stage === "validation") {
      buckets[key] = ["1", "2-7", "8+"].flatMap((band) =>
        values
          .filter((v) => broadBand(v.games) === band)
          .slice(0, o.max_validation_per_stratum),
      );
    } else buckets[key] = values.slice(0, o.reservoir_per_stratum);
  }
  return buckets;
}
function comparable(a, b, o) {
  return (
    a.relation !== b.relation &&
    b.denominator > 0 &&
    a.denominator > 0 &&
    Math.max(
      a.denominator / a.games / (b.denominator / b.games),
      b.denominator / b.games / (a.denominator / a.games),
    ) <= o.exposure_ratio_limit &&
    a.seats.every((v, i) => Math.abs(v - b.seats[i]) <= o.seat_tolerance) &&
    a.mixture.every(
      (v, i) => Math.abs(v - b.mixture[i]) <= o.mixture_tolerance,
    ) &&
    !!a.ratings === !!b.ratings &&
    (!a.ratings ||
      a.ratings.every(
        (v, i) => Math.abs(v - b.ratings[i]) <= o.rating_tolerance,
      ))
  );
}
function selectPeers(bucket, target, o, excludeGames = true) {
  const gameSet =
    excludeGames && target.game_ids ? new Set(target.game_ids) : null;
  return (bucket || [])
    .filter(
      (b) =>
        comparable(target, b, o) &&
        (!gameSet || !b.game_ids?.some((id) => gameSet.has(id))),
    )
    .map((b) => ({
      b,
      distance:
        Math.abs(
          Math.log(
            b.denominator / b.games / (target.denominator / target.games),
          ),
        ) +
        sum(b.seats.map((v, i) => Math.abs(v - target.seats[i]))) +
        sum(b.mixture.map((v, i) => Math.abs(v - target.mixture[i]))),
    }))
    .sort(
      (a, b) =>
        a.distance - b.distance || hash(a.b.relation) - hash(b.b.relation),
    )
    .slice(0, o.max_reference_relations)
    .map((x) => x.b);
}
function trainingPrediction(fold, target, o) {
  const key = keyOf(target.rule_key, target.channel, bandOf(target.games));
  const peers = selectPeers(fold.training[key], target, o);
  if (peers.length < o.min_training_relations)
    return { status: "insufficient_training_peers", peers };
  const values = peers.map((p) => p.value),
    expected = sum(values) / values.length;
  const repeated = peers.filter(
    (p) => p.within_variance !== null && p.games > 1,
  );
  if (repeated.length < o.min_repeated_training_relations)
    return { status: "insufficient_repeated_training_relations", peers };
  const sigma2 = sum(repeated.map((p) => p.within_variance)) / repeated.length;
  const between =
    sum(values.map((v) => (v - expected) ** 2)) /
    Math.max(1, values.length - 1);
  const noise =
    (sigma2 * sum(peers.map((p) => p.weight_squared_sum))) / peers.length;
  const tau2 = Math.max(0, between - noise);
  // Retain uncertainty in the estimated mean as well as a persistent relation
  // component. More games never erase genuine between-relationship variation.
  const meanVariance = between / peers.length;
  const variance = tau2 + sigma2 * target.weight_squared_sum + meanVariance;
  const scale = Math.sqrt(Math.max(0, variance));
  if (!(scale > 1e-12)) {
    // A deterministic legal source (for example CHI exclusively upstream) has
    // zero spread; do not invent Gaussian noise or call its normal 100% odd.
    if (values.every((v) => v === expected))
      return {
        status: "available",
        peers,
        expected,
        scale: 0,
        variance_components: { relationship: 0, game: 0, mean_estimation: 0 },
        repeated_relations: repeated.length,
      };
    return { status: "unidentified_predictive_variance", peers };
  }
  return {
    status: "available",
    peers,
    expected,
    scale,
    repeated_relations: repeated.length,
    variance_components: {
      relationship: tau2,
      game: sigma2,
      mean_estimation: meanVariance,
    },
  };
}
function calibratedPrediction(fold, target, o) {
  const base = trainingPrediction(fold, target, o);
  if (base.status !== "available") return base;
  const key = keyOf(target.rule_key, target.channel, bandOf(target.games));
  const calibration = selectPeers(fold.calibration[key], target, o);
  if (calibration.length < o.min_calibration_relations)
    return { ...base, status: "insufficient_calibration_peers", calibration };
  const correction = quantile(
    calibration.map((c) => c.nonconformity),
    o.confidence,
  );
  if (correction === null)
    return { ...base, status: "insufficient_calibration_tail", calibration };
  const prediction = {
    ...base,
    status: "available",
    calibration,
    correction,
    raw_upper: base.expected,
  };
  return { ...prediction, ...predictionBounds(prediction, target, o) };
}
// Numerical binomial probabilities, initialized at the mode to avoid P(0)
// underflow. This is an extra independence-model screen, not a replacement
// for the empirical whole-game/relationship predictive distribution.
function binomialReference(n, probability, confidence = 0.95, observed = null) {
  if (!Number.isSafeInteger(n) || n < 1) throw new Error("invalid_binomial_event_count");
  const p = Math.max(0, Math.min(1, probability));
  if (!Number.isFinite(p)) throw new Error("invalid_binomial_probability");
  if (p === 0 || p === 1) {
    const point = p === 0 ? 0 : n;
    return { upper: point / n, tail: observed === null ? null : Number(observed <= point), events: n };
  }
  const mode = Math.min(n, Math.floor((n + 1) * p));
  const weights = new Float64Array(n + 1);
  weights[mode] = 1;
  let total = 1;
  for (let k = mode; k > 0; k--) {
    weights[k - 1] = weights[k] * k / (n - k + 1) * (1 - p) / p;
    total += weights[k - 1];
  }
  for (let k = mode; k < n; k++) {
    weights[k + 1] = weights[k] * (n - k) / (k + 1) * p / (1 - p);
    total += weights[k + 1];
  }
  let cumulative = 0, upper = n, tail = 0, found = false;
  for (let k = 0; k <= n; k++) {
    cumulative += weights[k];
    if (!found && cumulative >= confidence * total) { upper = k; found = true; }
    if (observed !== null && k >= observed) tail += weights[k];
  }
  return { upper: upper / n, tail: observed === null ? null : observed <= 0 ? 1 : Math.min(1, tail / total), events: n };
}
function predictionBounds(prediction, target, o) {
  const empirical = Math.max(0, Math.min(1, prediction.expected + prediction.correction * prediction.scale));
  // Curve event counts are rounded explicitly; real observations are integers.
  const n = Math.max(1, Math.round(target.denominator));
  const observed = target.prediction_only ? null : target.numerator;
  const prior = prediction.binomial;
  const binomial = prior && prior.events === n && prior.probability === prediction.expected && prior.observed === observed
    ? prior
    : { ...binomialReference(n, prediction.expected, o.confidence, observed), probability: prediction.expected, observed };
  return { upper: Math.max(empirical, binomial.upper), empirical_upper: empirical,
    small_sample_upper: binomial.upper, binomial };
}
function supportedPrediction(prediction, target, validation, o) {
  const weight = Math.max(target.weight_squared_sum, validation.min_supported_weight_squared_sum || 0);
  const v = prediction.variance_components;
  const result = {
    ...prediction,
    scale: Math.sqrt(Math.max(0, v.relationship + v.game * weight + v.mean_estimation)),
    prediction_weight_squared_sum: weight,
  };
  return { ...result, ...predictionBounds(result, target, o) };
}
function coverageReport(rows, confidence = 0.95) {
  const n = rows.length,
    k = rows.filter((r) => r.covered).length;
  if (!n)
    return { relations: 0, covered: 0, coverage: null, interval_95: null };
  // Descriptive relation-level Wilson interval. Related players/games can still
  // be dependent; its limitations are explicit and it is not a certification.
  const z = 1.95996398454,
    p = k / n,
    den = 1 + (z * z) / n,
    c = (p + (z * z) / (2 * n)) / den;
  const h = (z * Math.sqrt((p * (1 - p)) / n + (z * z) / (4 * n * n))) / den;
  return {
    relations: n,
    covered: k,
    coverage: p,
    interval_95: [c - h, c + h],
    nominal: confidence,
    interval_method:
      "relation_level_wilson_descriptive_shared_players_may_depend",
  };
}
function validationState(report, o) {
  if (report.relations < o.min_validation_relations)
    return "insufficient_validation";
  if (
    report.coverage < o.validation_min_coverage ||
    report.interval_95?.[1] < o.confidence
  )
    return "undercoverage";
  return "validated_reference";
}
function targetValidation(fold, target, o) {
  const bucket = fold.validation[keyOf(target.rule_key, target.channel)] || [];
  const rows = selectPeers(bucket, target, o);
  const overall = coverageReport(rows, o.confidence);
  const band = broadBand(target.games),
    same = selectPeers(
      bucket.filter((r) => broadBand(r.games) === band),
      target,
      o,
    );
  const report = coverageReport(same, o.confidence);
  const state = report.interval_95?.[1] < o.confidence
    ? "undercoverage"
    : validationState(overall, o);
  const minWeight = rows.length ? Math.min(...rows.map(r => r.weight_squared_sum)) : null;
  return {
    ...overall,
    overall,
    calibration_scope: "matched_reference_marginal_across_sample_sizes",
    sample_band: band,
    sample_band_diagnostic: { ...report, status: validationState(report, o) },
    status: state,
    max_validated_games: rows.length
      ? Math.max(...rows.map((r) => r.games))
      : 0,
    min_supported_weight_squared_sum: minWeight,
    max_supported_effective_games: minWeight > 0 ? 1 / minWeight : null,
    extrapolation: minWeight !== null && target.weight_squared_sum < minWeight,
  };
}
function trainPopulationModel(rawGames, options = {}) {
  const o = optionsOf(options),
    games = normalizeGames(rawGames);
  // Split at whole timestamp groups, so tied games never cross the boundary.
  const times = [...new Set(games.map((g) => g.t))];
  if (
    games.length < 3 ||
    (times.length < 3 && o.reference_split === "time_and_relation")
  ) {
    const empty = {
      version: VERSION,
      options: o,
      status:
        games.length < 3 ? "insufficient_reference" : "insufficient_time_split",
      reference_label:
        "historical_reference_population_not_verified_clean_players",
      split: null,
      validation: { groups: [] },
      supported_channels: CHANNELS,
      block_storage: { encoding: "column_arrays", columns: BLOCK_COLUMNS },
      folds: Array.from({ length: o.folds }, (_, index) => ({
        index,
        training: {},
        calibration: {},
        validation: {},
        game_counts: { training: 0, calibration: 0, validation: 0 },
      })),
    };
    empty.serialized_bytes = Buffer.byteLength(JSON.stringify(empty));
    return empty;
  }
  const trainEnd =
    times[Math.max(0, Math.floor(times.length * o.train_fraction) - 1)];
  const calEnd =
    times.length < 3
      ? times.at(-1)
      : times[
          Math.min(
            times.length - 2,
            Math.max(
              times.indexOf(trainEnd) + 1,
              Math.floor(
                times.length * (o.train_fraction + o.calibration_fraction),
              ) - 1,
            ),
          )
        ];
  const model = {
    version: VERSION,
    status: "reference_fitted_validation_required_per_relationship",
    options: o,
    method:
      "hierarchical_relation_location_scale_with_heldout_calibration_and_discrete_event_screen",
    validation_design:
      o.reference_split === "relation_full_history"
        ? "cross_sectional_unordered_relation_holdout"
        : "time_and_unordered_relation_holdout",
    temporal_validation:
      o.reference_split === "relation_full_history"
        ? { status: "separate_diagnostic_required" }
        : { status: "included_as_time_and_relation_holdout" },
    reference_label:
      "historical_reference_population_not_verified_clean_players",
    split: {
      strategy: o.reference_split,
      training_end:
        o.reference_split === "relation_full_history"
          ? null
          : new Date(trainEnd).toISOString(),
      calibration_end:
        o.reference_split === "relation_full_history"
          ? null
          : new Date(calEnd).toISOString(),
      validation_end: new Date(games.at(-1).t).toISOString(),
    },
    folds: [],
    validation: { groups: [] },
    supported_channels: CHANNELS,
    sampling: {
      method: "deterministic_outcome_independent_relation_hash_reservoir",
      per_stratum_cap: o.reservoir_per_stratum,
      relation_partition:
        "unordered_pair_hash_holdout_train_60_calibration_20_validation_20",
      game_blocks: "whole_relationship_weighted_rate_and_within_game_variance",
    },
    limitations: [
      "conditioning_is_observational_not_causal",
      "shared_players_can_correlate_relationships",
      "95_percent_is_empirical_reference_coverage_not_cheating_probability",
      "coverage_is_marginal_across_sample_sizes_not_a_guarantee_for_each_size",
      "binomial_event_screen_only_reduces_flags_and_does_not_assume_actual_events_are_independent",
      "fold_exclusion_and_sparse_strata_can_leave_no_estimate",
    ],
  };
  for (let index = 0; index < o.folds; index++) {
    const eligible = games.filter(
      (g) =>
        !g.people.some((a, i) =>
          g.people.slice(i + 1).some((b) => foldFor(a, b, o.folds) === index),
        ),
    );
    const training =
        o.reference_split === "relation_full_history"
          ? eligible
          : eligible.filter((g) => g.t <= trainEnd),
      cal =
        o.reference_split === "relation_full_history"
          ? eligible
          : eligible.filter((g) => g.t > trainEnd && g.t <= calEnd),
      val =
        o.reference_split === "relation_full_history"
          ? eligible
          : eligible.filter((g) => g.t > calEnd);
    const fold = {
      index,
      training: blocksFrom(training, o, "training"),
      calibration: {},
      validation: {},
      game_counts: {
        training: training.length,
        calibration: cal.length,
        validation: val.length,
      },
    };
    const calibration = blocksFrom(cal, o, "calibration");
    for (const [key, blocks] of Object.entries(calibration))
      for (const b of blocks) {
        const prediction = trainingPrediction(fold, b, o);
        if (prediction.status === "available")
          (fold.calibration[key] ??= []).push({
            ...b,
            nonconformity:
              prediction.scale > 0
                ? (b.value - prediction.expected) / prediction.scale
                : b.value === prediction.expected
                  ? 0
                  : Math.sign(b.value - prediction.expected) *
                    Number.MAX_SAFE_INTEGER,
          });
      }
    for (const [key, blocks] of Object.entries(
      blocksFrom(val, o, "validation"),
    )) {
      const scored = [];
      for (const b of blocks) {
        const p = calibratedPrediction(fold, b, o);
        if (p.status === "available")
          scored.push({
            ...b,
            covered: b.value <= p.upper,
            predicted_upper: p.upper,
          });
      }
      fold.validation[key] = scored;
      for (const band of ["1", "2-7", "8+"]) {
        const rows = scored.filter((r) => broadBand(r.games) === band),
          report = coverageReport(rows, o.confidence);
        model.validation.groups.push({
          fold: index,
          key,
          sample_band: band,
          ...report,
          status: validationState(report, o),
          attempted: blocks.filter((r) => broadBand(r.games) === band).length,
        });
      }
    }
    model.folds.push(fold);
  }
  model.block_storage = { encoding: "column_arrays", columns: BLOCK_COLUMNS };
  for (const fold of model.folds)
    for (const stage of ["training", "calibration", "validation"])
      fold[stage] = packBuckets(fold[stage]);
  const size = Buffer.byteLength(JSON.stringify(model));
  if (size > o.max_serialized_bytes)
    throw new Error(`model_serialized_size_exceeds_limit:${size}`);
  model.serialized_bytes = size;
  return model;
}
function distribution(values, shift = 0) {
  const counts = Array(20).fill(0);
  for (const v of values)
    counts[
      Math.min(19, Math.floor(Math.max(0, Math.min(1, v + shift)) * 20))
    ]++;
  return counts.map((count, i) => ({
    lower: i / 20,
    upper: (i + 1) / 20,
    count,
    mass: ratio(count, values.length) || 0,
  }));
}
function scorePopulationRelationship(model, input) {
  return scoreNormalized(model, input, normalizeGames(input.games || []));
}
// Snapshot API: source game objects must remain unchanged for the scorer lifetime.
// Selectors use IDs, while all statistical facts come from the normalized snapshot.
function createPopulationScorer(model, rawGames) {
  const normalized = normalizeGames(rawGames || []);
  const byId = new Map(normalized.map(g => [g.game_id, g]));
  const known = new Set((rawGames || []).map(g => String(g.game_id ?? g.id ?? "")));
  return input => {
    const seen = new Set(), selected = [];
    for (const game of input.games || []) {
      const id = String(game.game_id ?? game.id ?? "");
      if (seen.has(id)) throw new Error("invalid_or_duplicate_game_identity_or_timestamp");
      if (!known.has(id)) throw new Error("unknown_population_scorer_game");
      seen.add(id);
      if (byId.has(id)) selected.push(byId.get(id));
    }
    selected.sort((a,b) => a.t-b.t || a.game_id.localeCompare(b.game_id));
    return scoreNormalized(model, input, selected);
  };
}
function scoreNormalized(model, input, normalized) {
  if (model?.version !== VERSION)
    throw new Error("unsupported_population_model_version");
  const o = model.options,
    fold = decodeFold(
      model,
      foldFor(input.donor_id, input.recipient_id, o.folds),
    );
  const rel = relation(input.donor_id, input.recipient_id),
    rule = String(input.rule_key);
  const channels = {};
  for (const channel of CHANNELS) {
    const rows = normalized
      .filter((g) => g.rule_key === rule)
      .flatMap((g) =>
        g.samples.filter((s) => s.relation === rel && s.channel === channel),
      );
    const target = rows.length ? aggregate(rows, rule, channel) : null;
    const empty = {
      actual: {
        numerator: target?.numerator ?? null,
        denominator: target?.denominator ?? null,
        value: target?.value ?? null,
      },
      events: target?.numerator ?? null,
      expected: null,
      reference_upper_95: null,
      p_value: null,
      flag: false,
      score: null,
      distribution: { bins: [] },
      sample_size_range: target
        ? [2 ** bandOf(target.games), 2 ** (bandOf(target.games) + 1) - 1]
        : null,
      sample_size_band: [],
      calibrated: false,
      reliability: {
        games: target?.games ?? 0,
        dates: target?.dates ?? 0,
        received_events: target?.denominator ?? 0,
      },
      status: target ? "no_received_events" : "no_valid_source_records",
      validation: null,
    };
    if (!target || target.denominator === 0) {
      channels[channel] = empty;
      continue;
    }
    let predicted = calibratedPrediction(fold, target, o);
    if (predicted.status !== "available") {
      const hasLocation =
        Number.isFinite(predicted.expected) && Number.isFinite(predicted.scale);
      const residuals = hasLocation
        ? predicted.peers.map((peer) => {
            const v = predicted.variance_components;
            const s = Math.sqrt(
              Math.max(
                0,
                v.relationship +
                  v.game * peer.weight_squared_sum +
                  v.mean_estimation,
              ),
            );
            return s > 0 ? (peer.value - predicted.expected) / s : 0;
          })
        : [];
      const previewQ = quantile(residuals, o.confidence);
      channels[channel] = {
        ...empty,
        status: predicted.status,
        expected: hasLocation ? predicted.expected : null,
        unvalidated_reference_upper:
          previewQ !== null
            ? Math.max(
                0,
                Math.min(1, predicted.expected + previewQ * predicted.scale),
              )
            : null,
        distribution: hasLocation
          ? {
              bins: distribution(
                residuals.map((z) => predicted.expected + z * predicted.scale),
              ),
              expected: predicted.expected,
              upper95: null,
              actual: target.value,
            }
          : empty.distribution,
        distribution_kind: hasLocation
          ? "training_standardized_residual_preview_not_independently_calibrated"
          : null,
        variance_components: predicted.variance_components ?? null,
        predictive_scale: predicted.scale ?? null,
        effective_games: 1 / target.weight_squared_sum,
        reliability: {
          ...empty.reliability,
          training_relations: predicted.peers?.length ?? 0,
          calibration_relations: predicted.calibration?.length ?? 0,
          repeated_training_relations: predicted.repeated_relations ?? 0,
        },
      };
      continue;
    }
    const validation = targetValidation(fold, target, o),
      state = validation.status;
    predicted = supportedPrediction(predicted, target, validation, o);
    const residual =
      predicted.scale > 0
        ? (target.value - predicted.expected) / predicted.scale
        : target.value === predicted.expected
          ? 0
          : Math.sign(target.value - predicted.expected) *
            Number.MAX_SAFE_INTEGER;
    const empiricalP =
      (1 +
        predicted.calibration.filter((c) => c.nonconformity >= residual)
          .length) /
      (predicted.calibration.length + 1);
    const guardP = predicted.binomial.tail;
    const p = Math.max(empiricalP, guardP);
    const reliable = state === "validated_reference";
    const flag =
      reliable && target.value > predicted.upper && p <= 1 - o.confidence;
    channels[channel] = {
      ...empty,
      status: state,
      expected: predicted.expected,
      reference_upper_95: reliable ? predicted.upper : null,
      unvalidated_reference_upper: reliable ? null : predicted.upper,
      p_value: reliable ? p : null,
      p_value_method:
        "maximum_of_calibration_tail_rank_and_binomial_event_screen_not_cheating_probability",
      empirical_p_value: reliable ? empiricalP : null,
      binomial_event_guard: {
        upper: predicted.small_sample_upper,
        tail: guardP,
        events: predicted.binomial.events,
        purpose: "additional_independence_model_screen_not_independent_calibration",
      },
      flag,
      score: reliable
        ? flag && predicted.upper < 1
          ? (100 * (target.value - predicted.upper)) / (1 - predicted.upper)
          : 0
        : null,
      calibrated: reliable,
      calibration_status: state,
      distribution: {
        bins: distribution(
          predicted.calibration.map(
            (p) => predicted.expected + predicted.scale * p.nonconformity,
          ),
        ),
        expected: predicted.expected,
        upper95: reliable ? predicted.upper : null,
        actual: target.value,
      },
      distribution_kind:
        "heldout_standardized_residuals_scaled_to_target_sample_structure",
      variance_components: predicted.variance_components,
      predictive_scale: predicted.scale,
      effective_games: 1 / target.weight_squared_sum,
      prediction_effective_games: 1 / predicted.prediction_weight_squared_sum,
      calibration_correction: predicted.correction,
      confidence: o.confidence,
      validation,
      reliability: {
        ...empty.reliability,
        training_relations: predicted.peers.length,
        calibration_relations: predicted.calibration.length,
        validation_relations: validation.relations,
        repeated_training_relations: predicted.repeated_relations,
      },
      conditioning: {
        rule_key: rule,
        seat_exposure: target.seats,
        action_mixture: target.mixture,
        exposure_ratio_limit: o.exposure_ratio_limit,
        model_fold: fold.index,
        prior_elo: target.ratings,
        rating_status: target.ratings
          ? "saved_pre_game_matched"
          : "prior_rating_unavailable",
      },
    };
    const maximumBand = Math.max(5, Math.ceil(Math.log2(target.games)));
    const bands = [
      ...new Set(
        Array.from({ length: 6 }, (_, i) => Math.round((i * maximumBand) / 5)),
      ),
    ];
    const curveValidations = new Map([[broadBand(target.games), validation]]);
    for (const band of bands) {
      const n = 2 ** band,
        hypothetical = {
          ...target,
          games: n,
          prediction_only: true,
          denominator: (target.denominator * n) / target.games,
          weight_squared_sum: Math.min(
            1,
            (target.weight_squared_sum * target.games) / n,
          ),
        };
      const broad = broadBand(n);
      if (!curveValidations.has(broad))
        curveValidations.set(broad, targetValidation(fold, hypothetical, o));
      const template = curveValidations.get(broad),
        report = {
          ...template,
          extrapolation: hypothetical.weight_squared_sum < template.min_supported_weight_squared_sum,
        };
      const prediction = supportedPrediction(predicted, hypothetical, report, o);
      const available = report.status === "validated_reference";
      channels[channel].sample_size_band.push({
        sample_size: n,
        prediction_events: prediction.binomial.events,
        expected: prediction.expected,
        lower95: null,
        upper95: available ? prediction.upper : null,
        calibrated: available,
        validation: report,
        unvalidated_reference_upper: available ? null : prediction.upper,
      });
    }
  }
  const known = Object.values(channels)
    .map((c) => c.score)
    .filter((x) => x !== null);
  return {
    model_version: VERSION,
    reference_split: o.reference_split,
    donor_id: String(input.donor_id),
    recipient_id: String(input.recipient_id),
    rule_key: rule,
    channels,
    anomaly: CHANNELS.some((c) => channels[c].flag),
    delivery_score: known.length ? Math.max(...known) : null,
    score_combination: "maximum_independent_channel_priority_no_ALL_RON_sum",
    reference_label: model.reference_label,
  };
}
module.exports = {
  VERSION,
  DEFAULTS,
  trainPopulationModel,
  scorePopulationRelationship,
  createPopulationScorer,
  binomialReference,
};
