import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'

const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection } = require('../../server/utils/eventRoomSettings.js')
const profiles = { nanchang: 'mil2026', nanjing: 'mil2024', shenyang: 'mil2025', wuhan: 'mil2025' }

function change(doc, kind, body, options = {}) {
  return applyRoomSettingsChange(doc, {
    kind, body: { revision: doc.revision, ...body }, userId: 10000101, eventStatus: 'active',
  }, { now: '2026-10-12T00:00:00Z', ...options }).settings
}

for (const [rule, edition] of Object.entries(profiles)) {
  test(`${rule}: room form survives manual, preset, automatic and restored selection`, () => {
    const form = createEventRoomForm({ room_rule: rule })
    Object.assign(form, { room_name: `${rule} roundtrip`, game_round: 2, round_timer: 0,
      step_timer: 0, tips: true, count_tips: true, pointer_tips: false,
      tourist_limit: true, allow_spectator: false, password: 'unsaved-test-password' })
    const payload = buildEventRoomSettings(form)
    assert.equal(payload.room_config.sub_rule, `${rule}/${edition}`)
    assert.equal(typeof payload.room_config.detailed_config.rule_version, 'string')
    const manual = change(readRoomSettings({}), 'manual', payload)
    const presets = change(manual, 'preset-create', { name: `${rule} test`, ...payload }, { id: `${rule}-preset` })
    const automatic = change(presets, 'auto', { enabled: true, preset_id: `${rule}-preset` })
    const restored = readRoomSettings(JSON.stringify(automatic))
    const selected = resolveRoomSelection(restored, { revision: restored.revision, preset_id: `${rule}-preset` })
    assert.equal(selected.room_rule, rule)
    assert.deepEqual(selected.room_config, payload.room_config)
    assert.deepEqual(restored.auto_match.room_config, payload.room_config)
    const rebuilt = buildEventRoomSettings(createEventRoomForm(selected))
    assert.deepEqual(rebuilt.room_config, payload.room_config)
    assert.ok(eventRoomSettingsRows(selected).some(row => String(row.value).includes(edition.slice(3))))
    assert.equal(JSON.stringify(restored).includes('unsaved-test-password'), false)
    assert.equal(readRoomSettings({}).manual.room_rule, 'guobiao')
  })

  test(`${rule}: event settings reject foreign editions and gameplay switches`, () => {
    const payload = buildEventRoomSettings(createEventRoomForm({ room_rule: rule }))
    for (const invalid of [
      { sub_rule: `${rule}/unsupported` },
      { detailed_config: { rule_version: 'unsupported-edition' } },
      { tactical_call: true },
      { duplicate_key: 'dup_0123456789abcdef0123456789abcdef' },
    ]) {
      assert.throws(() => change(readRoomSettings({}), 'manual', {
        ...payload, room_config: { ...payload.room_config, ...invalid },
      }), /不支持|仅支持|不能|只支持|固定|须|必须|复式|版本/)
    }
  })
}
