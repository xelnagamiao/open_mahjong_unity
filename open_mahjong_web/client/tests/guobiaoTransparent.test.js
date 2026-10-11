import test from 'node:test'
import assert from 'node:assert/strict'
import { createDefaultGuobiaoRoomConfig, buildGuobiaoRoomPayload } from '../src/utils/guobiaoRoomConfig.js'
import { createEventRoomCreationForm, buildEventRoomCreation } from '../src/utils/eventRoomCreation.js'

test('transparent MCR keeps standard settings including flowers and eight fan', () => {
  const form = {...createDefaultGuobiaoRoomConfig(), sub_rule: 'guobiao/transparent', tian_di_ren_he: true}
  const before = structuredClone(form)
  const {room_config} = buildGuobiaoRoomPayload(form)
  assert.equal(room_config.sub_rule, 'guobiao/transparent')
  assert.equal(room_config.hepai_limit, 8)
  assert.equal(room_config.use_flowers, true)
  assert.equal(room_config.tian_di_ren_he, true)
  assert.equal(room_config.tactical_call, true)
  assert.equal(room_config.claim_protection, true)
  assert.equal('dead_wall_count' in room_config, false)
  assert.equal('dora_indicators' in room_config, false)
  assert.deepEqual(form, before)
  form.use_flowers = false
  assert.equal(buildGuobiaoRoomPayload(form).room_config.use_flowers, false)
})

test('ordinary duplicate keys cannot silently remove the transparent distribution', () => {
  assert.throws(() => buildGuobiaoRoomPayload({...createDefaultGuobiaoRoomConfig(),sub_rule:'guobiao/transparent',duplicate_key:'test-wall'}), /透明国标/)
  const form = createEventRoomCreationForm({room_rule:'guobiao',room_config:{sub_rule:'guobiao/transparent'}})
  form.duplicate_enabled = true
  form.auto_duplicate = true
  assert.throws(() => buildEventRoomCreation(form), /透明国标/)
})
