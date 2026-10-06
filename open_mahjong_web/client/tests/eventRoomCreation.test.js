import test from 'node:test'
import assert from 'node:assert/strict'
import { buildEventRoomCreation, createEventRoomCreationForm, eventRoomCreationError } from '../src/utils/eventRoomCreation.js'
import { eventRoomSettingsSummary, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'

test('batch creation keeps transient quantity and key-generation options out of saved presets', () => {
  const preset = { room_rule: 'guobiao', room_config: { game_round: 2, use_flowers: false } }
  const original = structuredClone(preset)
  const form = createEventRoomCreationForm(preset)
  assert.equal(form.room_count, 1)
  assert.equal(form.auto_duplicate, false)
  assert.equal(form.duplicate_enabled, false)
  form.room_count = 20
  form.duplicate_enabled = true
  form.auto_duplicate = true
  form.duplicate_round_count = 8
  const payload = buildEventRoomCreation(form)
  assert.equal(payload.room_count, 20)
  assert.equal(payload.auto_duplicate, true)
  assert.equal(payload.duplicate_enabled, true)
  assert.equal(payload.duplicate_round_count, 8)
  assert.equal(payload.room_config.use_flowers, false)
  assert.equal('room_count' in payload.room_config, false)
  assert.equal('auto_duplicate' in payload.room_config, false)
  assert.equal('duplicate_enabled' in payload.room_config, false)
  assert.deepEqual(preset, original)
  assert.equal(createEventRoomCreationForm(preset).room_count, 1)
});

test('manual keys remain reusable and invalid batch settings are rejected before submitting', () => {
  const form = createEventRoomCreationForm()
  form.room_count = 3
  form.duplicate_enabled = true
  form.duplicate_key = 'dup_existing'
  assert.equal(buildEventRoomCreation(form).room_config.duplicate_key, 'dup_existing')
  form.auto_duplicate = true
  assert.throws(() => buildEventRoomCreation(form), /已有密钥/)
  form.duplicate_key = ''
  form.room_rule = 'riichi'
  assert.throws(() => buildEventRoomCreation(form), /仅支持国标/)
  form.room_rule = 'guobiao'
  form.duplicate_round_count = 2
  assert.throws(() => buildEventRoomCreation(form), /复式局数/)
  form.auto_duplicate = false
  form.room_count = 2
  assert.throws(() => buildEventRoomCreation(form), /房间数量/)
});

test('automatic keys require duplicate mode, while manual presets restore their existing key', () => {
  const form = createEventRoomCreationForm()
  form.auto_duplicate = true
  assert.throws(() => buildEventRoomCreation(form), /先开启复式/)
  form.auto_duplicate = false
  form.duplicate_enabled = true
  assert.throws(() => buildEventRoomCreation(form), /请输入复式密钥/)
  const restored = createEventRoomCreationForm({ room_rule: 'guobiao', room_config: { duplicate_key: 'existing_key' } })
  assert.equal(restored.duplicate_enabled, true)
  assert.equal(restored.auto_duplicate, false)
  assert.equal(buildEventRoomCreation(restored).room_config.duplicate_key, 'existing_key')
  restored.duplicate_enabled = false
  assert.throws(() => buildEventRoomCreation(restored), /先开启复式/)
});

test('East-West rooms preserve three rounds in requests and display a readable name', () => {
  const form = createEventRoomCreationForm()
  form.game_round = 3
  form.room_count = 5
  const payload = buildEventRoomCreation(form)
  assert.equal(payload.room_config.game_round, 3)
  assert.equal(payload.duplicate_enabled, false)
  assert.match(eventRoomSettingsSummary(payload), /东西战/)
  assert.equal(eventRoomSettingsRows(payload).find(row => row.label === '圈数').value, '东西战')
  form.duplicate_enabled = true
  form.auto_duplicate = true
  form.duplicate_round_count = 12
  assert.equal(buildEventRoomCreation(form).duplicate_round_count, 12)
});

test('partial failures describe both created rooms and keys that can be reused', () => {
  const error = { response: { data: { message: '游戏服拒绝创建', data: { created_count: 1, requested_count: 5, generated_key_count: 5 } } } }
  assert.match(eventRoomCreationError(error), /1 \/ 5 个房间/)
  assert.match(eventRoomCreationError(error), /5 个密钥.*复用/)
  assert.equal(eventRoomCreationError({ message: '额度不足' }), '额度不足')
});
