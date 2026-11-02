import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { readFileSync } from 'node:fs'
import { buildBeijingRoomPayload, BEIJING_OPTIONS, BEIJING_HUN_MODES, BEIJING_DESCRIPTION } from '../src/utils/beijingRoomConfig.js'
import { createEventRoomForm, buildEventRoomSettings, changeEventRoomRule, eventRoomSettingsRows, eventRoomSettingsSummary } from '../src/utils/eventRoomSettings.js'
import { beijingRulebooks } from '../src/constants/beijingRulebooks.js'

const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection } = require('../../server/utils/eventRoomSettings.js')
const { normalizeBeijingRoomConfig } = require('../../server/utils/beijingRoomSettings.js')
class SettingsError extends Error { constructor(code, message) { super(message); this.statusCode = code } }

for (const [mode, label] of Object.entries(BEIJING_HUN_MODES)) {
  test(`${label}: manual, preset, automatic room and reopening preserve every variant`, () => {
    const form = createEventRoomForm({ room_rule: 'beijing', room_config: {
      room_name: '  北京测试桌  ', game_round: 1, round_timer: 0, step_timer: 0,
      tips: false, count_tips: true, pointer_tips: false, tourist_limit: true, allow_spectator: false,
      detailed_config: { hun_mode: mode, ...Object.fromEntries(Object.keys(BEIJING_OPTIONS).map(key => [key, true])) },
    } })
    const payload = buildEventRoomSettings(form)
    assert.equal(payload.room_config.starting_score, 0)
    let doc = readRoomSettings({})
    const update = (kind, body) => {
      doc = applyRoomSettingsChange(doc, { kind, body: { revision: doc.revision, ...body }, userId: 10001, eventStatus: 'active' }, { id: 'beijing-preset', now: '2026-10-22T00:00:00Z' }).settings
    }
    update('manual', payload)
    update('preset-create', { name: '北京吃碰提', ...payload })
    update('auto', { enabled: true, preset_id: 'beijing-preset' })
    doc = readRoomSettings(JSON.stringify(doc))
    const selected = resolveRoomSelection(doc, { revision: doc.revision, preset_id: 'beijing-preset' })
    assert.deepEqual(selected.room_config, payload.room_config)
    assert.deepEqual(doc.auto_match.room_config, payload.room_config)
    assert.deepEqual(buildEventRoomSettings(createEventRoomForm(selected)).room_config, payload.room_config)
    const changed = createEventRoomForm(selected)
    changed.beijing_hun_mode = 'self'
    changed.beijing_half_hun = false
    changed.beijing_dingzhuang = false
    assert.equal(selected.room_config.detailed_config.hun_mode, mode)
    assert.equal(selected.room_config.detailed_config.dingzhuang, true)
    assert.equal(selected.room_config.detailed_config.half_hun, true)
    const rows = Object.fromEntries(eventRoomSettingsRows(selected).map(row => [row.label, row.value]))
    assert.equal(rows['混儿方式'], label)
    assert.equal(rows['局时储备'], '0 秒')
    assert.equal(rows['混儿减半'], '开启')
    for (const option of Object.values(BEIJING_OPTIONS)) assert.equal(rows[option], '开启')
    assert.match(eventRoomSettingsSummary(selected), /1圈/)
  })
}

test('basic rules default to all variants off and survive switching without stale room settings', () => {
  const form = createEventRoomForm({ room_rule: 'guobiao', room_config: { room_name: '一桌', duplicate_key: 'dup_' + 'a'.repeat(32) }, password: 'test' })
  changeEventRoomRule(form, 'beijing')
  const payload = buildBeijingRoomPayload(form)
  assert.equal(payload.password, 'test')
  assert.equal(form.duplicate_key, '')
  assert.equal(payload.room_config.game_round, 4)
  assert.equal(payload.room_config.detailed_config.hun_mode, 'up')
  assert.equal(payload.room_config.detailed_config.half_hun, false)
  for (const key of Object.keys(BEIJING_OPTIONS)) assert.equal(payload.room_config.detailed_config[key], false)
  for (const key of ['use_flowers', 'open_cuohe', 'tactical_call', 'claim_protection']) assert.equal(payload.room_config[key], false)
  changeEventRoomRule(form, 'riichi')
  assert.equal(buildEventRoomSettings(form).room_config.starting_score, 25000)
  changeEventRoomRule(form, 'beijing')
  assert.equal(buildEventRoomSettings(form).room_config.starting_score, 0)
})

for (const key of Object.keys(BEIJING_OPTIONS)) {
  test(`${key}: boolean values round-trip; malformed switches rejected`, () => {
    for (const value of [true, false]) {
      const form = createEventRoomForm({ room_rule: 'beijing', room_config: { detailed_config: { [key]: value } } })
      const payload = buildBeijingRoomPayload(form)
      assert.equal(normalizeBeijingRoomConfig(payload.room_config, SettingsError).detailed_config[key], value)
    }
    const form = createEventRoomForm({ room_rule: 'beijing' })
    form['beijing_' + key] = 'false'
    assert.throws(() => buildBeijingRoomPayload(form))
    assert.throws(() => normalizeBeijingRoomConfig({ detailed_config: { [key]: 'false' } }, SettingsError))
  })
}

test('room boundaries reject invalid values, wrong rules and incompatible settings', () => {
  const form = createEventRoomForm({ room_rule: 'beijing' })
  for (const bad of [{ sub_rule: 'beijing/standard' }, { game_round: 0 }, { game_round: 5 }, { game_round: 1.5 }, { round_timer: -1 }, { round_timer: 1001 }, { step_timer: 101 }, { tips: 'false' }, { duplicate_key: 'dup_' + 'a'.repeat(32) }, { room_name: '字'.repeat(129) }]) {
    assert.throws(() => buildBeijingRoomPayload({ ...form, ...bad }))
    assert.throws(() => normalizeBeijingRoomConfig(bad, SettingsError))
  }
  for (const bad of [null, [], 'beijing', { starting_score: 25000 }, { use_flowers: true }, { unknown: false }, { detailed_config: [] }, { detailed_config: { rule_version: 'future' } }, { detailed_config: { hun_mode: 'bad' } }, { detailed_config: { joker: true } }, { detailed_config: { half_hun_threshold: 1 } }, JSON.parse('{"__proto__":{}}')]) assert.throws(() => normalizeBeijingRoomConfig(bad, SettingsError))
  assert.throws(() => buildBeijingRoomPayload({ ...form, beijing_hun_mode: 'toString' }))
})

test('published rulebook retains the supplied introduction, table and all exponent formulas', () => {
  assert.equal(beijingRulebooks.length, 2)
  const html = readFileSync(new URL('../public/rulebooks/beijing/rules.html', import.meta.url), 'utf8')
  assert.ok(html.includes(BEIJING_DESCRIPTION))
  assert.ok(html.includes('<table>'))
  assert.ok(html.includes('3×2<sup>F</sup>'))
  assert.equal(html.match(/2<sup>F<\/sup>/g).length, 3)
  for (const text of ['严格独听', '混儿减半', '包四口', '天听', '豪华七对']) assert.ok(html.includes(text))
})
