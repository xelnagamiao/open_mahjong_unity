import assert from 'node:assert/strict';
import test from 'node:test';
import { analyzeRecords } from '../src/utils/recordAnalyzer.js';
import { buildPlayerStatsRows, buildPlatformStatsRows } from '../src/utils/statsDisplay.js';
import { STATISTICS_RULES, STATISTIC_VALUE_UNITS } from '../src/utils/statisticsCatalog.js';

function record(sub = 'sichuan/standard', names = ['清一色', '七对'], value = 8) {
  return { game_title: { rule: sub.split('/')[0], sub_rule: sub, p0_uid: 101, p1_uid: 102, p2_uid: 103, p3_uid: 104 },
    game_round: { round_index_1: { seats: [2, 3, 0, 1], action_ticks: [
      ['reset', 0], ['c', 11], ['hu_first', 3, value, names, [-8, 0, 0, 8], 11, 0, 0, 1], ['end'],
    ] } } };
}

test('SBR counts uncapped fan for both winner and payer, including rotated deferred settlements', () => {
  for (const sub of ['sichuan/standard', 'sichuan/doubles', 'sichuan/doubles_duplicate']) {
    for (const [names, base, expected] of [[['平和'], 1, 0], [['大对子'], 2, 1], [['清一色', '七对'], 8, 4], [['清一色', '杠*2', '根×2'], 8, 6]]) {
      const data = record(sub, names, base);
      data.game_round.round_index_1.action_ticks[2][4] = [0, 0, 0, 0];
      const winner = analyzeRecords([data], 102);
      const payer = analyzeRecords([data], 103);
      assert.equal(winner.total_fan_score, expected);
      assert.equal(payer.total_fangchong_score, expected);
      assert.equal(winner.win_count, 1);
      assert.equal(payer.deal_in_count, 1);
      assert.equal(winner.total_win_turn, 1);
    }
  }
});

test('saved fan wins over a capped score and unknown legacy fan fails visibly', () => {
  const data = record('sichuan/standard', ['new fan']);
  assert.throws(() => analyzeRecords([data], 102), /Unknown historical Sichuan fan/);
  data.game_round.round_index_1.win_fan_by_tick = { 2: 7 };
  assert.equal(analyzeRecords([data], 102).total_fan_score, 7);
});

test('blood-flow linear fan values do not use the SBR table', () => {
  for (const { sub_rule } of STATISTICS_RULES.filter(r => r.sub_rule.startsWith('sichuan/xueliu'))) {
    assert.equal(analyzeRecords([record(sub_rule, ['清碰', '暗杠'], 14)], 102).total_fan_score, 14);
  }
});

test('multi-ron charges the explicit payer for each winning hand, even before deferred payment', () => {
  const data = record();
  data.game_title.blood_battle = true;
  data.game_round.round_index_1.action_ticks[2][4] = [0, 0, 0, 0];
  data.game_round.round_index_1.action_ticks.splice(3, 0, ['hu_second', 1, 2, ['大对子'], [0, 0, 0, 0], 11, 1, 0, 1]);
  const payer = analyzeRecords([data], 103);
  assert.equal(payer.deal_in_count, 2);
  assert.equal(payer.total_fangchong_score, 5);
  assert.equal(analyzeRecords([data], 102).total_fan_score, 4);
  assert.equal(analyzeRecords([data], 104).total_fan_score, 1);
});

test('player and platform use the declared value units for every subrule and selection key', () => {
  for (const r of STATISTICS_RULES) {
    for (const key of [r.key, r.sub_rule]) {
      const s = { rule: key, win_count: 2, deal_in_count: 1, total_fan_score: 10, total_fangchong_score: 6 };
      for (const build of [buildPlayerStatsRows, buildPlatformStatsRows]) {
        // Riichi personal basic stats deliberately show actual points; fan is in advanced stats.
        if (build === buildPlayerStatsRows && r.rule === 'riichi') continue;
        const rows = build(s);
        const winLabel = STATISTIC_VALUE_UNITS[r.win_stat_unit].win_label;
        const lossLabel = STATISTIC_VALUE_UNITS[r.deal_in_stat_unit].deal_in_label;
        if (winLabel) assert.equal(rows.find(x => x.label === winLabel)?.value, '5.00', `${key} win`);
        else assert.ok(!rows.some(x => x.label === '平均和番'));
        if (lossLabel) assert.equal(rows.find(x => x.label === lossLabel)?.value, '6.00', `${key} loss`);
        else assert.ok(!rows.some(x => x.label === '平均铳番'));
        if (r.win_stat_unit !== 'fan') assert.ok(!rows.some(x => x.label === '平均和番'));
        if (r.deal_in_stat_unit !== 'fan') assert.ok(!rows.some(x => x.label === '平均铳番'));
      }
    }
  }
  assert.equal(buildPlatformStatsRows({ win_count: 1, total_fan_score: 8 }, { rule: 'shanghai' })
    .find(r => r.label === '平均和牌基本分').value, '8.00');
  assert.equal(buildPlayerStatsRows({ win_count: 1, total_fan_score: 8 }, { rule: 'shanghai', detailed: false })
    .find(r => r.label === '平均和牌基本分').value, '—');
});

test('local analysis preserves Qingque fan versus payment and rounds once per game', () => {
  const data = record('qingque/standard', ['门前清'], 3.60616);
  const rd = data.game_round.round_index_1;
  rd.action_ticks[2][0] = 'hu_third'; // Winner 3, payer 0.
  rd.action_ticks[2][4] = [-39, 0, 0, 39];
  data.game_round.round_index_2 = structuredClone(rd);
  const winner = analyzeRecords([data], 102);
  assert.equal(winner.total_fan_score, 7);
  const payer = analyzeRecords([data], 103);
  assert.equal(payer.total_fangchong_score, 78);
  assert.equal(buildPlayerStatsRows(payer).find(r => r.label === '平均放铳失分').value, '39.00');
});
