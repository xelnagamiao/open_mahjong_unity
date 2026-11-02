import test from 'node:test'
import assert from 'node:assert/strict'
import { bestWinResult } from '../src/game2d/calc/hongque/index.ts'

const flags = { selfDraw: false, beforeFirstDiscard: false, wallEmpty: false }
const cases = [
  [['AX1 AX5 AX9'], true],
  [['AX5 BX5 CX5'], true],
  [['AX1 AX2 AX3', 'BY7 BY8 BY9'], true],
  [['AX1 AX2 AX3', 'BY7 BY8 BY9', 'CX4 CX5 CX6'], true],
  [['AX1 AX2 AX3', 'BX1 BX2 BX3', 'CY7 CY8 CY9', 'DY7 DY8 DY9'], true],
  [['AX1 AX2 AX3', 'BX1 BX2 BX3', 'CY7 CY8 CY9'], false],
  [['AX1 AY1 BX1', 'BY1 CY1 DY1', 'AX9 AY9 BX9 BY9 CX9 CY9'], false],
  [['AX1 AX2 AX3', 'BY6 BY7 BY8 BY9'], false],
  [['AX1 AX2 AX3', 'BY7 BY8 BY9', 'CX5 CX6 CX7'], false],
]
for (const [groups, mirror] of cases) {
  test(`v1.7 mirror respects group boundaries: ${groups}`, () => {
    const result = bestWinResult([], groups.map(group => ({ tiles: group.split(' ') })), flags)
    assert.ok(result)
    assert.equal(result.fans.find(fan => fan.name === '镜像')?.total ?? 0, mirror ? 2 : 0)
  })
}
for (const [count, name, value] of [[2, '双同顺', 1], [3, '三同顺', 3], [4, '四同顺', 6]]) {
  test(`v1.7 ${name} is ${value} fan without lower tiers`, () => {
    const melds = [...'ABCD'].slice(0, count).map(colour => ({tiles: [1, 2, 3].map(n => `${colour}X${n}`)}))
    const result = bestWinResult([], melds, flags)
    assert.deepEqual(result.fans.filter(fan => ['双同顺', '三同顺', '四同顺'].includes(fan.name)),
      [{ name, value, count: 1, total: value }])
  })
}
