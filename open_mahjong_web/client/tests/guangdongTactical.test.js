import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'
import { GUANGDONG_MIL_SUB_RULE } from '../src/utils/guangdongRoomConfig.js'
const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange } = require('../../server/utils/eventRoomSettings.js')
function save(payload) {
  const doc = readRoomSettings({})
  return applyRoomSettingsChange(doc, { kind: 'manual', userId: 101, eventStatus: 'active', body: { revision: doc.revision, ...payload } }).settings.manual
}
test('花鬼新房默认开启战鸣，手动关闭在保存和读回后保留', () => {
  const initial = createEventRoomForm({ room_rule: 'guangdong', room_config: { sub_rule: GUANGDONG_MIL_SUB_RULE } })
  assert.equal(initial.tactical_call, true)
  for (const tactical_call of [true, false]) for (const require_minimum_score of [true, false])
    for (const [round_timer,step_timer] of [[20,5], [31,8], [0,8], [0,0]]) for (let bits=0; bits<8; bits++) {
      const form = createEventRoomForm({ room_rule: 'guangdong', room_config: { sub_rule: GUANGDONG_MIL_SUB_RULE, tactical_call } })
      Object.assign(form, { require_minimum_score, round_timer, step_timer, tips:!!(bits&1), tourist_limit:!!(bits&2), allow_spectator:!!(bits&4) })
      const payload = buildEventRoomSettings(form)
      assert.equal(payload.room_config.tactical_call, tactical_call)
      const restored = createEventRoomForm(save(payload))
      assert.deepEqual(buildEventRoomSettings(restored).room_config, payload.room_config)
      const row = eventRoomSettingsRows(payload).find(row => row.label === '战术鸣牌')
      assert.match(row.value, tactical_call ? /开启.*5秒/ : /关闭/)
    }
})
test('花鬼战鸣禁止字符串/数字等布尔强制转换', () => {
  for (const bad of [null, 0, 1, 'true', 'false', [], {}]) {
    assert.throws(() => save({ room_rule:'guangdong', room_config:{ sub_rule:GUANGDONG_MIL_SUB_RULE, tactical_call:bad } }))
    assert.throws(() => buildEventRoomSettings({ ...createEventRoomForm({ room_rule:'guangdong', room_config:{sub_rule:GUANGDONG_MIL_SUB_RULE} }), tactical_call:bad }))
  }
})
