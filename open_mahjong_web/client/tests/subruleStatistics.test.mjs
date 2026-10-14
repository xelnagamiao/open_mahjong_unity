import test from 'node:test';
import assert from 'node:assert/strict';
import { buildAllFanEntries } from '../src/utils/statsDisplay.js';
import { STATISTICS_RULES, statisticsSelection, statisticsGameType, statisticsModeLabel } from '../src/utils/statisticsCatalog.js';
import { homeSceneOptions, homeModeOptions, selectHomeStats } from '../src/utils/homeStats.js';

test('observed fan labels are retained when an older display dictionary lacks them', () => {
  assert.deepEqual(buildAllFanEntries({'清顺':3,'清刻':2},{},1,{}).map(r=>[r.label,r.count]), [['清顺',3],['清刻',2]]);
});

test('catalog exposes exactly the reviewed capabilities and all available modes', () => {
  assert(STATISTICS_RULES.length >= 43);
  assert.equal(STATISTICS_RULES.filter(r=>r.fan_stats).length, 24);
  assert.equal(statisticsSelection('guobiao_huahun').sub_rule, 'guobiao/huahun');
  assert.equal(statisticsSelection('guobiao_huahun').matchmaking, false);
  assert.equal(statisticsSelection('guobiao_four_jokers').sub_rule, 'guobiao/four_jokers');
  assert.equal(statisticsSelection('guobiao_four_jokers').matchmaking, false);
  assert.equal(STATISTICS_RULES.filter(r=>r.matchmaking).length, 7);
  for (const row of STATISTICS_RULES) {
    assert.equal(homeSceneOptions(row.key).length > 2, row.matchmaking, row.key);
    assert.equal(Boolean(row.fan_key), row.fan_stats, row.key);
    assert(row.custom_modes.length > 0);
    assert.equal(row.ranked_modes.length > 0, row.matchmaking);
  }
  assert.deepEqual(statisticsSelection('hongque').custom_modes, ['4/4','8/4','12/4','16/4']);
  assert.deepEqual(statisticsSelection('hongque').ranked_modes, ['16/4_rank']);
  assert.deepEqual(statisticsSelection('riichi_sanma').ranked_modes, ['1/3_rank','2/3_rank']);
  assert.deepEqual(statisticsSelection('changsha').custom_modes, ['1/4','2/4','4/4']);
  assert.equal(statisticsSelection('free'), undefined);
});

test('Hongque uses actual hands in labels and separates Elo from four-hand custom games', () => {
  assert.deepEqual(homeModeOptions('hongque','rank'), [{value:'hands16',label:'16局'}]);
  assert.deepEqual(homeModeOptions('hongque','custom').map(row => row.label), ['16局','12局','8局','4局']);
  for (const hands of [4,8,12,16]) {
    assert.equal(statisticsModeLabel('hongque',`${hands}/4`), `${hands}局`);
    assert.equal(statisticsGameType('hongque',`${hands}/4`), `hands${hands}`);
  }
  assert.equal(statisticsModeLabel('hongque','16/4_rank'), '16局');
  assert.equal(statisticsModeLabel('guobiao','4/4_rank'), '全庄战');
  const rows = [
    {rule:'hongque',room_type:'custom',mode:'4/4',game_type:'quanzhuang',total_games:4},
    {rule:'hongque',room_type:'custom',mode:'16/4',total_games:16},
    {rule:'hongque',room_type:'match',mode:'16/4_rank',game_type:'hands16',source:'record_metrics',total_games:2},
    {rule:'hongque',room_type:'match',mode:'16/4_rank',source:'record_metrics',total_games:3},
  ];
  assert.equal(selectHomeStats(rows,'hongque','custom','hands4').total_games,4);
  assert.equal(selectHomeStats(rows,'hongque','custom','hands16').total_games,16);
  assert.equal(selectHomeStats(rows,'hongque','rank','hands16').total_games,5);
  assert.equal(selectHomeStats(rows,'hongque','rank','hands4').total_games,0);
});

test('new regional rules expose basic statistics without fan statistics', () => {
  for (const [key, version] of [['nanchang', 2026], ['nanjing', 2024], ['shenyang', 2025], ['wuhan', 2025]]) {
    const row = statisticsSelection(key);
    assert(row, key);
    assert.equal(row.sub_rule, `${key}/mil${version}`);
    assert.equal(row.player_count, 4);
    assert.equal(row.basic_stats, true);
    assert.equal(row.fan_stats, false);
    assert.equal(Boolean(row.fan_key), false);
  }
});
