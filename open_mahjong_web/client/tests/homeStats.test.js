import test from 'node:test';
import assert from 'node:assert/strict';
import { HOME_RULE_DEFS, homeRuleOptions, homeSceneOptions, homeDefaultScene, selectHomeStats, buildHomeStatsRows } from '../src/utils/homeStats.js';

test('shortcut rules end with Changsha and keep other variants in the complete rule selector', () => {
  assert.deepEqual(HOME_RULE_DEFS.map(rule => rule.label), ['国标','立直','青雀','古典','川麻(血战)','长沙']);
  const options = homeRuleOptions([{ rule: 'custom_future', rule_label: '后续规则' }]);
  assert.ok(options.some(rule => rule.value === 'hongque'));
  assert.ok(options.some(rule => rule.value === 'yixing'));
  assert.ok(options.some(rule => rule.label === '后续规则'));
  assert.equal(options.some(rule => rule.value === 'jiandan' || rule.label.includes('简单')), false);
  assert.equal(options.at(-1).label, '南雀');
  assert.equal(options.find(rule => rule.value === 'guangdong').label, '广东麻将');
  assert.equal(options.find(rule => rule.value === 'guangdong_tuidao').label, '广东麻将（推倒和）');
  assert.equal(options.find(rule => rule.value === 'sichuan_xueliu_exchange').label, '川麻(血流换三)');
  for (const [rule, label] of Object.entries({ riichi_sanma: '立直(三人)', guobiao_sanma: '国标(三人)',
    classical: '古典麻将', hongzhong: '红中麻将', free: '自由模式' })) {
    assert.equal(options.find(option => option.value === rule).label, label);
  }
  assert.equal(new Set(options.map(rule => rule.value)).size, options.length);
});

test('ranked scenes reflect grade and Elo queues', () => {
  for (const rule of ['riichi','riichi_sanma']) {
    assert.deepEqual(homeSceneOptions(rule).map(scene => scene.value), ['beginner','intermediate','advanced','custom','events']);
    assert.equal(homeDefaultScene(rule), 'beginner');
  }
  for (const rule of ['sichuan','sichuan_xueliu','sichuan_xueliu_exchange','qingque']) {
    assert.deepEqual(homeSceneOptions(rule).map(scene => scene.value), ['rank','custom','events']);
    assert.equal(homeDefaultScene(rule), 'rank');
  }
  assert.equal(homeDefaultScene('guobiao'), 'beginner');
  assert.equal(homeDefaultScene('classical'), 'custom');
});

test('legacy Nanque metadata cannot recreate a Simple Mahjong option', () => {
  const rows = [{ rule: 'jiandan', sub_rule: 'jiandan/standard', room_type: 'custom', source: 'record_metrics', total_games: 2 },
    { rule: 'zhongyong', sub_rule: 'zhongyong/nanque', room_type: 'custom', source: 'record_metrics', total_games: 3 }];
  assert.equal(homeRuleOptions(rows).filter(rule => rule.label === '南雀').length, 1);
  assert.equal(homeRuleOptions(rows).some(rule => rule.value === 'jiandan'), false);
  assert.equal(selectHomeStats(rows, 'nanque', 'custom').total_games, 5);
  assert.equal(selectHomeStats(rows, 'zhongyong', 'custom').total_games, 0);
});

test('Shanghai variants use separate samples and retain the historical Qiaoma default', () => {
  const rows = [
    { rule: 'shanghai', sub_rule: 'shanghai/qiaoma', room_type: 'custom', source: 'record_metrics', total_games: 2, win_count: 3 },
    { rule: 'shanghai', sub_rule: 'shanghai/qinghunpeng', room_type: 'custom', source: 'record_metrics', total_games: 1, win_count: 2 },
    { rule: 'shanghai_qinghunpeng', room_type: 'events', source: 'record_metrics', total_games: 4, win_count: 5 },
    { rule: 'shanghai', sub_rule: null, room_type: 'custom', source: 'record_metrics', total_games: 1, win_count: 1 },
  ];
  const qiaoma = selectHomeStats(rows,'shanghai','custom');
  const qinghunpeng = selectHomeStats(rows,'shanghai_qinghunpeng','custom');
  assert.equal(qiaoma.total_games,3);
  assert.equal(qiaoma.win_count,4);
  assert.equal(qinghunpeng.total_games,1);
  assert.equal(qinghunpeng.win_count,2);
  assert.equal(selectHomeStats(rows,'shanghai_qinghunpeng','events').total_games,4);
});

test('riichi keeps original basic labels, points in fourth position and numeric results', () => {
  const rows = buildHomeStatsRows({ rule: 'riichi', total_games: 1, player_game_count: 4,
    total_rounds: 12, win_count: 2, self_draw_count: 1, total_win_turn: 10,
    total_round_score: 1200, details_available: false,
    riichi_details: { net_score_count: 2, total_win_points: 6400, riichi_round_count: 3 } });
  const values = Object.fromEntries(rows.map(row => [row.label,row.value]));
  assert.equal(rows[3].label, '局均点');
  assert.equal(rows[3].value, '300.00');
  assert.equal(values['总回合'], '12');
  assert.equal(values['自摸率'], '50.00%');
  assert.equal(values['平均和了打点'], '3200.00');
  assert.equal(values['平均和了巡目'], '5.00');
  assert.equal(values['立直率'], '25.00%');
  assert.equal(rows.some(row => row.value === '—'), false);
});

test('Guobiao keeps table-based rates and selects tier metrics once', () => {
  const rows = [
    { rule: 'guobiao', room_type: 'match', match_tier: 'beginner', source: 'history', total_games: 99, total_rounds: 99 },
    { rule: 'guobiao', room_type: 'match', match_tier: 'beginner', source: 'metrics', total_games: 1, total_rounds: 4, win_count: 3, fulu_round_count: 4 },
  ];
  const stats = selectHomeStats(rows, 'guobiao', 'beginner');
  const values = Object.fromEntries(buildHomeStatsRows(stats).map(row => [row.label,row.value]));
  assert.equal(stats.total_games, 1);
  assert.equal(values['和牌率'], '75.00%');
  assert.equal(values['副露率'], '25.00%');
});

test('Sichuan multi-win and deal-in events display per-round counts', () => {
  for (const rule of ['sichuan', 'sichuan_xueliu', 'sichuan_xueliu_exchange']) {
    const values = Object.fromEntries(buildHomeStatsRows({ rule, details_available: true,
      total_games: 1, total_rounds: 4, win_count: 12, deal_in_count: 8 }).map(row => [row.label, row.value]));
    assert.equal(values['局均和牌次数'], '3.00');
    assert.equal(values['局均放铳次数'], '2.00');
    assert.equal(values['和牌率'], undefined);
    assert.equal(values['放铳率'], undefined);
  }
});
