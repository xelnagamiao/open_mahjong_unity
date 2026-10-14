import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { buildEventRoomSettings, changeEventRoomRule, createEventRoomForm } from '../src/utils/eventRoomSettings.js'
import { updateGuobiaoRoomConfig } from '../src/utils/guobiaoRoomConfig.js'
import { supportsDuplicateKey } from '../src/utils/sichuanDuplicate.js'

const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection } = require('../../server/utils/eventRoomSettings.js')
const profiles = {
  guobiao: ['standard', 'xiaolin', 'kshen', 'lanshi', 'transparent', 'transparent_realtime', 'huahun', 'four_jokers', 'sixteen'],
  riichi: ['standard'], qingque: ['standard'], classical: ['standard'], taiwan: ['standard'],
  sichuan: ['standard', 'xueliu', 'xueliu_exchange', 'doubles', 'xueliu_doubles'],
  changsha: ['classic_double_bird'],
  hongkong: ['qingzhang', 'new13', 'new13_gametower', 'new13_lianhuise', 'qingzhang_lianhuise', 'new16'],
  guangdong: ['tuidao_mil2024', 'mil2023'], guizhou: ['standard'], yixing: ['standard'],
  malaysia: ['traditional84'], shenyang: ['mil2025'], nanjing: ['mil2024'], fuzhou: ['standard'],
  wuhan: ['mil2025'], wenzhou: ['mil2024'], nanchang: ['mil2026'], hangzhou: ['mil2025'],
  hongzhong: ['mil2024'], singapore: ['spgg2024'], jiujiang: ['standard'], changchun: ['mil2024'], shanxi: ['mil2023'],
}

function change(doc, kind, body, presetId) {
  return applyRoomSettingsChange(doc, {
    kind, body: { revision: doc.revision, ...body }, presetId, userId: 10001, eventStatus: 'active',
  }, { id: 'roundtrip-preset', now: '2026-10-14T00:00:00Z' }).settings
}

function roundtrip(form) {
  const payload = JSON.parse(JSON.stringify(buildEventRoomSettings(form)))
  let doc = change(readRoomSettings({}), 'manual', payload)
  doc = change(doc, 'preset-create', { name: '建房设置回归', ...payload })
  doc = change(doc, 'auto', { enabled: true, preset_id: 'roundtrip-preset' })
  doc = readRoomSettings(JSON.stringify(doc))
  const selected = resolveRoomSelection(doc, { revision: doc.revision, preset_id: 'roundtrip-preset' })
  assert.equal(selected.room_rule, form.room_rule)
  for (const [key, value] of Object.entries(payload.room_config)) {
    assert.deepEqual(selected.room_config[key], value, `${form.sub_rule}: ${key}`)
  }
  assert.deepEqual(doc.manual.room_config, selected.room_config)
  assert.deepEqual(doc.auto_match.room_config, selected.room_config)
  const reopened = change(doc, 'preset-update', buildEventRoomSettings(createEventRoomForm(selected)), 'roundtrip-preset')
  assert.deepEqual(reopened.presets[0].room_config, selected.room_config)
  return selected.room_config
}

for (const [room_rule, subRules] of Object.entries(profiles)) {
  for (const sub of subRules) {
    const sub_rule = `${room_rule}/${sub}`
    test(`${sub_rule}: defaults and editable switches survive save, automatic matching and reopening`, () => {
      const form = createEventRoomForm({ room_rule, room_config: { sub_rule, room_name: '回归测试' } })
      roundtrip(form)
      const initial = JSON.stringify(buildEventRoomSettings(form))
      for (const key of Object.keys(form).filter(key => typeof form[key] === 'boolean')) {
        const edited = { ...form, [key]: !form[key] }
        if (room_rule === 'guobiao') updateGuobiaoRoomConfig(edited, key, !form[key])
        if (JSON.stringify(buildEventRoomSettings(edited)) !== initial) roundtrip(edited)
      }
    })
  }
}

test('all visible Guobiao settings keep explicit values, including opening wins and flower-mixed options', () => {
  const form = createEventRoomForm()
  Object.assign(form, { room_name: '国标设置', tian_di_ren_he: true, use_flowers: false,
    open_cuohe: true, cuohe_type: 1, tactical_call: false, claim_protection: false,
    tourist_limit: true, allow_spectator: false, game_round: 2, round_timer: 0, step_timer: 0, hepai_limit: 16 })
  assert.equal(roundtrip(form).tian_di_ren_he, true)
  form.tian_di_ren_he = false
  assert.equal(roundtrip(form).tian_di_ren_he, false)
  updateGuobiaoRoomConfig(form, 'sub_rule', 'guobiao/huahun')
  for (const no_joker_bonus of ['add8', 'double', 'none']) {
    Object.assign(form, { no_joker_bonus, joker_multi_wait: true, black_flower_progressive: false })
    const saved = roundtrip(form)
    assert.equal(saved.no_joker_bonus, no_joker_bonus)
    assert.equal(saved.joker_multi_wait, true)
    assert.equal(saved.black_flower_progressive, false)
  }
})

test('switching rules resets incompatible options in both directions and preserves room identity', () => {
  const form = createEventRoomForm({ password: 'local-password', room_config: { room_name: '我的桌' } })
  form.room_count = 3
  for (const room_rule of Object.keys(profiles)) {
    changeEventRoomRule(form, room_rule)
    assert.equal(form.room_name, '我的桌')
    assert.equal(form.password, 'local-password')
    assert.equal(form.room_count, 3)
    assert.ok(form.sub_rule.startsWith(`${room_rule}/`))
    roundtrip(form)
    changeEventRoomRule(form, 'guobiao')
    assert.equal(form.sub_rule, 'guobiao/standard')
    assert.equal(form.hepai_limit, 8)
    assert.equal(form.use_flowers, true)
    roundtrip(form)
  }
})

test('hints and false-win penalties stay mutually exclusive while editing and switching to Lanshi', () => {
  const form = createEventRoomForm()
  updateGuobiaoRoomConfig(form, 'tips', true)
  updateGuobiaoRoomConfig(form, 'open_cuohe', true)
  assert.equal(form.tips, false)
  form.cuohe_type = 1
  updateGuobiaoRoomConfig(form, 'tips', true)
  assert.equal(form.open_cuohe, false)
  assert.equal(form.cuohe_type, 0)
  roundtrip(form)
  updateGuobiaoRoomConfig(form, 'sub_rule', 'guobiao/lanshi')
  updateGuobiaoRoomConfig(form, 'tips', true)
  assert.equal(form.tips, false)
  assert.equal(form.open_cuohe, true)
  assert.equal(form.cuohe_type, 1)
  roundtrip(form)
})

test('ordinary duplicate walls are offered and accepted only for compatible subrules', () => {
  const duplicate_key = 'dup_0123456789abcdef0123456789abcdef'
  for (const sub of profiles.guobiao) {
    const sub_rule = `guobiao/${sub}`
    const supported = ['standard', 'xiaolin', 'kshen', 'lanshi'].includes(sub)
    assert.equal(supportsDuplicateKey({ room_rule: 'guobiao', sub_rule }), supported)
    const save = () => change(readRoomSettings({}), 'manual', { room_rule: 'guobiao', room_config: { sub_rule, duplicate_key } })
    if (supported) assert.equal(save().manual.room_config.duplicate_key, duplicate_key)
    else assert.throws(save, /复式/)
  }
  for (const sub of ['xueliu', 'xueliu_exchange']) {
    const form = createEventRoomForm({ room_rule: 'sichuan', room_config: { sub_rule: `sichuan/${sub}`, duplicate_key } })
    assert.equal(supportsDuplicateKey(form), true)
    assert.equal(roundtrip(form).duplicate_key, duplicate_key)
  }
})

test('new switch support does not accept malformed values or foreign fields', () => {
  const doc = readRoomSettings({})
  for (const key of ['tian_di_ren_he', 'joker_multi_wait', 'black_flower_progressive', 'use_flowers', 'tactical_call', 'claim_protection']) {
    for (const value of ['false', 0, 1, null]) {
      assert.throws(() => change(doc, 'manual', { room_config: { [key]: value } }), /布尔值/)
    }
  }
  assert.throws(() => change(doc, 'manual', { room_config: { unsupported_setting: true } }), /不支持的对局设置字段/)
  assert.throws(() => change(doc, 'manual', { room_rule: 'riichi', room_config: { tian_di_ren_he: true } }), /不支持的对局设置字段/)
})
