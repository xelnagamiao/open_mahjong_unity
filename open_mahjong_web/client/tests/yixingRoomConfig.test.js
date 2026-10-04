import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { loadYixingForm, buildYixingRoomPayload } from '../src/utils/yixingRoomConfig.js'
import { parseYixingFan } from '../src/utils/yixingReplay.js'

const require = createRequire(import.meta.url)
const { normalizeYixingRoomConfig } = require('../../server/utils/yixingRoomSettings.js')
class SettingsError extends Error {
  constructor(code, message) { super(message); this.code = code }
}

test('宜兴六个开关、四种局长和计时设置在前后端完整往返', () => {
  const keys = ['tips', 'count_tips', 'pointer_tips', 'tourist_limit', 'allow_spectator', 'seven_pairs']
  for (let flags = 0; flags < 64; flags++) {
    for (const rounds of [1, 2, 3, 4]) {
      for (const timers of [[0, 0], [20, 5], [1000, 100]]) {
        const form = loadYixingForm({ room_rule: 'yixing' })
        keys.forEach((key, i) => { form[key] = Boolean(flags & (1 << i)) })
        form.game_round = rounds
        ;[form.round_timer, form.step_timer] = timers
        const payload = buildYixingRoomPayload(form)
        const stored = normalizeYixingRoomConfig(payload.room_config, SettingsError)
        const restored = loadYixingForm({ room_rule: 'yixing' }, stored)
        for (const key of [...keys, 'game_round', 'round_timer', 'step_timer']) {
          assert.equal(restored[key], form[key], key)
        }
        assert.equal(stored.use_flowers, true)
      }
    }
  }
})

test('七小对默认关闭，非法布尔值和其他规则的配置不能混入宜兴', () => {
  assert.equal(loadYixingForm({ room_rule: 'yixing' }).seven_pairs, false)
  for (const invalid of [0, 1, 'true', null, [], {}]) {
    assert.throws(() => normalizeYixingRoomConfig({ detailed_config: { seven_pairs: invalid } }, SettingsError))
  }
  for (const raw of [
    { use_flowers: false }, { detailed_config: { flowers: false } },
    { detailed_config: { rule_version: 'bad' } }, { game_round: 0 }, { game_round: 5 },
    { game_round: 1.5 }, { round_timer: -1 }, { step_timer: 101 }, { tips: 'true' },
    { sub_rule: 'yixing/other' }, { duplicate_key: 'code' }, { unknown: true },
    { open_cuohe: true }, { claim_protection: true }, { tactical_call: true }, { tian_di_ren_he: true },
  ]) assert.throws(() => normalizeYixingRoomConfig(raw, SettingsError))
})

test('结算区分固定花数、重复底花扣除和倍数', () => {
  assert.deepEqual(parseYixingFan('YX|x|6|混一色'), { name: '混一色', value: '6花' })
  assert.deepEqual(parseYixingFan('YX|x|-1|重复底花'), { name: '重复底花', value: '-1花' })
  assert.deepEqual(parseYixingFan('YX|x|×3|抢杠包三家'), { name: '抢杠包三家', value: '×3' })
  assert.equal(parseYixingFan('invalid'), null)
})
