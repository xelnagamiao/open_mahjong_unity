import test from 'node:test'
import assert from 'node:assert/strict'
import { STATISTICS_RULES, statisticsFanDictionary } from '../src/utils/statisticsCatalog.js'

test('legacy player info resolves the reported Guobiao fan keys to Chinese names', () => {
  const legacy = { guobiao: { huapai: '花牌', pinghe: '平和', sansesanbugao: '三色三步高' } }
  for (const key of ['guobiao', 'guobiao/standard']) {
    assert.deepEqual(statisticsFanDictionary(key, legacy), legacy.guobiao)
  }
})

test('every supported subrule accepts its declared legacy fan dictionary key', () => {
  for (const definition of STATISTICS_RULES.filter(row => row.fan_stats)) {
    const names = { pattern: definition.stats_name }
    const legacy = { [definition.fan_key]: names }
    for (const key of [definition.key, definition.sub_rule]) {
      assert.equal(statisticsFanDictionary(key, legacy), names, key)
    }
  }
})

test('an exact subrule dictionary takes priority without mixing other subrules', () => {
  const names = { riichi: '立直' }
  const dictionaries = {
    riichi: { riichi: '旧名称', legacy_only: '旧番种' },
    'riichi/standard': { standard_only: '标准规番种' },
    'riichi/sanma': names,
  }
  assert.equal(statisticsFanDictionary('riichi_sanma', dictionaries), names)
  assert.equal(statisticsFanDictionary('riichi/sanma', dictionaries), names)
  assert.deepEqual(statisticsFanDictionary('riichi_sanma', { ...dictionaries, 'riichi/sanma': {} }), {})
})

test('missing dictionaries and rules without fan statistics remain unavailable', () => {
  for (const dictionaries of [undefined, null, {}]) {
    assert.equal(statisticsFanDictionary('guobiao', dictionaries), null)
  }
  assert.equal(statisticsFanDictionary('unknown', { guobiao: { huapai: '花牌' } }), null)
  for (const definition of STATISTICS_RULES.filter(row => !row.fan_stats)) {
    assert.equal(statisticsFanDictionary(definition.key, { [definition.sub_rule]: { pattern: '番种' } }), null)
  }
})
