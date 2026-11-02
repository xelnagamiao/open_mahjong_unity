import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { predictWinResults } from '../src/game2d/calc/hongque/prediction.ts'

// The same authoritative corpus is consumed by LocalHintValidation.Hongque.
const fixturePath = process.env.HONGQUE_PARITY_FIXTURE
  ?? new URL('../../../open_mahjong_unity/Assets/Tests/Fixtures/LocalHints/hongque-parity.json', import.meta.url)
const rows = JSON.parse(readFileSync(fixturePath, 'utf8'))
for (const [index, row] of rows.entries()) {
  test(`v1.7 Python/TypeScript/C# score parity ${index}`, () => {
    const c = row.context
    const hand = c.self_draw ? row.hand : [...row.hand, c.tile]
    const [score] = predictWinResults(hand, row.melds.map(tiles => ({ tiles })), {
      selfDraw: c.self_draw, beforeFirstDiscard: c.before_first_discard, wallEmpty: c.wall_empty,
    })
    const actual = score ? { base: score.base, fan_total: score.fanTotal, points: score.points } : null
    assert.deepEqual(actual, row.expected)
  })
}
