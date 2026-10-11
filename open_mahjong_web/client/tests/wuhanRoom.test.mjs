import test from 'node:test'
import assert from 'node:assert/strict'
import { createEventRoomForm, buildEventRoomSettings } from '../src/utils/eventRoomSettings.js'
import { buildWuhanRoomPayload } from '../src/utils/wuhanRoomConfig.js'
import { milRulebook } from '../src/constants/milRulebooks.js'

test('Wuhan MIL room roundtrip preserves all hint switches', () => {
  const form = createEventRoomForm({ room_rule: 'wuhan', room_config: { count_tips: true, pointer_tips: false } })
  const room = buildEventRoomSettings(form)
  assert.equal(room.room_rule, 'wuhan')
  assert.equal(room.room_config.sub_rule, 'wuhan/mil2025')
  assert.deepEqual(room.room_config.detailed_config, { rule_version: 'mil2025' })
  assert.equal(room.room_config.count_tips, true)
  assert.equal(room.room_config.pointer_tips, false)
  assert.equal(room.room_config.use_flowers, false)
})
test('Wuhan rejects unsupported profiles and invalid room values', () => {
  const form = createEventRoomForm({ room_rule: 'wuhan' })
  for (const [key, value] of [['sub_rule', 'wuhan/standard'], ['tips', 'false'], ['game_round', 0], ['round_timer', -1], ['step_timer', 101], ['duplicate_key', 'key']]) {
    assert.throws(() => buildWuhanRoomPayload({ ...form, [key]: value }))
  }
})
test('Wuhan source document is the supplied MIL 2025 PDF', () => {
  const book = milRulebook('wuhan', 'wuhan/mil2025')
  assert.equal(book.subRule, 'wuhan/mil2025')
  assert.equal(book.filename, '武汉麻将（推广）竞赛规则（试行 2025 版）.pdf')
})
