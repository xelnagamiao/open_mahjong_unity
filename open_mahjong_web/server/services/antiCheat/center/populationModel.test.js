"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const {
  trainPopulationModel,
  scorePopulationRelationship,
  VERSION,
  createPopulationScorer,
  binomialReference,
} = require("./populationModel");

function game(group, stage, round, extreme = false) {
  const people = Array.from({ length: 4 }, (_, i) => String(group * 10 + i));
  const pairs = [];
  for (let recipient = 0; recipient < 4; recipient++) {
    const donors = [0, 1, 2, 3].filter((i) => i !== recipient);
    const wobble = (group + round) % 5;
    const counts = extreme
      ? [94 + wobble, 3 - Math.floor(wobble / 2), 3 - Math.ceil(wobble / 2)]
      : [55 + wobble, 25 - wobble, 20];
    donors.forEach((donor, index) => {
      const n = counts[index];
      const c = {
        events: n,
        received: 100,
        available: true,
        subtypes: { CHI: 0, PENG: 0, GANG: 0, RON: n },
      };
      pairs.push({
        donor_id: people[donor],
        recipient_id: people[recipient],
        delivery_supported: true,
        seat_exposure: { upstream: 4, opposite: 4, downstream: 4 },
        channels: {
          ALL: { ...c },
          RON: { ...c },
          MELD: {
            events: 0,
            received: 0,
            available: true,
            subtypes: { CHI: 0, PENG: 0, GANG: 0, RON: 0 },
          },
        },
      });
    });
  }
  return {
    game_id: `${group}-${stage}-${round}`,
    created_at: new Date(
      Date.UTC(2025, 0, 1) +
        stage * 100 * 86400000 +
        round * 86400000 +
        group * 1000,
    ).toISOString(),
    rule_key: "mcr-four-tier-a",
    competitive: true,
    delivery_supported: true,
    players: people.map((user_id) => ({ user_id })),
    pairs,
  };
}
const data = Array.from({ length: 260 }, (_, g) =>
  Array.from({ length: 3 }, (_, s) =>
    Array.from({ length: 4 }, (_, r) => game(g, s, r)),
  ),
).flat(2);
const options = {
  reference_split: "time_and_relation",
  folds: 10,
  train_fraction: 1 / 3,
  calibration_fraction: 1 / 3,
  min_training_relations: 12,
  min_calibration_relations: 20,
  min_validation_relations: 12,
  max_reference_relations: 100,
  reservoir_per_stratum: 130,
  max_validation_per_stratum: 100,
};
let model;
test("empty and same-time reference data return an inspectable unavailable model, not a failed update", () => {
  const empty = trainPopulationModel([]);
  assert.equal(empty.status, "insufficient_reference");
  assert.equal(empty.split, null);
  const tied = Array.from({ length: 3 }, (_, i) => ({
    ...game(i, 0, 0),
    created_at: "2025-01-01T00:00:00Z",
  }));
  assert.equal(
    trainPopulationModel(tied, { reference_split: "time_and_relation" }).status,
    "insufficient_time_split",
  );
  const result = scorePopulationRelationship(empty, {
    donor_id: "99990",
    recipient_id: "99991",
    rule_key: "mcr-four-tier-a",
    games: Array.from({ length: 4 }, (_, r) => game(9999, 3, r, true)),
  });
  assert.equal(result.channels.RON.status, "insufficient_training_peers");
  assert.equal(result.channels.RON.flag, false);
  assert.equal(result.channels.RON.reference_upper_95, null);
});
test("fits a serializable bounded, time- and relationship-separated reference model", () => {
  model = trainPopulationModel(data, options);
  assert.equal(model.version, VERSION);
  assert.ok(model.serialized_bytes < 48 * 1024 * 1024);
  assert.ok(model.validation.groups.some((g) => g.relations > 0));
  assert.ok(
    model.validation.groups.every(
      (g) => g.coverage === null || g.coverage === g.covered / g.relations,
    ),
  );
  for (const fold of model.folds) {
    const identities = (buckets) =>
      new Set(
        Object.values(buckets)
          .flat()
          .map((x) => JSON.stringify(JSON.parse(x[0]).sort())),
      );
    const training = identities(fold.training),
      cal = identities(fold.calibration),
      val = identities(fold.validation);
    assert.ok([...cal].every((id) => !training.has(id)));
    assert.ok([...val].every((id) => !training.has(id) && !cal.has(id)));
    assert.ok(
      Object.values(fold.training).every(
        (rows) => rows.length <= options.reservoir_per_stratum,
      ),
    );
  }
  assert.deepEqual(JSON.parse(JSON.stringify(model)).split, model.split);
});
test("full-history relationship holdout retains long samples and is not labeled temporal validation", () => {
  const cross = trainPopulationModel(data, {
    ...options,
    reference_split: "relation_full_history",
  });
  assert.equal(cross.split.strategy, "relation_full_history");
  const trainingRows = cross.folds.flatMap((f) =>
    Object.values(f.training).flat(),
  );
  assert.ok(trainingRows.some((r) => r[1] === 12));
  const c = scorePopulationRelationship(cross, {
    donor_id: "99990",
    recipient_id: "99991",
    rule_key: "mcr-four-tier-a",
    games: Array.from({ length: 12 }, (_, r) => game(9999, 3, r, true)),
  }).channels.RON;
  assert.equal(c.validation.sample_band, "8+");
  assert.equal(c.calibrated, true);
  assert.equal(c.flag, true);
});
test("scoring uses observed reference blocks, preserves received-event denominators, and makes independent RON output", () => {
  const games = Array.from({ length: 4 }, (_, r) => game(9999, 3, r, true));
  const result = scorePopulationRelationship(model, {
    donor_id: "99990",
    recipient_id: "99991",
    rule_key: "mcr-four-tier-a",
    games,
  });
  const c = result.channels.RON;
  assert.equal(c.actual.denominator, 400);
  assert.ok(c.actual.value > 0.9);
  assert.equal(result.channels.ALL.actual.value, c.actual.value);
  assert.equal(
    c.distribution_kind,
    "heldout_standardized_residuals_scaled_to_target_sample_structure",
  );
  assert.equal(c.distribution.bins.length, 20);
  assert.ok(
    Math.abs(c.distribution.bins.reduce((a, b) => a + b.mass, 0) - 1) < 1e-12,
  );
  assert.ok(
    c.distribution.bins.filter((b) => b.count).every((b) => b.upper <= 0.65),
  );
  assert.ok(c.sample_size_band.length <= 6);
  if (c.calibrated) {
    assert.ok(c.reference_upper_95 < c.actual.value);
    assert.equal(c.flag, true);
    assert.equal(
      result.delivery_score,
      Math.max(result.channels.ALL.score, c.score),
    );
  } else {
    assert.equal(c.flag, false);
    assert.equal(c.reference_upper_95, null);
  }
});
test("target outcomes cannot alter the learned reference distribution", () => {
  const make = (extreme) =>
    scorePopulationRelationship(model, {
      donor_id: "99990",
      recipient_id: "99991",
      rule_key: "mcr-four-tier-a",
      games: Array.from({ length: 4 }, (_, r) => game(9999, 3, r, extreme)),
    });
  const normal = make(false).channels.RON,
    extreme = make(true).channels.RON;
  assert.notEqual(normal.actual.value, extreme.actual.value);
  assert.deepEqual(normal.distribution.bins, extreme.distribution.bins);
  assert.equal(normal.expected, extreme.expected);
  assert.equal(normal.reference_upper_95, extreme.reference_upper_95);
});
test("large samples retain relationship variation and freeze unsupported precision without excluding the target", () => {
  const score = (n) =>
    scorePopulationRelationship(model, {
      donor_id: "99990",
      recipient_id: "99991",
      rule_key: "mcr-four-tier-a",
      games: Array.from({ length: n }, (_, r) => game(9999, 3, r, true)),
    }).channels.RON;
  const small = score(4),
    large = score(100);
  assert.ok(small.variance_components.relationship > 0);
  assert.ok(
    large.predictive_scale >= Math.sqrt(large.variance_components.relationship),
  );
  assert.ok(large.predictive_scale <= small.predictive_scale);
  assert.equal(large.status, "validated_reference");
  assert.equal(large.validation.extrapolation, true);
  assert.ok(large.prediction_effective_games < large.effective_games);
  assert.ok(Math.abs(large.predictive_scale - small.predictive_scale) < 1e-12);
  assert.ok(Number.isFinite(large.reference_upper_95));
  assert.equal(large.flag, true);
  assert.ok(large.distribution.bins.length > 0);
});
test("a single game enters prediction, while unseen rules and poor heldout coverage cannot manufacture a 95% line", () => {
  const input = {
    donor_id: "99990",
    recipient_id: "99991",
    rule_key: "mcr-four-tier-a",
    games: [game(9999, 3, 0, true)],
  };
  for (const p of input.games[0].pairs.filter(p => p.recipient_id === "99991")) {
    for (const c of ["ALL", "RON"]) {
      p.channels[c].events = p.donor_id === "99990" ? 100 : 0;
      p.channels[c].subtypes = { CHI: 0, PENG: 0, GANG: 0, RON: p.channels[c].events };
    }
  }
  const small = scorePopulationRelationship(model, input).channels.RON;
  assert.equal(small.actual.numerator, 100);
  assert.equal(small.actual.denominator, 100);
  assert.equal(small.reliability.games, 1);
  assert.ok(Number.isFinite(small.expected));
  assert.ok(Number.isFinite(small.predictive_scale));
  assert.equal(small.validation.sample_band_diagnostic.relations, 0);
  assert.equal(small.calibrated, true);
  assert.ok(small.reference_upper_95 < 1);
  assert.equal(small.flag, true);
  const missing = scorePopulationRelationship(model, {
    ...input,
    rule_key: "unknown",
    games: Array.from({ length: 4 }, (_, r) => ({
      ...game(9999, 3, r, true),
      rule_key: "unknown",
    })),
  }).channels.RON;
  assert.equal(missing.calibrated, false);
  const broken = JSON.parse(JSON.stringify(model));
  for (const f of broken.folds)
    for (const rows of Object.values(f.validation))
      for (const r of rows) r[9] = false;
  const bad = scorePopulationRelationship(broken, {
    ...input,
    games: Array.from({ length: 4 }, (_, r) => game(9999, 3, r, true)),
  }).channels.RON;
  assert.equal(bad.reference_upper_95, null);
  assert.equal(bad.flag, false);
  assert.equal(bad.p_value, null);
});

test("ten same-day games and ten RON events are evaluated without a second sample gate", () => {
  function oneEvent(group, round, donorIndex) {
    const g = game(group, 0, round);
    g.created_at = new Date(Date.UTC(2025, 0, 1) + round * 60000 + group).toISOString();
    const sources = new Map();
    for (const p of g.pairs) {
      if (!sources.has(p.recipient_id)) sources.set(p.recipient_id, []);
      sources.get(p.recipient_id).push(p);
    }
    for (const [recipient, pairs] of sources) {
      let h = 2166136261;
      for (const c of `${group}/${round}/${recipient}`) h = Math.imul(h ^ c.charCodeAt(0), 16777619);
      const selected = donorIndex ?? ((h >>> 0) % 3);
      pairs.forEach((p, index) => {
        const events = index === selected ? 1 : 0;
        for (const c of ["ALL", "RON"]) p.channels[c] = {
          available: true, events, received: 1,
          subtypes: { CHI: 0, PENG: 0, GANG: 0, RON: events },
        };
      });
    }
    return g;
  }
  const reference = Array.from({ length: 520 }, (_, group) =>
    Array.from({ length: group < 130 ? 1 : group < 260 ? 3 : 10 }, (_, r) => oneEvent(group, r)),
  ).flat();
  const fitted = trainPopulationModel(reference, {
    ...options, reference_split: "relation_full_history",
    reservoir_per_stratum: 800,
    min_games: 999, min_dates: 999, min_events: 999,
  });
  for (const key of ["min_games", "min_dates", "min_events"])
    assert.equal(Object.hasOwn(fitted.options, key), false);
  const input = {
    donor_id: "99990", recipient_id: "99991", rule_key: "mcr-four-tier-a",
    games: Array.from({ length: 10 }, (_, r) => oneEvent(9999, r, 0)),
  };
  const ten = scorePopulationRelationship(fitted, input).channels.RON;
  assert.equal(ten.actual.numerator, 10);
  assert.equal(ten.actual.denominator, 10);
  assert.equal(ten.reliability.dates, 1);
  assert.ok(Math.abs(ten.effective_games - 10) < 1e-10);
  assert.equal(ten.calibrated, true, JSON.stringify(ten.validation));
  assert.equal(ten.flag, true);
  assert.ok(ten.reference_upper_95 < 1);
  assert.ok(ten.p_value <= 0.05);
  assert.ok(ten.p_value >= ten.binomial_event_guard.tail);
  assert.ok(ten.reference_upper_95 >= ten.binomial_event_guard.upper);
  const single = scorePopulationRelationship(fitted, { ...input, games: input.games.slice(0, 1) }).channels.RON;
  assert.equal(single.calibrated, true);
  assert.equal(single.flag, false);
  assert.equal(single.actual.value, 1);
  assert.equal(single.effective_games, 1);
  assert.ok(single.predictive_scale > ten.predictive_scale);
  assert.ok(single.reference_upper_95 >= ten.reference_upper_95);
  const balanced = scorePopulationRelationship(fitted, {
    ...input, games: Array.from({ length: 10 }, (_, r) => oneEvent(9999, r, r % 3)),
  }).channels.RON;
  assert.equal(balanced.calibrated, true);
  assert.equal(balanced.actual.denominator, 10);
  assert.equal(balanced.flag, false);
  const clusteredGames = structuredClone(input.games);
  for (const [i, g] of clusteredGames.entries()) for (const p of g.pairs) for (const c of ["ALL", "RON"]) {
    p.channels[c].events *= i === 0 ? 10 : 0;
    p.channels[c].received = i === 0 ? 10 : 0;
    p.channels[c].subtypes.RON = p.channels[c].events;
  }
  const clustered = scorePopulationRelationship(fitted, { ...input, games: clusteredGames }).channels.RON;
  assert.equal(clustered.actual.numerator, 10);
  assert.equal(clustered.actual.denominator, 10);
  assert.equal(clustered.calibrated, true);
  assert.equal(clustered.effective_games, 1);
  assert.ok(clustered.binomial_event_guard.upper < 1);
  assert.ok(clustered.predictive_scale > ten.predictive_scale);
  assert.ok(clustered.reference_upper_95 >= clustered.binomial_event_guard.upper);

  const sparseBand = structuredClone(fitted);
  for (const f of sparseBand.folds) for (const [key, rows] of Object.entries(f.validation)) {
    const long = rows.filter(r => r[1] >= 8).slice(0, 4);
    f.validation[key] = [...rows.filter(r => r[1] < 8), ...long];
  }
  const sparse = scorePopulationRelationship(sparseBand, input).channels.RON;
  assert.equal(sparse.calibrated, true);
  assert.equal(sparse.validation.sample_band_diagnostic.relations, 4);
  assert.equal(sparse.flag, true);
  const badBand = structuredClone(sparseBand);
  for (const f of badBand.folds) for (const rows of Object.values(f.validation))
    for (const r of rows) r[9] = r[1] < 8;
  const bad = scorePopulationRelationship(badBand, input).channels.RON;
  assert.ok(bad.validation.overall.coverage >= 0.9);
  assert.equal(bad.status, "undercoverage");
  assert.equal(bad.calibrated, false);
  assert.equal(bad.flag, false);
  assert.equal(bad.reference_upper_95, null);
});

test("zero received events and invalid records have distinct reasons, not target sample restrictions", () => {
  const input = {
    donor_id: "99990", recipient_id: "99991", rule_key: "mcr-four-tier-a",
    games: [game(9999, 3, 0, true)],
  };
  const zero = scorePopulationRelationship(model, input).channels.MELD;
  assert.equal(zero.status, "no_received_events");
  assert.equal(zero.actual.denominator, 0);
  assert.equal(zero.score, null);
  const missing = scorePopulationRelationship(model, { ...input, games: [] }).channels.RON;
  assert.equal(missing.status, "no_valid_source_records");
  assert.equal(missing.actual.denominator, null);
  assert.equal(missing.score, null);
});
test("a fitted location remains visible as an explicitly uncalibrated preview when independent calibration is sparse", () => {
  const sparse = JSON.parse(JSON.stringify(model));
  sparse.options.min_calibration_relations = 1000;
  const c = scorePopulationRelationship(sparse, {
    donor_id: "99990",
    recipient_id: "99991",
    rule_key: "mcr-four-tier-a",
    games: Array.from({ length: 4 }, (_, r) => game(9999, 3, r, true)),
  }).channels.RON;
  assert.equal(c.status, "insufficient_calibration_peers");
  assert.ok(Number.isFinite(c.expected));
  assert.equal(
    c.distribution_kind,
    "training_standardized_residual_preview_not_independently_calibrated",
  );
  assert.equal(c.distribution.bins.length, 20);
  assert.equal(c.reference_upper_95, null);
  assert.equal(c.p_value, null);
  assert.equal(c.flag, false);
  assert.equal(c.calibrated, false);
});
test("invalid source totals are not silently repaired into fractions and scoring does not mutate a fixed model", () => {
  const before = JSON.stringify(model),
    games = Array.from({ length: 4 }, (_, r) => game(9999, 3, r, true));
  for (const g of games)
    g.pairs.find(
      (p) => p.donor_id === "99990" && p.recipient_id === "99991",
    ).channels.RON.received = 10;
  const c = scorePopulationRelationship(model, {
    donor_id: "99990",
    recipient_id: "99991",
    rule_key: "mcr-four-tier-a",
    games,
  }).channels.RON;
  assert.equal(c.flag, false);
  assert.equal(JSON.stringify(model), before);
});

test("the target relationship fold removes its whole table, including unrelated directed sources, from all reference phases", () => {
  let h = 2166136261;
  for (const c of JSON.stringify(["0", "1"]))
    h = Math.imul(h ^ c.charCodeAt(0), 16777619);
  const fold = model.folds[(h >>> 0) % options.folds];
  for (const buckets of [fold.training, fold.calibration, fold.validation])
    for (const row of Object.values(buckets).flat()) {
      const [a, b] = JSON.parse(row[0]);
      assert.notEqual(Math.floor(Number(a) / 10), 0);
      assert.notEqual(Math.floor(Number(b) / 10), 0);
    }
});

test("legal upstream-only CHI sources keep their own empirical 100% reference rather than a thirds floor", () => {
  function chi(g) {
    const result = structuredClone(g);
    const byRecipient = new Map();
    for (const p of result.pairs) {
      if (!byRecipient.has(p.recipient_id)) byRecipient.set(p.recipient_id, []);
      byRecipient.get(p.recipient_id).push(p);
    }
    for (const sources of byRecipient.values())
      sources.forEach((p, i) => {
        const n = i === 0 ? 100 : 0;
        p.seat_exposure = {
          upstream: i === 0 ? 12 : 0,
          opposite: i === 1 ? 12 : 0,
          downstream: i === 2 ? 12 : 0,
        };
        const c = {
          events: n,
          received: 100,
          available: true,
          subtypes: { CHI: n, PENG: 0, GANG: 0, RON: 0 },
        };
        p.channels = {
          ALL: { ...c },
          MELD: { ...c },
          RON: {
            events: 0,
            received: 0,
            available: true,
            subtypes: { CHI: 0, PENG: 0, GANG: 0, RON: 0 },
          },
        };
      });
    return result;
  }
  const m = trainPopulationModel(data.map(chi), options);
  const c = scorePopulationRelationship(m, {
    donor_id: "99990",
    recipient_id: "99991",
    rule_key: "mcr-four-tier-a",
    games: Array.from({ length: 4 }, (_, r) => chi(game(9999, 3, r))),
  }).channels.ALL;
  assert.equal(c.actual.value, 1);
  assert.equal(c.expected, 1);
  assert.equal(c.flag, false);
  assert.equal(c.reference_upper_95, 1);
  assert.equal(c.score, 0);
});


test("exact discrete event screen uses inclusive tails and stable mode-relative probabilities", () => {
  const single = binomialReference(1, 0.3, 0.95, 1);
  assert.ok(Math.abs(single.tail - 0.3) < 1e-14);
  assert.equal(single.upper, 1);
  const ten = binomialReference(10, 0.3, 0.95, 10);
  assert.ok(Math.abs(ten.tail - 0.3 ** 10) < 1e-18);
  assert.equal(ten.upper, 0.5);
  const hundred = binomialReference(100, 0.3, 0.95, 100);
  assert.ok(hundred.upper < 1);
  assert.ok(hundred.tail > 0 && hundred.tail < 0.05);
  const rare = binomialReference(1, 0.01, 0.95, 1);
  assert.equal(rare.upper, 0);
  assert.ok(Math.abs(rare.tail - 0.01) < 1e-14);
  const big = binomialReference(100000, 0.5, 0.95, 50000);
  assert.ok(big.tail > 0.5 && big.tail < 0.51);
  assert.ok(big.upper > 0.5 && big.upper < 0.51);
  assert.equal(binomialReference(10, 0.3, 0.95, 0).tail, 1);
  assert.equal(binomialReference(10, 1, 0.95, 10).tail, 1);
  assert.equal(binomialReference(10, 0, 0.95, 1).tail, 0);
  assert.throws(() => binomialReference(0, 0.3), /invalid_binomial_event_count/);
  assert.throws(() => binomialReference(1.5, 0.3), /invalid_binomial_event_count/);
});

test("pre-normalized scorer equals pure API field for field and rejects duplicate or unknown selections", () => {
  const games = Array.from({ length: 10 }, (_, r) => game(9999, 3, r, true));
  const before = JSON.stringify(games);
  const score = createPopulationScorer(model, games);
  for (const selected of [[], games.slice(0, 1), games.slice(0, 4), [...games].reverse()]) {
    const input = { donor_id: "99990", recipient_id: "99991", rule_key: "mcr-four-tier-a", games: selected };
    assert.deepEqual(score(input), scorePopulationRelationship(model, input));
    for (const c of Object.values(score(input).channels))
      for (const point of c.sample_size_band) assert.ok(Number.isInteger(point.prediction_events));
  }
  const base = { donor_id: "99990", recipient_id: "99991", rule_key: "mcr-four-tier-a" };
  assert.throws(() => score({ ...base, games: [games[0], games[0]] }), /duplicate_game/);
  assert.throws(() => score({ ...base, games: [game(8888, 3, 0)] }), /unknown_population_scorer_game/);
  assert.throws(() => createPopulationScorer(model, [games[0], games[0]]), /duplicate_game/);
  assert.equal(JSON.stringify(games), before);
});
