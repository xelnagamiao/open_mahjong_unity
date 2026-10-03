import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { loadHongzhongForm, buildHongzhongRoomPayload, HONGZHONG_CONFIG } from '../src/utils/hongzhongRoomConfig.js'
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows, eventRoomSettingsSummary } from '../src/utils/eventRoomSettings.js'
const require = createRequire(import.meta.url)
const { normalizeHongzhongRoomConfig } = require('../../server/utils/hongzhongRoomSettings.js')
const { readRoomSettings, applyRoomSettingsChange } = require('../../server/utils/eventRoomSettings.js')
class SettingsError extends Error { constructor(code, message) { super(message); this.code = code } }
const flags = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator']

test('全部五开关、四种局长与计时边界在前后端、赛事预设完整往返', () => {
  for (let bits = 0; bits < 32; bits++) for (const rounds of [1, 2, 3, 4]) for (const [timer, step] of [[0, 0], [20, 5], [1000, 100]]) {
    const form = loadHongzhongForm({ room_rule: 'hongzhong' })
    flags.forEach((key, i) => { form[key] = Boolean(bits & (1 << i)) })
    Object.assign(form, { game_round: rounds, round_timer: timer, step_timer: step })
    const payload = buildHongzhongRoomPayload(form)
    const normalized = normalizeHongzhongRoomConfig(payload.room_config, SettingsError)
    for (const kind of ['manual', 'preset-create']) {
      const initial = readRoomSettings({})
      const saved = applyRoomSettingsChange(initial, { kind, userId: 101, eventStatus: 'active', body: { revision: initial.revision, ...payload, name: '红中' } }).settings
      const setting = kind === 'manual' ? saved.manual : saved.presets[0]
      const restored = createEventRoomForm(setting)
      for (const key of [...flags, 'game_round', 'round_timer', 'step_timer']) assert.equal(restored[key], form[key], key)
      assert.deepEqual(buildEventRoomSettings(restored).room_config.detailed_config, HONGZHONG_CONFIG)
      assert.deepEqual(readRoomSettings(saved), saved)
    }
    assert.deepEqual(normalized.detailed_config, HONGZHONG_CONFIG)
  }
})

test('拒绝错误类型、版本、规则覆写及未知字段', () => {
  const bad = [null, [], 'bad', { room_name: 'x'.repeat(129) }, { sub_rule: 'hongque/standard' }, { duplicate_key: 'code' }, { duplicate_key: false }, { unknown: true },
    { detailed_config: null }, { detailed_config: [] }, { detailed_config: { fan_cap: 5 } }, { detailed_config: { fan_cap: true } }, { detailed_config: { self_draw_only: 1 } }, { detailed_config: { future: true } },
    ...flags.map(key => ({ [key]: 'true' })), ...['use_flowers', 'open_cuohe', 'tactical_call', 'claim_protection', 'tian_di_ren_he'].map(key => ({ [key]: true })),
    { game_round: 0 }, { game_round: 5 }, { game_round: 1.5 }, { round_timer: -1 }, { round_timer: 1001 }, { step_timer: 101 }, { step_timer: true }, { room_name: 3 },
  ]
  for (const input of bad) assert.throws(() => normalizeHongzhongRoomConfig(input, SettingsError))
  for (const changes of [{ tips: 'true' }, { game_round: '1' }, { sub_rule: 'other' }, { duplicate_key: 'code' }]) assert.throws(() => buildHongzhongRoomPayload({ ...loadHongzhongForm({}), ...changes }))
  assert.equal(normalizeHongzhongRoomConfig({ duplicate_key: '', room_name: ' 红中 ' }, SettingsError).room_name, '红中')
})

test('设置摘要准确说明固定番数、扎鸟和零秒储备', () => {
  const settings = buildHongzhongRoomPayload(loadHongzhongForm({ room_rule: 'hongzhong' }, { game_round: 2, round_timer: 0 }))
  assert.equal(eventRoomSettingsSummary(settings, { hongzhong: '红中麻将' }), '红中麻将 · MIL 2024 · 8局')
  const rows = eventRoomSettingsRows(settings, { hongzhong: '红中麻将' })
  assert.equal(rows.find(row => row.label === '局数').value, '8局')
  assert.equal(rows.find(row => row.label === '局时储备').value, '0 秒')
  assert.equal(rows.find(row => row.label === '和牌').value, '仅自摸，四番封顶')
})

test('直接房间草稿使用明确名称密码与缺省开关', () => {
  const form = loadHongzhongForm({ room_name: ' 红中 ', password: ' private ' })
  flags.forEach(key => { delete form[key] })
  const payload = buildHongzhongRoomPayload(form)
  assert.equal(payload.room_config.room_name, '红中')
  assert.equal(payload.password, 'private')
  assert.equal(payload.room_config.tips, true)
  assert.equal(payload.room_config.count_tips, false)
  assert.equal(payload.room_config.pointer_tips, true)
  assert.equal(payload.room_config.tourist_limit, false)
  assert.equal(payload.room_config.allow_spectator, true)
})
