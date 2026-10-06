import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { analyzeRiichiRecord, analyzeRiichiRecords } from '../src/utils/riichiRecordStats.js';
import { buildPlayerStatsRows, buildRiichiAdvancedStatsGroups } from '../src/utils/statsDisplay.js';

const cases = JSON.parse(readFileSync(new URL('../../../open_mahjong_server/server/database/riichi/stats_cases.json', import.meta.url)));
for (const fixture of cases) {
  test(fixture.name, () => {
    const s = analyzeRiichiRecord(fixture.record, fixture.index, fixture.score, fixture.rank);
    for (const [key, expected] of Object.entries(fixture.expected)) {
      if (typeof expected === 'object') {
        for (const [subkey, value] of Object.entries(expected)) assert.equal(s[key][subkey], value, `${key}.${subkey}`);
      } else assert.equal(s[key], expected, key);
    }
  });
}

test('basic rates stay numeric while advanced conditional rates keep their denominators', () => {
  const s = { ...analyzeRiichiRecord(cases[4].record, 0), details_available: true };
  const rows = Object.fromEntries(buildPlayerStatsRows(s).map(row => [row.label, row.value]));
  assert.equal(rows['立直率'], '50.00%');
  const advanced = Object.fromEntries(buildRiichiAdvancedStatsGroups(s).flatMap(group => group.rows).map(row => [row.label, row.value]));
  assert.equal(advanced['立直后和牌率'], '0.00%');
  assert.equal(rows['自摸率'], '100.00%');
  assert.equal(rows['平均和了打点'], '3000.00');
  const unavailable = Object.fromEntries(buildPlayerStatsRows({ ...s, riichi_details: null }).map(row => [row.label, row.value]));
  assert.equal(unavailable['立直率'], '0.00%');
  assert.equal(unavailable['和牌率'], rows['和牌率']);
});

test('basic profiles exclude conditional statistics; advanced groups keep samples and formulas', () => {
  const stats = analyzeRiichiRecord(cases[4].record, 0);
  const basic = buildPlayerStatsRows(stats);
  for (const label of ['立直后和牌率', '立直后放铳率', '副露后和牌率', '默听和牌占比', '平均立直巡目', '荒牌流局听牌率']) {
    assert.equal(basic.some(row => row.label === label), false, label);
  }
  assert.equal(basic.length, 16);
  const groups = buildRiichiAdvancedStatsGroups(stats);
  assert.equal(groups[0].sample, '1 次成立立直');
  assert.equal(groups[0].rows.find(row => row.label === '立直后和牌率').value, '0.00%');
  assert.match(groups[1].rows.find(row => row.label === '默听和牌占比').tip, /全部和牌次数/);
  assert.equal(groups[1].rows.find(row => row.label === '副露后和牌率').value, '—');
  const missing = buildRiichiAdvancedStatsGroups({ ...stats, riichi_details: null });
  assert.ok(missing.every(group => group.rows.every(row => row.value === '—')));
});

test('local rank accounts for unequal starting points and final saved points', () => {
  const record = structuredClone(cases[5].record);
  Object.assign(record.game_title, { p0_uid: 101, p1_uid: 102, p2_uid: 103, p3_uid: 104,
    riichi_points: { 0: 10000, 1: -2000, 2: 20000, 3: 25000 } });
  const stats = analyzeRiichiRecords([record], 102);
  assert.equal(stats.fourth_place_count, 1);
  assert.equal(stats.total_round_score, -17000);
});

test('average winning turn weights every win instead of averaging game means', () => {
  const recordWithTurns = (turns) => ({
    game_title: { rule: 'riichi', p0_uid: 101, p1_uid: 102, p2_uid: 103, p3_uid: 104 },
    game_round: Object.fromEntries(turns.map((turn, i) => {
      const ticks = [];
      for (let n = 1; n < turn; n += 1) {
        for (let seat = 0; seat < 4; seat += 1) ticks.push(['d', 11, seat], ['c', 11, 'F']);
      }
      ticks.push(['d', 12, 0], ['hu_riichi', 0, 'hu_self', 2, 30, ['门前清自摸和'], [6000, -2000, -2000, -2000]], ['end']);
      return [`round_index_${i + 1}`, { action_ticks: ticks }];
    })),
  });
  const stats = analyzeRiichiRecords([recordWithTurns([2]), recordWithTurns([8, 10])], 101);
  assert.equal(stats.win_count, 3);
  assert.equal(stats.total_win_turn, 20);
  assert.equal(buildPlayerStatsRows(stats).find(row => row.label === '平均和了巡目').value, '6.67');
});

test('local sanma aggregation ranks three players and keeps nuki in personal turn', () => {
  const record = structuredClone(cases.find(c => c.name.startsWith('sanma nuki replacement')).record);
  Object.assign(record.game_title, { p0_uid: 101, p1_uid: 102, p2_uid: 103 });
  const stats = analyzeRiichiRecords([record], 101);
  assert.equal(stats.total_rounds, 1);
  assert.equal(stats.win_count, 1);
  assert.equal(stats.total_win_turn, 1);
  assert.equal(stats.first_place_count, 1);
  assert.equal(stats.fourth_place_count, 0);
  assert.equal(analyzeRiichiRecord(record, 3), null);
  assert.equal(analyzeRiichiRecord(record, 0, null, 4).fourth_place_count, 0);
});
