import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import catalog from '../../../open_mahjong_server/server/game_calculation/riichi/rule_options.json' with { type: 'json' }
import { buildEventRoomSettings, createEventRoomForm } from '../src/utils/eventRoomSettings.js'
import { applyRiichiPreset, selectedRiichiPreset } from '../src/utils/riichiRoomConfig.js'

const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection } = require('../../server/utils/eventRoomSettings.js')

for (const preset of catalog.presets) {
  test(`${preset.id}: full preset survives edit, save, automatic matching and reopen`, () => {
    const expected = { ...preset.room, sub_rule: preset.sub_rule || 'riichi/standard', detailed_config: { ...preset.values } }
    const form = createEventRoomForm({ room_rule: 'riichi', room_config: expected })
    const payload = buildEventRoomSettings(form)
    for (const [key, value] of Object.entries(expected)) assert.deepEqual(payload.room_config[key], value, key)
    let doc = readRoomSettings({})
    doc = applyRoomSettingsChange(doc, { kind: 'preset-create', body: { ...payload, name: preset.label, revision: doc.revision }, userId: 10001, eventStatus: 'active' }, { id: preset.id }).settings
    doc = applyRoomSettingsChange(doc, { kind: 'auto', body: { enabled: true, preset_id: preset.id, revision: doc.revision }, userId: 10001, eventStatus: 'active' }).settings
    doc = readRoomSettings(JSON.stringify(doc))
    const stored = resolveRoomSelection(doc, { revision: doc.revision, preset_id: preset.id })
    for (const [key, value] of Object.entries(expected)) {
      assert.deepEqual(stored.room_config[key], value, key)
      assert.deepEqual(doc.auto_match.room_config[key], value, `auto ${key}`)
    }
    assert.deepEqual(buildEventRoomSettings(createEventRoomForm(stored)).room_config, stored.room_config)
  })
}

test('preset changes preserve room identity, timers and independent drafts', () => {
  const form = createEventRoomForm({ room_rule: 'riichi', password: 'test', room_config: { room_name: 'audit', round_timer: 15, step_timer: 3 } })
  for (const preset of catalog.presets) {
    applyRiichiPreset(form, preset.id)
    assert.equal(selectedRiichiPreset(form).id, preset.id)
    assert.deepEqual([form.room_name, form.password, form.round_timer, form.step_timer], ['audit', 'test', 15, 3])
    const second = createEventRoomForm(buildEventRoomSettings(form))
    form.riichi_detailed_config.ura_dora = !preset.values.ura_dora
    assert.equal(selectedRiichiPreset(form), undefined)
    assert.equal(second.riichi_detailed_config.ura_dora, preset.values.ura_dora)
    assert.equal(preset.values.ura_dora, second.riichi_detailed_config.ura_dora)
  }
})

test('all detailed option values survive the browser and Node boundaries', () => {
  for (const option of catalog.options) for (const value of option.values) {
    const form = createEventRoomForm({ room_rule: 'riichi' })
    applyRiichiPreset(form, 'sanma_tenhou')
    form.riichi_detailed_config[option.key] = value
    if (option.key === 'minimum_win_score') form.minimum_win_score = value
    const payload = buildEventRoomSettings(form)
    const doc = readRoomSettings({})
    const stored = applyRoomSettingsChange(doc, { kind: 'manual', body: { ...payload, revision: doc.revision }, userId: 10001, eventStatus: 'active' }).settings.manual
    assert.equal(stored.room_config.detailed_config[option.key], value, option.key)
  }
})

test('malformed, unknown and incompatible detailed options cannot silently become defaults', () => {
  for (const detail of [{ ura_dora: 'false' }, { ura_dora: 0 }, { tenpai_owned_tiles: 'unknown' }, { unknown: true }, { noten_penalty: 2000 }, { rank_points: 'sanma_15' }]) {
    const form = createEventRoomForm({ room_rule: 'riichi', room_config: { detailed_config: detail } })
    assert.throws(() => buildEventRoomSettings(form))
    const doc = readRoomSettings({})
    assert.throws(() => applyRoomSettingsChange(doc, { kind: 'manual', body: { room_rule: 'riichi', room_config: { detailed_config: detail }, revision: doc.revision }, userId: 10001, eventStatus: 'active' }))
  }
})
