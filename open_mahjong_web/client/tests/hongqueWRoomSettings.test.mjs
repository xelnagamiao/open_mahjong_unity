import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { readFileSync } from 'node:fs'
import { createHash } from 'node:crypto'
import { createEventRoomForm, buildEventRoomSettings, changeEventRoomRule, eventRoomSettingsRows, eventRoomSettingsSummary } from '../src/utils/eventRoomSettings.js'
import { hongqueRulebooks, hongqueRulebook, HONGQUE_W_DESCRIPTION } from '../src/constants/hongqueRulebooks.js'

const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection } = require('../../server/utils/eventRoomSettings.js')
const { normalizeHongqueRoomConfig } = require('../../server/utils/hongqueRoomSettings.js')
class SettingsError extends Error { constructor(code, message) { super(message); this.statusCode = code } }
const w = { sub_rule: 'hongque/w-v1.0' }

for (const colourless of [true, false]) for (const game_round of [4, 8, 12, 16]) {
  test(`W presets preserve colourless=${colourless}, ${game_round} hands and all room options`, () => {
    const form = createEventRoomForm({ room_rule: 'hongque', room_config: {
      ...w, room_name: '  W 测试桌  ', game_round, hongque_colourless: colourless,
      hongque_min_fan: 3, hongque_min_points: 12, hepai_way: 'head_bump',
      round_timer: 0, step_timer: 0, tips: false, count_tips: true, pointer_tips: false,
      tourist_limit: true, allow_spectator: false,
    } })
    const payload = buildEventRoomSettings(form)
    assert.equal(payload.room_config.room_name, 'W 测试桌')
    let doc = readRoomSettings({})
    const update = (kind, body) => {
      doc = applyRoomSettingsChange(doc, { kind, body: { revision: doc.revision, ...body }, userId: 10001, eventStatus: 'active' }, { id: 'hongque-w-preset', now: '2026-10-29T00:00:00Z' }).settings
    }
    update('manual', payload)
    update('preset-create', { name: '虹雀 W', ...payload })
    update('auto', { enabled: true, preset_id: 'hongque-w-preset' })
    doc = readRoomSettings(JSON.stringify(doc))
    const selected = resolveRoomSelection(doc, { revision: doc.revision, preset_id: 'hongque-w-preset' })
    assert.deepEqual(selected.room_config, payload.room_config)
    assert.deepEqual(doc.auto_match.room_config, payload.room_config)
    assert.deepEqual(buildEventRoomSettings(createEventRoomForm(selected)).room_config, payload.room_config)
    const draft = createEventRoomForm(selected)
    draft.hongque_colourless = !colourless
    assert.equal(selected.room_config.hongque_colourless, colourless)
    const rows = Object.fromEntries(eventRoomSettingsRows(selected).map(row => [row.label, row.value]))
    assert.equal(rows['加入无色牌'], colourless ? '开启（144张）' : '关闭（126张）')
    assert.equal(rows['局数'], `${game_round}局`)
    assert.equal(rows['局时储备'], '0 秒')
    assert.equal(rows['点和方式'], '截和')
    assert.equal(rows['起和番数'], '3番')
    assert.equal(rows['起和点数'], '12点')
    assert.match(eventRoomSettingsSummary(selected), new RegExp(`${game_round}局`))
  })
}

test('family defaults preserve Hongque 2, switching profiles clears incompatible serialized fields', () => {
  const form = createEventRoomForm({ room_rule: 'guobiao', room_config: { room_name: '测试', duplicate_key: 'dup_' + 'a'.repeat(32) }, password: 'pass' })
  changeEventRoomRule(form, 'hongque')
  assert.equal(form.sub_rule, 'hongque/v1.6')
  assert.equal(form.game_round, 16)
  assert.equal(form.duplicate_key, '')
  assert.equal(buildEventRoomSettings(form).password, 'pass')
  assert.equal('hongque_colourless' in buildEventRoomSettings(form).room_config, false)
  form.sub_rule = w.sub_rule
  let config = buildEventRoomSettings(form).room_config
  assert.equal(config.hongque_colourless, true)
  assert.equal(config.hongque_min_fan, 0)
  assert.equal(config.hongque_min_points, 0)
  form.sub_rule = 'hongque/v1.6'
  config = buildEventRoomSettings(form).room_config
  assert.equal('hongque_min_fan' in config, false)
  assert.deepEqual(normalizeHongqueRoomConfig(config, SettingsError), config)
  changeEventRoomRule(form, 'riichi')
  assert.equal(buildEventRoomSettings(form).room_config.starting_score, 25000)
  changeEventRoomRule(form, 'hongque')
  assert.equal(buildEventRoomSettings(form).room_config.starting_score, 0)
})

for (const [key, maximum] of [['round_timer', 1000], ['step_timer', 100], ['hongque_min_fan', 100], ['hongque_min_points', 10000]]) {
  test(`${key}: client and Node agree on boundaries`, () => {
    for (const value of [0, maximum]) {
      const payload = buildEventRoomSettings(createEventRoomForm({ room_rule: 'hongque', room_config: { ...w, [key]: value } }))
      assert.equal(normalizeHongqueRoomConfig(payload.room_config, SettingsError)[key], value)
    }
    const form = createEventRoomForm({ room_rule: 'hongque', room_config: w })
    for (const value of [-1, maximum + 1, 0.5, '0', null]) {
      assert.throws(() => buildEventRoomSettings({ ...form, [key]: value }))
      assert.throws(() => normalizeHongqueRoomConfig({ ...w, [key]: value }, SettingsError))
    }
  })
}

test('invalid or incompatible room options cannot be silently persisted', () => {
  const form = createEventRoomForm({ room_rule: 'hongque', room_config: w })
  for (const bad of [{ sub_rule: 'hongque/future' }, { game_round: 1 }, { game_round: 20 }, { game_round: '4' }, { hongque_colourless: 'false' }, { tips: 0 }, { hepai_way: 'any' }, { room_name: '字'.repeat(129) }, { duplicate_key: 'dup_' + 'a'.repeat(32) }]) {
    assert.throws(() => buildEventRoomSettings({ ...form, ...bad }))
    assert.throws(() => normalizeHongqueRoomConfig({ ...w, ...bad }, SettingsError))
  }
  for (const bad of [null, [], 'hongque', { ...w, starting_score: 25000 }, { ...w, tactical_call: false }, { ...w, open_cuohe: true }, { ...w, unknown: true }, { sub_rule: 'hongque/v1.6', hongque_colourless: true }, JSON.parse('{"__proto__":{}}')]) assert.throws(() => normalizeHongqueRoomConfig(bad, SettingsError))
})

test('W uses the supplied introduction and original downloadable rulebook', () => {
  assert.equal(HONGQUE_W_DESCRIPTION, '虹雀W第1.0版使用7种花色、9种数字各2张的虹雀牌，最先将手牌全部组成牌组的玩家赢得一局。彩虹顺子的存在给游戏带来了很多变数，是入门虹雀的最佳选择。')
  assert.equal(hongqueRulebook(w.sub_rule).description, HONGQUE_W_DESCRIPTION)
  assert.ok(hongqueRulebook('hongque/v1.6').description.length > 0)
  assert.equal(hongqueRulebooks.length, 2)
  const pdf = readFileSync(new URL('../public/rulebooks/hongque-w-v1.0.pdf', import.meta.url))
  assert.equal(createHash('sha256').update(pdf).digest('hex'), '948af9e6bd3a1b7a6accbaa8fa1ff62c3f550f1f00424f39d8e4d4180f5112d7')
})
