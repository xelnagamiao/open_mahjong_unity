import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { loadChangchunForm, buildChangchunRoomPayload, CHANGCHUN_CONFIG } from '../src/utils/changchunRoomConfig.js'
const require = createRequire(import.meta.url)
const { normalizeChangchunRoomConfig } = require('../../server/utils/changchunRoomSettings.js')
class SettingsError extends Error { constructor(code, message) { super(message); this.code = code } }
test('all 32 switch combinations and four match lengths roundtrip', () => {
  const keys = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator']
  for (let mask = 0; mask < 32; mask++) for (const rounds of [1, 2, 3, 4]) {
    const form = loadChangchunForm({ room_rule: 'changchun', room_name: '长春测试' })
    keys.forEach((key, i) => { form[key] = Boolean(mask & (1 << i)) }); form.game_round = rounds
    const payload = buildChangchunRoomPayload(form)
    const normalized = normalizeChangchunRoomConfig(payload.room_config, SettingsError)
    keys.forEach(key => assert.equal(normalized[key], form[key]))
    assert.deepEqual(normalized.detailed_config, CHANGCHUN_CONFIG)
    assert.equal(normalized.game_round, rounds)
  }
})
for (const [key, value] of [['game_round', 0], ['round_timer', -1], ['step_timer', 101], ['tips', 1], ['sub_rule', 'changchun/folk'], ['duplicate_key', 'abc']]) test(`client rejects invalid ${key}`, () => assert.throws(() => buildChangchunRoomPayload({ ...loadChangchunForm({}), [key]: value })))
for (const raw of [null, [], true, { wrong: 1 }, { tips: 1 }, { use_flowers: true }, { detailed_config: { fan_cap: 7 } }, { detailed_config: [] }, { room_name: 1 }, { duplicate_key: 'x' }]) test(`server rejects ${JSON.stringify(raw)}`, () => assert.throws(() => normalizeChangchunRoomConfig(raw, SettingsError)))
