import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { buildDuocaiRoomPayload, changeDuocaiProfile, DUOCAI_PROFILES } from '../src/utils/duocaiRoomConfig.js'
import { createEventRoomForm, buildEventRoomSettings, changeEventRoomRule, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'

const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection } = require('../../server/utils/eventRoomSettings.js')
const { normalizeDuocaiRoomConfig } = require('../../server/utils/duocaiRoomSettings.js')
class SettingsError extends Error { constructor(code, message) { super(message); this.statusCode = code } }

for (const profile of DUOCAI_PROFILES) {
  test(`${profile.label} survives manual, saved preset, automatic selection and reopen`, () => {
    const form = createEventRoomForm({ room_rule: 'duocai', room_config: { sub_rule: profile.value, room_name: '  夺彩测试桌  ', round_timer: 0, step_timer: 0, tips: false, pointer_tips: false, count_tips: true, tourist_limit: true, allow_spectator: false } })
    const payload = buildEventRoomSettings(form)
    const original = structuredClone(payload)
    let doc = readRoomSettings({})
    const update = (kind, body, presetId) => {
      doc = applyRoomSettingsChange(doc, { kind, body: { revision: doc.revision, ...body }, presetId, userId: 10001, eventStatus: 'active' }, { id: 'duocai-preset', now: '2026-10-21T00:00:00Z' }).settings
    }
    update('manual', payload)
    update('preset-create', { name: '夺彩配置', ...payload })
    update('auto', { enabled: true, preset_id: 'duocai-preset' })
    doc = readRoomSettings(JSON.stringify(doc))
    const selected = resolveRoomSelection(doc, { revision: doc.revision, preset_id: 'duocai-preset' })
    assert.equal(selected.room_rule, 'duocai')
    assert.deepEqual(selected.room_config, payload.room_config)
    assert.deepEqual(doc.auto_match.room_config, selected.room_config)
    assert.deepEqual(buildEventRoomSettings(createEventRoomForm(selected)).room_config, payload.room_config)
    assert.deepEqual(payload, original)
    const cancelled = createEventRoomForm(selected)
    cancelled.count_tips = false
    cancelled.round_timer = 15
    assert.equal(selected.room_config.count_tips, true)
    assert.equal(selected.room_config.round_timer, 0)
    const rows = Object.fromEntries(eventRoomSettingsRows(selected).map(row => [row.label, row.value]))
    assert.equal(rows['局时储备'], '0 秒')
    assert.equal(rows['子规则'], `夺彩·${profile.label}`)
    assert.equal(rows['起始 / 返点'], '40000 / 40000 点')
  })
}

test('switching from other rules preserves room identity and discards incompatible options', () => {
  const form = createEventRoomForm({ room_rule: 'guobiao', room_config: { room_name: '桌一', duplicate_key: 'dup_' + 'a'.repeat(32), use_flowers: true, open_cuohe: true }, password: 'local-test' })
  changeEventRoomRule(form, 'duocai')
  assert.equal(form.password, 'local-test')
  assert.equal(form.duplicate_key, '')
  const first = buildDuocaiRoomPayload(form)
  assert.equal(first.room_config.sub_rule, 'duocai/guobiao')
  assert.equal(first.room_config.game_round, 4)
  assert.equal(first.room_config.use_flowers, false)
  changeDuocaiProfile(form, 'duocai/riichi')
  const second = buildDuocaiRoomPayload(form)
  assert.equal(second.room_config.game_round, 2)
  assert.equal(second.room_config.starting_score, 40000)
  assert.equal(second.room_config.room_name, '桌一')
  assert.deepEqual(second.room_config.detailed_config, { rule_version: 'duocai-v1' })
  for (const field of ['hepai_limit', 'red_dora', 'allow_kuikae', 'duplicate_key', 'riichi_detailed_config']) assert.equal(field in second.room_config, false)
  changeEventRoomRule(form, 'riichi')
  assert.equal(buildEventRoomSettings(form).room_config.starting_score, 25000)
})

test('both boundaries reject unsupported modes and malformed settings', () => {
  const form = createEventRoomForm({ room_rule: 'duocai' })
  for (const bad of [{ sub_rule: 'duocai/sichuan' }, { sub_rule: 'duocai/xuehe' }, { sub_rule: 'riichi/standard' }, { game_round: 1 }, { round_timer: -1 }, { step_timer: 101 }, { step_timer: 0.5 }, { tips: 'false' }, { duplicate_key: 'dup_' + 'a'.repeat(32) }]) {
    assert.throws(() => buildDuocaiRoomPayload({ ...form, ...bad }))
    assert.throws(() => normalizeDuocaiRoomConfig({ ...bad }, SettingsError))
  }
  for (const bad of [{ starting_score: 25000 }, { room_name: 'a'.repeat(129) }, { use_flowers: true }, { open_cuohe: true }, { detailed_config: { rule_version: 'duocai-v2' } }, { detailed_config: { rank_points: '10_30' } }, { variant: 'sichuan' }, JSON.parse('{"__proto__":{}}')]) assert.throws(() => normalizeDuocaiRoomConfig(bad, SettingsError))
  for (const bad of [null, [], 'duocai']) assert.throws(() => normalizeDuocaiRoomConfig(bad, SettingsError))
})
