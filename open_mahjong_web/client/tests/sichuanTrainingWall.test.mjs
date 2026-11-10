import test from 'node:test'
import assert from 'node:assert/strict'
import { createEventRoomCreationForm, buildEventRoomCreation, sharedWallConfirmation } from '../src/utils/eventRoomCreation.js'
import { selectSichuanWallSource, selectSichuanKeySource, buildSichuanKeyRequest, generateTrainingSeed } from '../src/utils/sichuanWallSource.js'
import { createEventRoomForm, buildEventRoomSettings, changeEventRoomRule } from '../src/utils/eventRoomSettings.js'

const seed = 'abcdef0123456789'.repeat(4)
for (const sub_rule of ['sichuan/doubles_duplicate', 'sichuan/xueliu_doubles_duplicate']) {
  test(`${sub_rule}: training keeps duplicate rules but never submits a key or auto-generation`, () => {
    const form = createEventRoomCreationForm({ room_rule: 'sichuan', room_config: { sub_rule } })
    selectSichuanWallSource(form, 'training')
    form.random_seed = `0x${seed.toUpperCase()}`
    form.duplicate_key = 'dup_' + 'a'.repeat(32)
    form.auto_duplicate = true
    form.room_count = 3
    const body = buildEventRoomCreation(form)
    assert.equal(body.room_config.wall_source, 'training')
    assert.equal(body.room_config.random_seed, seed)
    assert.equal(body.room_config.sub_rule, sub_rule)
    assert.equal(body.room_config.allow_spectator, true)
    assert.equal(body.room_count, 3)
    assert.equal('duplicate_key' in body.room_config, false)
    assert.equal('auto_duplicate' in body, false)
    assert.equal('duplicate_enabled' in body, false)
    form.allow_spectator = false
    const restored = createEventRoomCreationForm(buildEventRoomCreation(form))
    assert.equal(restored.wall_source, 'training')
    assert.equal(restored.allow_spectator, false)
    assert.equal(buildEventRoomCreation(restored).room_config.random_seed, seed)
  })
  test(`${sub_rule}: explicit source changes reset spectator and enforce key requirements`, () => {
    const form = createEventRoomCreationForm({ room_rule: 'sichuan', room_config: { sub_rule } })
    for (const source of ['seed', 'manual', 'key']) {
      selectSichuanWallSource(form, 'training')
      form.random_seed = seed
      selectSichuanWallSource(form, source)
      assert.equal(form.allow_spectator, false)
      assert.equal(form.random_seed, seed, 'source selection keeps the training draft without submitting it')
      if (source !== 'key') assert.throws(() => buildEventRoomCreation(form), /请输入复式密钥/)
      form.duplicate_key_source = 'existing'
      form.duplicate_key = 'dup_' + 'a'.repeat(32)
      const body = buildEventRoomCreation(form)
      assert.equal(body.room_config.wall_source, source)
      assert.equal(body.room_config.allow_spectator, false)
      assert.equal('random_seed' in body.room_config, false)
    }
    selectSichuanWallSource(form, 'training')
    assert.equal(form.allow_spectator, true)
  })
}
test('other rule families retain the independent scene-reproduction form contract', () => {
  const form = createEventRoomForm({ room_rule: 'sichuan', room_config: { sub_rule: 'sichuan/doubles', random_seed: seed, allow_spectator: false } })
  assert.equal(form.scene_reproduction, true)
  const body = buildEventRoomSettings(form)
  assert.equal(body.room_config.random_seed, seed)
  assert.equal(body.room_config.allow_spectator, false)
  assert.equal('wall_source' in body.room_config, false)
})
test('training seeds are always 64 hex characters and nonzero', () => {
  const fakeCrypto = { getRandomValues(bytes) { bytes.fill(0xab) } }
  assert.equal(generateTrainingSeed(fakeCrypto), 'ab'.repeat(32))
  assert.equal(generateTrainingSeed({ getRandomValues(bytes) { bytes.fill(0) } }), '0'.repeat(63) + '1')
})
test('switching rule families clears paired-only source state and retains ordinary presets', () => {
  const form = createEventRoomCreationForm({ room_rule: 'sichuan', room_config: { sub_rule: 'sichuan/doubles_duplicate', wall_source: 'training', random_seed: seed } })
  changeEventRoomRule(form, 'guobiao')
  assert.equal(form.wall_source, '')
  assert.equal(form.random_seed, '')
  assert.equal('wall_source' in buildEventRoomCreation(form).room_config, false)
})

test('switching key acquisition back to automatic clears the fixed key', () => {
  const form = createEventRoomCreationForm({ room_rule: 'sichuan', room_config: { sub_rule: 'sichuan/doubles_duplicate' } })
  selectSichuanKeySource(form, 'existing')
  form.duplicate_key = 'dup_' + 'a'.repeat(32)
  form.room_count = 3
  const fixed = buildEventRoomCreation(form)
  assert.equal(fixed.room_config.duplicate_key, form.duplicate_key)
  assert.match(sharedWallConfirmation(fixed), /相同的密钥/)
  selectSichuanKeySource(form, 'auto')
  assert.equal(form.duplicate_key, '')
  const automatic = buildEventRoomCreation(form)
  assert.equal('duplicate_key' in automatic.room_config, false)
  assert.equal(sharedWallConfirmation(automatic), '')
})

test('random-key creation uses the correct wall layout and requires explicit existing-key mode', () => {
  for (const [sub_rule, rule] of [['sichuan/doubles_duplicate', 'sichuan/sbr'], ['sichuan/xueliu_doubles_duplicate', 'sichuan/xueliu']]) {
    const form = createEventRoomCreationForm({ room_rule: 'sichuan', room_config: { sub_rule } })
    assert.throws(() => buildSichuanKeyRequest(form), /输入已有密钥/)
    selectSichuanKeySource(form, 'existing')
    form.duplicate_round_count = 4
    assert.deepEqual(buildSichuanKeyRequest(form), { rule, wall_type: 'key', round_count: 4, use_flowers: false })
    for (const source of ['training', 'seed', 'manual']) {
      selectSichuanWallSource(form, source)
      assert.throws(() => buildSichuanKeyRequest(form), /密钥牌山/)
    }
  }
})

test('shared-wall warning covers fixed seeds and keys only when creating multiple rooms', () => {
  for (const room_config of [{ random_seed: seed }, { duplicate_key: 'dup_' + 'a'.repeat(32) }]) {
    assert.equal(sharedWallConfirmation({ room_count: 1, room_config }), '')
    assert.match(sharedWallConfirmation({ room_count: 3, room_config }), /是否确认/)
  }
  assert.equal(sharedWallConfirmation({ room_count: 20, room_config: {} }), '')
  const form = createEventRoomCreationForm({ room_rule: 'sichuan', room_config: { sub_rule: 'sichuan/doubles_duplicate' } })
  form.room_count = 3
  selectSichuanWallSource(form, 'training')
  form.random_seed = seed
  assert.match(sharedWallConfirmation(buildEventRoomCreation(form)), /相同的随机种子/)
})
