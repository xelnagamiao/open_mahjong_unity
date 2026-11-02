import test from 'node:test'
import assert from 'node:assert/strict'
import dictionaries from '../../shared/statisticsFans.json' with { type: 'json' }
import { statisticsSelection, statisticsFanDictionary, statisticsGameType, statisticsModeLabel, statisticRuleKey } from '../src/utils/statisticsCatalog.js'
import { homeRuleOptions, homeModeOptions, homeSceneOptions, selectHomeStats } from '../src/utils/homeStats.js'
import { buildAllFanEntries } from '../src/utils/statsDisplay.js'

test('W is a Hongque subrule with independent basic and complete fan statistics', () => {
  const w = statisticsSelection('hongque_w')
  assert.equal(w, statisticsSelection('hongque/w-v1.0'))
  assert.equal(w.rule, 'hongque')
  assert.equal(w.basic_stats, true)
  assert.equal(w.fan_stats, true)
  assert.equal(w.fan_key, 'hongque_w')
  assert.equal(w.matchmaking, false)
  assert.ok(homeRuleOptions().some(option => option.value === 'hongque_w'))
  assert.deepEqual(homeSceneOptions('hongque_w').map(scene => scene.value), ['custom', 'events'])
  const names = statisticsFanDictionary('hongque_w', dictionaries)
  assert.equal(Object.keys(names).length, 30)
  for (const name of ['对对和', '混一色', '清双色', '混双色', '双同刻', '四同顺', '双虹会']) assert.equal(names[name], name)
  for (const name of ['清顺', '清刻', '全半色', '全纯色', '同花']) assert.equal(names[name], undefined)
  const entries = buildAllFanEntries({ 对对和: 2, 双虹会: 1 }, names, 3)
  assert.equal(entries.find(row => row.label === '对对和').count, 2)
  assert.equal(entries.find(row => row.label === '双虹会').count, 1)
})

test('W hand-length filters and totals cannot mix Hongque 2 or mistake four hands for four winds', () => {
  const old = { rule: 'hongque', sub_rule: 'hongque/v1.6', room_type: 'custom', mode: '4/4', game_type: 'quanzhuang', total_games: 17 }
  const w = { rule: 'hongque', sub_rule: 'hongque/w-v1.0', room_type: 'custom', mode: '4/4', game_type: 'quanzhuang', total_games: 3 }
  const w16 = { ...w, mode: '16/4', total_games: 5 }
  assert.equal(statisticRuleKey(old), 'hongque')
  assert.equal(statisticRuleKey(w), 'hongque_w')
  assert.equal(selectHomeStats([old, w, w16], 'hongque_w', 'custom', 'hands4').total_games, 3)
  assert.equal(selectHomeStats([old, w, w16], 'hongque_w', 'custom', 'hands16').total_games, 5)
  assert.equal(selectHomeStats([old, w, w16], 'hongque', 'custom', 'hands4').total_games, 17)
  assert.deepEqual(homeModeOptions('hongque_w', 'custom').map(row => row.label), ['16局', '12局', '8局', '4局'])
  for (const n of [4, 8, 12, 16]) {
    assert.equal(statisticsGameType('hongque_w', `${n}/4`), `hands${n}`)
    assert.equal(statisticsModeLabel('hongque_w', `${n}/4`), `${n}局`)
  }
  assert.equal(statisticsGameType('hongque_w', '1/4'), null)
})
