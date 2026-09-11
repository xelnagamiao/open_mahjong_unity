import test from 'node:test'
import assert from 'node:assert/strict'
import { buildEventRoomSettings, createEventRoomForm, eventRoomSettingsSummary, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'

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
  form.room_rule = 'riichi'
  form.room_name = '  立直桌  '
  const settings = buildEventRoomSettings(form)
  assert.deepEqual(settings.room_config, { room_name: '立直桌' })
  assert.equal(settings.room_rule, 'riichi')
})

test('table confirmation shows every supported setting without mutating the saved default', () => {
  const settings = { room_rule: 'guobiao', room_config: { sub_rule: 'guobiao/lanshi', game_round: 2, round_timer: 0, step_timer: 0, hepai_limit: 5, open_cuohe: true, cuohe_type: 1, allow_spectator: false, tactical_call: false, claim_protection: false }, password: 'private' }
  const initial = JSON.stringify(settings)
  const rows = eventRoomSettingsRows(settings, { guobiao: '国标' })
  const values = Object.fromEntries(rows.map(row => [row.label, row.value]))
  assert.equal(values['子规则'], '蓝氏')
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
