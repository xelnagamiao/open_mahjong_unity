import test from 'node:test';
import assert from 'node:assert/strict';
import { STATISTICS_RULES } from '../src/utils/statisticsCatalog.js';
import { homeSceneOptions } from '../src/utils/homeStats.js';
import { buildSceneDailyChartOption, buildSceneDailyTable } from '../src/utils/statsDisplay.js';
import {
  STATISTICS_SCENE_LABELS,
  platformSceneOptions,
  platformSceneSelection,
  platformRecordsScope,
} from '../src/utils/statisticsScenes.js';

test('platform scenes match the homepage and only expose supported queues for every subrule', () => {
  for (const rule of STATISTICS_RULES) {
    const options = platformSceneOptions(rule.key);
    const expected = homeSceneOptions(rule.key).filter(option => option.value !== 'events')
      .map(option => option.value === 'rank' ? 'elo' : option.value);
    assert.deepEqual(options.map(option => option.value), expected, rule.key);
    assert.equal(options.length > 1, rule.matchmaking, rule.key);
    assert.equal(options.at(-1).label, '自定义', rule.key);
  }
  assert.deepEqual(platformSceneOptions('guobiao').map(option => option.value),
    ['beginner', 'intermediate', 'advanced', 'mcrpl', 'custom']);
  for (const rule of ['riichi', 'riichi_sanma']) {
    assert.deepEqual(platformSceneOptions(rule).map(option => option.value),
      ['beginner', 'intermediate', 'advanced', 'custom']);
  }
  for (const rule of ['qingque', 'sichuan', 'sichuan_xueliu_exchange', 'hongque']) {
    assert.deepEqual(platformSceneOptions(rule).map(option => option.value), ['elo', 'custom']);
  }
  for (const rule of ['classical', 'guobiao_sanma', 'riichi_washizu', 'sichuan_xueliu', 'hongque_w']) {
    assert.deepEqual(platformSceneOptions(rule), [{ value: 'custom', label: '自定义' }]);
  }
});

test('rule changes replace a hidden selected scene while retaining valid custom and total choices', () => {
  assert.equal(platformSceneSelection('riichi', 'mcrpl'), 'beginner');
  assert.equal(platformSceneSelection('sichuan', 'beginner'), 'elo');
  assert.equal(platformSceneSelection('classical', 'elo'), 'custom');
  assert.equal(platformSceneSelection('guobiao', 'custom'), 'custom');
  assert.equal(platformSceneSelection('classical', 'total'), 'total');
});

test('custom records filter by room type and totals include custom without swallowing event filters', () => {
  assert.deepEqual(platformRecordsScope('custom'), { room_type: 'custom' });
  assert.deepEqual(platformRecordsScope('total'), { room_type: 'all' });
  assert.deepEqual(platformRecordsScope('elo'), { match_tier: 'elo' });
  assert.deepEqual(platformRecordsScope('beginner'), { match_tier: 'beginner' });
  assert.deepEqual(platformRecordsScope('custom', 'all'), { event_id: 'all' });
  assert.deepEqual(platformRecordsScope('custom', 'evt_test'), { event_id: 'evt_test' });
});

test('daily chart and table include custom and omit unavailable tier series', () => {
  const rows = [{ stat_date: '2026-10-28', match_tier: 'custom', total_games: 3 }];
  const tierOptions = platformSceneOptions('classical');
  const chart = buildSceneDailyChartOption(rows, { tierOptions, tierLabel: STATISTICS_SCENE_LABELS });
  assert.deepEqual(chart.legend.data, ['自定义']);
  assert.deepEqual(chart.series.map(series => series.data), [[3]]);
  assert.deepEqual(buildSceneDailyTable(rows, tierOptions, STATISTICS_SCENE_LABELS),
    [{ stat_date: '2026-10-28', custom: 3 }]);
});
