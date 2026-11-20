'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { analyzeRecords, settingsFrom, buildWindow, verifyOpportunities } = require('./engine');
const { replayRecord, inferGuobiaoSeats, relation } = require('./replay');
const { runPython, evaluateDecisions } = require('./rules');
const { concentrationFixture, simpleRecord, physicalDrawRecord, physicalWinningRecord } = require('./fixtures.test-helper');

const OPTIONS = { as_of: '2026-11-14T00:00:00Z', windows_days: [30], compute_opportunities: false };
const pairOf = (result, a = '101', b = '102') => result.pairs.find((p) => p.donor_id === a && p.recipient_id === b);

test('reopenable snapshots match array analysis and quarantine late conflicting duplicates', async () => {
  const rows = concentrationFixture();
  rows.push(structuredClone(rows[0]));
  const conflict = structuredClone(rows[1]);
  conflict.record.game_title.stream_test_conflict = true;
  rows.push(conflict);
  let passes = 0;
  async function* source() { passes += 1; for (const row of rows) yield structuredClone(row); }
  assert.deepEqual(await analyzeRecords(source, OPTIONS), await analyzeRecords(rows, OPTIONS));
  assert.equal(passes, 2);
  assert.deepEqual(await analyzeRecords(source, { ...OPTIONS, analysis_concurrency: 4 }), await analyzeRecords(rows, OPTIONS));
});

test('parallel rule processes preserve qualified opportunities, payments and evidence ordering', async () => {
  const rows = [physicalWinningRecord('parallel-win'), physicalDrawRecord({ id: 'parallel-draw' })];
  const options = { ...OPTIONS, compute_opportunities: true };
  const sequential = await analyzeRecords(rows, options);
  const parallel = await analyzeRecords(rows, { ...options, analysis_concurrency: 4 });
  assert.deepEqual(parallel, sequential);
  assert.equal(parallel.quality.opportunity_verified_hands, 2);
});

test('ALL 30% does not suppress independent RON 80%; no invented 1/3 baseline', async () => {
  const result = await analyzeRecords(concentrationFixture(), OPTIONS);
  const pair = pairOf(result);
  assert.equal(pair.channels.ALL.source_share.numerator, 45);
  assert.equal(pair.channels.ALL.source_share.denominator, 150);
  assert.equal(pair.channels.ALL.source_share.value, 0.3);
  assert.equal(pair.channels.MELD.source_share.value, 33 / 135);
  assert.equal(pair.channels.RON.source_share.value, 0.8);
  assert.equal(pair.flag_all, false);
  assert.equal(pair.flag_meld, false);
  assert.equal(pair.flag_ron, true);
  assert.equal(pair.channels.RON.baseline_share, null);
  assert.equal(pair.channels.RON.lift, null);
  assert.equal(result.findings.find((f) => f.pair_key === pair.pair_key && f.channel === 'RON').status, 'observe');
  assert.ok(pair.evidence.length <= 50);
  assert.equal(pair.channels.RON.opportunity_creation_rate.value, null);
});

test('directed attribution and score payers follow current seats, not original player indices', async () => {
  const row = simpleRecord('switched', 0, 'RON', [1, 0, 3, 2]);
  const result = await analyzeRecords([row], OPTIONS);
  const pair = pairOf(result);
  assert.equal(pair.channels.RON.events, 1);
  assert.equal(pairOf(result, '102', '101').channels.RON.events, 0);
  assert.equal(pair.evidence[0].donor_seat, 1);
  assert.equal(pair.evidence[0].recipient_seat, 0);
  assert.equal(pair.score.direct_paid_to_recipient, 16);
  assert.equal(pair.score.recipient_ron_gain, 32);
  assert.equal(pair.score.others_paid_on_ron, 16);
  assert.equal(pair.score.combined_mean_with, 16);
  assert.deepEqual(result.games[0].score_deltas, { 101: -16, 102: 32, 103: -8, 104: -8 });
  assert.deepEqual(inferGuobiaoSeats(5), [1, 0, 3, 2]);
  assert.deepEqual(inferGuobiaoSeats(9), [3, 2, 0, 1]);
  assert.deepEqual(inferGuobiaoSeats(13), [2, 3, 1, 0]);
  assert.equal(relation(0, 1), 'upstream');
});

test('added kong does not duplicate pung delivery; robbing kong is SPECIAL; tsumo has no donor', async () => {
  const row = simpleRecord('rob', 0, 'MELD');
  row.record.game_round.round_index_1.action_ticks = [
    ['reset', 0], ['c', 17, 'F'], ['p', 17, 1, 17, 17], ['jg', 17, 'F'],
    ['hu_first', 2, 8, ['抢杠和'], [-8, -16, 32, -8], 17], ['end'],
  ];
  const self = simpleRecord('self', 0, 'RON');
  self.record.game_round.round_index_1.action_ticks = [['reset', 0],
    ['hu_self', 0, 8, ['自摸'], [48, -16, -16, -16]], ['end']];
  const result = await analyzeRecords([row, self], OPTIONS);
  assert.equal(pairOf(result).channels.MELD.events, 1);
  assert.equal(pairOf(result, '102', '103').special_events, 1);
  assert.equal(pairOf(result, '102', '103').channels.RON.events, 0);
  assert.equal(result.pairs.reduce((n, p) => n + p.channels.RON.events, 0), 0);
  assert.equal(result.pairs.reduce((n, p) => n + p.channels.ALL.events, 0), 1);
});

test('duplicate input is idempotent, conflicting same-game versions are quarantined, input is untouched', async () => {
  const row = simpleRecord('same');
  const original = JSON.stringify(row);
  const one = await analyzeRecords([row], OPTIONS);
  const repeated = await analyzeRecords([row, JSON.parse(original)], OPTIONS);
  assert.deepEqual(repeated.pairs, one.pairs);
  assert.equal(repeated.quality.duplicate_records, 1);
  assert.equal(JSON.stringify(row), original);
  const conflict = JSON.parse(original);
  conflict.record.game_title.p0_name = '不同版本';
  const isolated = await analyzeRecords([row, conflict], OPTIONS);
  assert.equal(isolated.pairs.length, 0);
  assert.equal(isolated.quality.conflicting_records, 1);
});

test('source-share denominator is restricted to shared games; both co-play directions remain distinct', async () => {
  const shared = simpleRecord('shared');
  const solo = simpleRecord('not-with-A');
  solo.record.game_title.p0_uid = 105;
  solo.record.game_title.p0_name = '模拟玩家105';
  const result = await analyzeRecords([shared, solo], OPTIONS);
  const pair = pairOf(result);
  assert.equal(pair.donor_coplay_rate.value, 1);
  assert.equal(pair.recipient_coplay_rate.value, 0.5);
  assert.equal(pair.channels.RON.source_share.denominator, 1);
  assert.equal(pair.channels.RON.source_share.value, 1);
  assert.equal(pair.score.recipient_games_without, 1);
  assert.equal(pair.score.recipient_difference, 0);
});

test('metadata conflicts, unknown timestamp zones and unfinished hands do not become zero-risk samples', async () => {
  const timezone = simpleRecord('zone');
  timezone.created_at = '2026-11-10 12:00:00';
  const metadata = { ...simpleRecord('conflict'), metadata_conflict: true };
  const unfinished = simpleRecord('unfinished');
  unfinished.record.game_round.round_index_1.action_ticks.pop();
  const result = await analyzeRecords([timezone, metadata, unfinished], OPTIONS);
  assert.equal(result.quality.missing_timestamp_records, 1);
  assert.equal(result.quality.invalid_records, 1);
  assert.equal(result.pairs.length, 0);
  assert.ok(result.quality.issues.some((i) => i.code === 'unfinished_hand'));
});

test('a full physical wall replays and under-eight-fan structural waits are not legal RON opportunities', async () => {
  const row = physicalDrawRecord();
  const game = replayRecord(row);
  assert.equal(game.hands[0].reconstructed, true, JSON.stringify(game.issues));
  const decision = game.hands[0].decisions[0];
  const calculated = await runPython([decision]);
  const response = calculated.results[0].recipients.find((r) => r.recipient_id === '102');
  assert.ok(response.structural_waits.includes(46));
  assert.equal(response.helpful.RON.includes(46), false, 'open_cuohe must not count a structural 5-fan win');
  assert.equal(calculated.results[0].status, 'available');
  const result = await analyzeRecords([row], { ...OPTIONS, compute_opportunities: true });
  assert.equal(result.quality.reconstructed_hands, 1);
  assert.ok(pairOf(result).channels.RON.opportunity_creation_rate.denominator > 0);
  assert.ok(pairOf(result).channels.RON.choice_hit_rate.value === null || pairOf(result).channels.RON.choice_hit_rate.value <= 1);
});

test('qualifying seven-pairs opportunity and mixed-choice numerator share exactly the same support', async () => {
  const row = physicalDrawRecord({ id: 'seven-pairs', recipientHand: [11, 11, 12, 12, 21, 21, 22, 22, 31, 31, 32, 32, 33], firstDiscard: 33 });
  const game = replayRecord(row);
  const first = game.hands[0].decisions[0];
  const calculated = await runPython([first]);
  const response = calculated.results[0].recipients.find((r) => r.recipient_id === '102');
  assert.ok(response.helpful.RON.includes(33));
  assert.ok(response.fan_values['33'].qualifying_fan >= 24);
  const calculation = { results: new Map([[first.id, calculated.results[0]]]) };
  game.time_ms = Date.parse(row.created_at);
  verifyOpportunities([game], calculation);
  const window = buildWindow([game], calculation, settingsFrom(OPTIONS), 30);
  const pair = pairOf(window);
  assert.equal(pair.channels.RON.opportunity_creation_rate.denominator, 1, 'only evaluated decisions belong in the denominator');
  assert.equal(pair.channels.RON.choice_hit_rate.numerator, 1);
  assert.equal(pair.channels.RON.choice_hit_rate.denominator, 1);
  first.candidates = [33];
  const forced = buildWindow([game], calculation, settingsFrom(OPTIONS), 30);
  assert.equal(pairOf(forced).channels.RON.choice_hit_rate.numerator, 0);
  assert.equal(pairOf(forced).channels.RON.choice_hit_rate.denominator, 0);
  assert.equal(pairOf(forced).channels.RON.choice_hit_rate.value, null);
});

test('missing Python preserves legal MELD analysis but never substitutes structural wins for qualified RON', async () => {
  const decision = replayRecord(physicalDrawRecord()).hands[0].decisions[0];
  const result = await evaluateDecisions([decision], { python_binary: '/nonexistent-anti-cheat-python' });
  const response = result.results.get(decision.id).recipients[0];
  assert.ok(Array.isArray(response.helpful.MELD));
  assert.equal(response.helpful.RON, null);
  assert.equal(response.helpful.ALL, null);
  assert.ok(result.errors.length > 0);
});

test('corrupted wall suppresses all opportunity denominators for that hand', async () => {
  const row = physicalDrawRecord();
  row.record.game_round.round_index_1.action_ticks[2][1] = 47;
  const result = await analyzeRecords([row], { ...OPTIONS, compute_opportunities: true });
  assert.equal(result.quality.reconstructed_hands, 0);
  assert.equal(pairOf(result).channels.RON.opportunity_creation_rate.value, null);
  assert.ok(result.quality.issues.some((i) => i.code === 'wall_draw_mismatch'));
});

test('separate settings flags and progress reporting work without mutating original JSON', async () => {
  const rows = concentrationFixture();
  const progress = [];
  const result = await analyzeRecords(rows, { ...OPTIONS, channels: { RON: { enabled: false } }, onProgress: (p) => progress.push(p) });
  assert.equal(pairOf(result).flag_ron, false);
  assert.ok(progress.length > 0);
  assert.equal(progress.at(-1).processed, rows.length);
  assert.ok(result.pairs.every((p) => p.evidence.length <= 50));
});

test('ALL opportunity union is deduplicated when the same discarded tile can both chow and pung', async () => {
  const game = replayRecord(physicalDrawRecord({ recipientHand: [11, 11, 12, 13, 23, 24, 25, 31, 32, 33, 41, 42, 43], firstDiscard: 11 }));
  const d = game.hands[0].decisions[0];
  const calc = await runPython([d]);
  const response = calc.results[0].recipients.find((r) => r.recipient_id === '102');
  assert.ok(response.helpful.CHI.includes(11));
  assert.ok(response.helpful.PENG.includes(11));
  assert.equal(response.helpful.ALL.filter((t) => t === 11).length, 1);
  game.time_ms = Date.parse('2026-11-10T12:00:00Z');
  const calculation = { results: new Map([[d.id, calc.results[0]]]) };
  verifyOpportunities([game], calculation);
  const pair = pairOf(buildWindow([game], calculation, settingsFrom(OPTIONS), 30));
  assert.equal(pair.channels.ALL.opportunity_creation_rate.numerator, 1);
  assert.equal(pair.channels.ALL.opportunity_creation_rate.denominator, 1);
});

test('flower points never turn a below-threshold structural win into a qualifying RON opportunity', async () => {
  const d = replayRecord(physicalDrawRecord()).hands[0].decisions[0];
  d.flowers[1] = 4;
  const calc = await runPython([d]);
  assert.ok(calc.results[0].recipients.find((r) => r.recipient_id === '102').structural_waits.includes(46));
  assert.ok(!calc.results[0].recipients.find((r) => r.recipient_id === '102').helpful.RON.includes(46));
});

test('a fully reconstructed actual win joins opportunity, exact fan, realized action and actual payment', async () => {
  const result = await analyzeRecords([physicalWinningRecord()], { ...OPTIONS, compute_opportunities: true });
  const pair = pairOf(result);
  assert.equal(result.quality.reconstructed_hands, 1);
  assert.equal(result.quality.opportunity_verified_hands, 1);
  assert.equal(pair.channels.RON.events, 1);
  assert.deepEqual(pair.channels.RON.opportunity_realization_rate, { numerator: 1, denominator: 1, value: 1 });
  assert.equal(pair.evidence[0].calculated_fan, 48);
  assert.equal(pair.score.direct_paid_to_recipient, 56);
  assert.equal(pair.score.recipient_ron_gain, 72);
  assert.equal(pair.score.others_paid_on_ron, 16);
});

test('explicit source timezone admits a local timestamp without relying on the host timezone', async () => {
  const row = simpleRecord('source-zone');
  row.created_at = '2026-11-10 12:00:00';
  row.created_at_timezone = 'Asia/Shanghai';
  const result = await analyzeRecords([row], OPTIONS);
  assert.equal(result.games[0].created_at, '2026-11-10T04:00:00.000Z');
});

test('unimplemented variants expose unavailable delivery counts rather than pretending zero cheating events', async () => {
  const row = simpleRecord('variant');
  row.record.game_title.sub_rule = 'guobiao/xiaolin';
  const result = await analyzeRecords([row], OPTIONS);
  assert.equal(pairOf(result).shared_games, 1);
  assert.equal(pairOf(result).channels.RON.events, null);
  assert.equal(pairOf(result).channels.RON.source_share.value, null);
  assert.equal(pairOf(result).channels.RON.status, 'unavailable');
  assert.equal(pairOf(result).score.direct_paid_to_recipient, null);
});

test('a metadata-conflicted identity is quarantined even when another copy of that game looks valid', async () => {
  const row = simpleRecord('metadata-copies');
  const result = await analyzeRecords([row, { ...row, metadata_conflict: true }], OPTIONS);
  assert.equal(result.summary.unique_games, 0);
  assert.equal(result.pairs.length, 0);
});

test('end without a terminal result is missing data, not a verified zero-score hand', async () => {
  const row = simpleRecord('missing-result');
  row.record.game_round.round_index_1.action_ticks = [['reset', 0], ['c', 17, 'F'], ['end']];
  const result = await analyzeRecords([row], OPTIONS);
  assert.equal(result.games[0].scores_available, false);
  assert.equal(pairOf(result).channels.RON.events, null);
  assert.ok(result.quality.issues.some((i) => i.code === 'missing_terminal_result'));
});
