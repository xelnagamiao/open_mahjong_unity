import test from 'node:test'
import assert from 'node:assert/strict'
import { buildGuizhouRoomPayload, loadGuizhouForm, GUIZHOU_VERSION } from '../src/utils/guizhouRoomConfig.js'

const defaults = () => loadGuizhouForm({ room_rule: 'guizhou', room_name: ' 贵州测试 ', password: ' 123 ', duplicate_key: '' })

test('MIL profile emits its exact version and never inherits foreign house rules', () => {
  const form = { ...defaults(), hk_flowers: true, hepai_limit: 8, use_flowers: true, tactical_call: true }
  const payload = buildGuizhouRoomPayload(form)
  assert.equal(payload.room_rule, 'guizhou')
  assert.equal(payload.password, '123')
  assert.equal(payload.room_config.room_name, '贵州测试')
  assert.deepEqual(payload.room_config.detailed_config, { rule_version: GUIZHOU_VERSION })
  assert.equal(payload.room_config.use_flowers, false)
  assert.equal(payload.room_config.tactical_call, false)
  assert.ok(!('hepai_limit' in payload.room_config))
})

test('all selectable lengths and switch branches round-trip independently', () => {
  for (const game_round of [1, 2, 3, 4]) {
    for (const value of [false, true]) {
      const options = { game_round, tips: value, count_tips: value, pointer_tips: value, tourist_limit: value, allow_spectator: value }
      const form = loadGuizhouForm(defaults(), options)
      const config = buildGuizhouRoomPayload(form).room_config
      for (const [key, expected] of Object.entries(options)) assert.equal(config[key], expected)
      assert.deepEqual(buildGuizhouRoomPayload(loadGuizhouForm(defaults(), config)).room_config, config)
    }
  }
})

test('time limits and explicit zero survive serialization', () => {
  for (const round_timer of [0, 20, 1000]) for (const step_timer of [0, 5, 100]) {
    const config = buildGuizhouRoomPayload({ ...defaults(), round_timer, step_timer }).room_config
    assert.equal(config.round_timer, round_timer); assert.equal(config.step_timer, step_timer)
  }
})

test('reject invalid rules, duplicate walls, numbers and booleans', () => {
  for (const [key, values] of Object.entries({
    game_round: [0, 5, 1.5, '1', true, NaN], round_timer: [-1, 1001, 1.5, '20'], step_timer: [-1, 101, true],
    tips: ['false', 1], count_tips: ['true'], pointer_tips: [0], tourist_limit: ['true'], allow_spectator: ['false'],
    sub_rule: ['guobiao/standard', 'guizhou/joker'], duplicate_key: ['secret'],
  })) for (const value of values) assert.throws(() => buildGuizhouRoomPayload({ ...defaults(), [key]: value }))
})

test('blank room names stay optional; draft changes do not mutate stored settings', () => {
  const source = { game_round: 1, tips: false }
  const form = loadGuizhouForm({ room_name: ' ' }, source)
  form.tips = true
  assert.equal(source.tips, false)
  assert.ok(!('room_name' in buildGuizhouRoomPayload(form).room_config))
})
