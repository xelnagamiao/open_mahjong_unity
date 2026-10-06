import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { loadChangchunForm, buildChangchunRoomPayload, CHANGCHUN_CONFIG } from '../src/utils/changchunRoomConfig.js'
const require = createRequire(import.meta.url)
const { normalizeChangchunRoomConfig } = require('../../server/utils/changchunRoomSettings.js')
class SettingsError extends Error { constructor(code, message) { super(message); this.code = code } }
test('all 64 switch combinations and four match lengths roundtrip', () => {
  const keys = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator', 'tactical_call']
  for (let mask = 0; mask < 64; mask++) for (const rounds of [1, 2, 3, 4]) {
    const form = loadChangchunForm({ room_rule: 'changchun', room_name: '长春测试' })
    keys.forEach((key, i) => { form[key] = Boolean(mask & (1 << i)) }); form.game_round = rounds
    const payload = buildChangchunRoomPayload(form)
    const normalized = normalizeChangchunRoomConfig(payload.room_config, SettingsError)
    keys.forEach(key => assert.equal(normalized[key], form[key]))
    assert.deepEqual(normalized.detailed_config, CHANGCHUN_CONFIG)
    assert.equal(normalized.game_round, rounds)
  }
})
for (const [key, value] of [['game_round', 0], ['round_timer', -1], ['step_timer', 101], ['tips', 1], ['tactical_call', 1], ['sub_rule', 'changchun/folk'], ['duplicate_key', 'abc']]) test(`client rejects invalid ${key}`, () => assert.throws(() => buildChangchunRoomPayload({ ...loadChangchunForm({}), [key]: value })))
for (const raw of [null, [], true, { wrong: 1 }, { tips: 1 }, { tactical_call: 1 }, { claim_protection: true }, { use_flowers: true }, { detailed_config: { fan_cap: 7 } }, { detailed_config: [] }, { room_name: 1 }, { duplicate_key: 'x' }]) test(`server rejects ${JSON.stringify(raw)}`, () => assert.throws(() => normalizeChangchunRoomConfig(raw, SettingsError)))

test('default 20+5 and custom/zero timers survive client payload and Node normalization', () => {
  const defaults = loadChangchunForm({})
  assert.equal(defaults.round_timer, 20)
  assert.equal(defaults.step_timer, 5)
  for (const bank of [0, 20, 47, 1000]) for (const step of [0, 3, 5, 9, 100]) {
    const payload = buildChangchunRoomPayload({ ...defaults, round_timer: bank, step_timer: step })
    const normalized = normalizeChangchunRoomConfig(payload.room_config, SettingsError)
    assert.equal(normalized.round_timer, bank)
    assert.equal(normalized.step_timer, step)
  }
})

test('new human rooms default tactical on and existing off survives form payload Node validation and preview', async () => {
  const { eventRoomSettingsRows } = await import('../src/utils/eventRoomSettings.js')
  assert.equal(loadChangchunForm({}).tactical_call, true)
  assert.equal(normalizeChangchunRoomConfig({}, SettingsError).tactical_call, true)
  for (const tactical_call of [true, false]) {
    const form = loadChangchunForm({ room_rule: 'changchun' }, { tactical_call })
    const payload = buildChangchunRoomPayload(form)
    assert.equal(payload.room_config.tactical_call, tactical_call)
    assert.equal(normalizeChangchunRoomConfig(payload.room_config, SettingsError).tactical_call, tactical_call)
    const row = eventRoomSettingsRows(payload).find(row => row.label === '战术鸣牌')
    assert.equal(row.value, tactical_call ? '开启' : '关闭')
  }
})
