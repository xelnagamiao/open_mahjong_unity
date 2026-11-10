import test from 'node:test'
import assert from 'node:assert/strict'
import { buildEventRoomSettings, changeEventRoomRule, createEventRoomForm, eventRoomSettingsSummary, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'

test('Sichuan variants and generic editors preserve custom timers in presets and room creation', () => {
  for (const sub_rule of ['standard', 'doubles', 'xueliu', 'xueliu_exchange', 'xueliu_doubles', 'doubles_duplicate', 'xueliu_doubles_duplicate']) {
    const form = createEventRoomForm({ room_rule: 'sichuan', room_config: { sub_rule: `sichuan/${sub_rule}`, round_timer: 127, step_timer: 17 } })
    const saved = buildEventRoomSettings(form)
    const reloaded = createEventRoomForm(saved)
    assert.equal(reloaded.round_timer, 127)
    assert.equal(reloaded.step_timer, 17)
    form.round_timer = 0
    assert.equal(eventRoomSettingsRows(buildEventRoomSettings(form)).find(row => row.label === '局时').value, '0 秒')
  }
  for (const room_rule of ['qingque', 'classical', 'taiwan']) {
    const saved = buildEventRoomSettings(createEventRoomForm({ room_rule, room_config: { round_timer: 96, step_timer: 12 } }))
    assert.equal(saved.room_config.round_timer, 96)
    assert.equal(saved.room_config.step_timer, 12)
  }
  for (const value of [null, -1, 1001, 2.5, '20']) {
    const form = createEventRoomForm({ room_rule: 'sichuan' })
    form.round_timer = value
    assert.throws(() => buildEventRoomSettings(form), /局时/)
  }
})

test('manual, automatic and dialog forms do not mutate their shared preset', () => {
  const preset = {
    room_rule: 'guobiao',
    room_config: { game_round: 2, step_timer: 10, tips: false },
    password: '1234',
  }
  const manual = createEventRoomForm(preset)
  const automatic = createEventRoomForm(preset)
  const draft = createEventRoomForm(buildEventRoomSettings(manual))
  draft.game_round = 1
  draft.tips = true
  draft.password = 'changed'
  assert.equal(manual.game_round, 2)
  assert.equal(automatic.game_round, 2)
  assert.equal(preset.room_config.game_round, 2)
  assert.equal(preset.room_config.tips, false)
  assert.equal(manual.password, '1234')
  assert.equal(buildEventRoomSettings(draft).room_config.game_round, 1)
})

test('password switch hides a retained draft from payloads and survives rule changes', () => {
  assert.equal(createEventRoomForm().password_enabled, false)
  const form = createEventRoomForm({ password: 'saved-password' })
  assert.equal(form.password_enabled, true)
  assert.equal(buildEventRoomSettings(form).password, 'saved-password')
  form.password_enabled = false
  for (const rule of ['sichuan', 'riichi', 'guizhou', 'guobiao']) {
    changeEventRoomRule(form, rule)
    assert.equal(form.password_enabled, false)
    assert.equal(form.password, 'saved-password')
    assert.equal(buildEventRoomSettings(form).password, '')
  }
  form.password_enabled = true
  assert.equal(buildEventRoomSettings(form).password, 'saved-password')
})

test('zero timers and disabled booleans survive the persisted settings round trip', () => {
  const form = createEventRoomForm({
    room_rule: 'guobiao',
    room_config: { round_timer: 0, step_timer: 0, allow_spectator: false, claim_protection: false },
  })
  const saved = buildEventRoomSettings(form)
  assert.equal(saved.room_config.round_timer, 0)
  assert.equal(saved.room_config.step_timer, 0)
  assert.equal(saved.room_config.allow_spectator, false)
  assert.equal(saved.room_config.claim_protection, false)
  assert.match(eventRoomSettingsSummary(saved, { guobiao: '国标' }), /局时 0s · 步时 0s/)
})

test('switching to another rule only submits the fields it supports', () => {
  const form = createEventRoomForm()
  changeEventRoomRule(form, 'riichi')
  form.room_name = '  立直桌  '
  const settings = buildEventRoomSettings(form)
  assert.equal(settings.room_config.room_name, '立直桌')
  assert.equal(settings.room_config.sub_rule, 'riichi/standard')
  assert.equal(settings.room_config.starting_score, 25000)
  assert.equal(settings.room_config.hepai_limit, 1)
  assert.equal(settings.room_config.detailed_config.minimum_win_score, 0)
  assert.equal(settings.room_config.claim_protection, false)
  for (const key of ['use_flowers', 'cuohe_type', 'tactical_call', 'duplicate_key']) {
    assert.equal(key in settings.room_config, false, key)
  }
  assert.equal(settings.room_rule, 'riichi')
})

test('table confirmation shows every supported setting without mutating the saved default', () => {
  const settings = { room_rule: 'guobiao', room_config: { sub_rule: 'guobiao/lanshi', game_round: 2, round_timer: 0, step_timer: 0, hepai_limit: 5, open_cuohe: true, cuohe_type: 1, allow_spectator: false, tactical_call: false, claim_protection: false }, password: 'private' }
  const initial = JSON.stringify(settings)
  const rows = eventRoomSettingsRows(settings, { guobiao: '国标' })
  const values = Object.fromEntries(rows.map(row => [row.label, row.value]))
  assert.equal(values['子规则'], '蓝十')
  assert.equal(values['局时'], '不限时')
  assert.equal(values['步时'], '0 秒')
  assert.equal(values['起和番'], '5 番')
  assert.equal(values['错和形式'], '错和 -40，其余不加分')
  assert.equal(values['允许观战'], '关闭')
  assert.equal(values['战术鸣牌'], '关闭')
  assert.equal(values['鸣牌保护'], '关闭')
  assert.doesNotMatch(JSON.stringify(rows), /private|password/)
  assert.equal(JSON.stringify(settings), initial)
})

test('non-Guobiao confirmation uses readable rule names and omits Guobiao-only switches', () => {
  const rows = eventRoomSettingsRows({ room_rule: 'taiwan', room_config: { sub_rule: 'taiwan/standard' } }, { taiwan: '台湾' })
  assert.equal(rows.find(row => row.label === '规则').value, '台湾')
  assert.equal(rows.find(row => row.label === '子规则').value, '台湾标准')
  assert.equal(rows.some(row => ['起和番', '错和', '战术鸣牌', '鸣牌保护'].includes(row.label)), false)
})


test('riichi starting points survive editing, preset round trip and confirmation', () => {
  const form = createEventRoomForm({ room_rule: 'riichi', room_config: { starting_score: 30000 } })
  const saved = buildEventRoomSettings(form)
  assert.equal(createEventRoomForm(saved).starting_score, 30000)
  assert.match(eventRoomSettingsSummary(saved), /30000/)
  assert.equal(eventRoomSettingsRows(saved).find(row => row.label === '起始点数').value, '30000 点')
  for (const value of [undefined, null, 0, 999, 1000001, 25001, 25000.5, '30000']) {
    assert.throws(() => buildEventRoomSettings({ ...form, starting_score: value }), /起始点数/)
  }
  form.room_rule = 'guobiao'
  assert.equal('starting_score' in buildEventRoomSettings(form).room_config, false)
  form.room_rule = 'taiwan'
  assert.equal('starting_score' in buildEventRoomSettings(form).room_config, false)
})
