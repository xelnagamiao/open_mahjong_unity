import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows, eventRoomSettingsSummary } from '../src/utils/eventRoomSettings.js'
import { GUANGDONG_MIL_SUB_RULE, TUIDAO_SUB_RULE, guangdongDetail, guangdongProfiles } from '../src/utils/guangdongRoomConfig.js'
const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange } = require('../../server/utils/eventRoomSettings.js')
const save = payload => {
  const doc = readRoomSettings({})
  return applyRoomSettingsChange(doc, { kind: 'manual', userId: 101, eventStatus: 'active', body: { revision: doc.revision, ...payload } }).settings.manual
}

test('广东子规则、起和开关及通用设置通过前端-服务端-读回链', () => {
  for (const sub_rule of [TUIDAO_SUB_RULE, GUANGDONG_MIL_SUB_RULE]) for (const require_minimum_score of [false, true])
    for (const game_round of [1, 2, 3, 4]) for (let bits = 0; bits < 8; bits++) {
      const form = createEventRoomForm({ room_rule: 'guangdong', room_config: { sub_rule } })
      Object.assign(form, { require_minimum_score, game_round, tips: !!(bits & 1), tourist_limit: !!(bits & 2), allow_spectator: !!(bits & 4) })
      const payload = buildEventRoomSettings(form)
      const restored = createEventRoomForm(save(payload))
      assert.deepEqual(buildEventRoomSettings(restored).room_config, payload.room_config)
      assert.equal(restored.require_minimum_score, sub_rule === GUANGDONG_MIL_SUB_RULE ? require_minimum_score : true)
    }
})

test('旧广东默认仍为无癞推倒和，MIL2023默认启用4分门槛', () => {
  assert.equal(createEventRoomForm({ room_rule: 'guangdong' }).sub_rule, TUIDAO_SUB_RULE)
  const form = createEventRoomForm({ room_rule: 'guangdong', room_config: { sub_rule: GUANGDONG_MIL_SUB_RULE } })
  assert.equal(form.require_minimum_score, true)
  assert.equal(guangdongDetail(form).horse_count, 4)
  const payload = buildEventRoomSettings(form)
  assert.match(eventRoomSettingsSummary(payload), /MIL 2023，花鬼/)
  assert.equal(eventRoomSettingsRows(payload).find(row => row.label === '起和条件').value, '至少2番，同时至少4分')
  form.require_minimum_score = false
  assert.equal(eventRoomSettingsRows(buildEventRoomSettings(form)).find(row => row.label === '起和条件').value, '至少2番')
  assert.equal(guangdongProfiles.length, 2)
})

test('拒绝强制类型转换、跨版本配置和未提供的馆规', () => {
  for (const detail of [null, [], { require_minimum_score: 'false' }, { require_minimum_score: 0 }, { minimum_fan: 1 }, { minimum_score: 8 }, { horse_count: 8 }, { wildcards: false }, { edition: 'mil-tuidao-2024-om1' }, { fan_cap: 32 }])
    assert.throws(() => save({ room_rule: 'guangdong', room_config: { sub_rule: GUANGDONG_MIL_SUB_RULE, detailed_config: detail } }))
  assert.throws(() => save({ room_rule: 'guangdong', room_config: { sub_rule: TUIDAO_SUB_RULE, detailed_config: { require_minimum_score: false } } }))
  assert.throws(() => guangdongDetail({ sub_rule: 'guangdong/invalid' }))
  assert.throws(() => guangdongDetail({ sub_rule: GUANGDONG_MIL_SUB_RULE, require_minimum_score: 'true' }))
})

test('平鸡自摸禁吃默认关闭，开关经建房与预设存储往返保留', () => {
  const legacy = createEventRoomForm({ room_rule: 'guangdong' })
  assert.equal(legacy.pingji_zimo_no_chow, false)
  for (const enabled of [false, true]) {
    legacy.pingji_zimo_no_chow = enabled
    const payload = buildEventRoomSettings(legacy)
    assert.equal(payload.room_config.detailed_config.pingji_zimo_no_chow, enabled)
    const restored = createEventRoomForm(save(payload))
    assert.equal(restored.pingji_zimo_no_chow, enabled)
    assert.deepEqual(buildEventRoomSettings(restored).room_config, payload.room_config)
    const row = eventRoomSettingsRows(payload).find(row => row.label === '平鸡自摸禁吃')
    assert.equal(row.value, enabled ? '开启（不能吃，鸡胡只能自摸）' : '关闭')
  }
  legacy.sub_rule = GUANGDONG_MIL_SUB_RULE
  assert.equal('pingji_zimo_no_chow' in guangdongDetail(legacy), false)
})

test('平鸡自摸禁吃拒绝非布尔值和花鬼跨版本覆盖', () => {
  for (const value of ['false', 0, 1, null, [], {}]) {
    assert.throws(() => save({ room_rule: 'guangdong', room_config: {
      sub_rule: TUIDAO_SUB_RULE, detailed_config: { pingji_zimo_no_chow: value },
    } }))
  }
  assert.throws(() => guangdongDetail({ sub_rule: TUIDAO_SUB_RULE, pingji_zimo_no_chow: 'false' }))
  assert.throws(() => save({ room_rule: 'guangdong', room_config: {
    sub_rule: GUANGDONG_MIL_SUB_RULE, detailed_config: { pingji_zimo_no_chow: true },
  } }))
})
