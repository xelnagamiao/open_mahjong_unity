import test from 'node:test'
import assert from 'node:assert/strict'
import { allWinResults, bestWinResult } from '../src/game2d/calc/hongque/index.ts'

const flags = { selfDraw: true, beforeFirstDiscard: false, wallEmpty: false }
const codes = (colours, numbers) => colours.map((c, i) => `${'ABCDEFG'[Math.floor(c / 2)]}${c % 2 ? 'Y' : 'X'}${numbers[i]}`)
const flowers = groups => {
  const [result] = allWinResults([], groups.map(tiles => ({ tiles })), flags)
  assert.ok(result, 'Every supplied group must be legal')
  return result.fans.filter(fan => fan.name.endsWith('同花'))
}

test('reported mixed cross-boundary groups: 双同花 2 番, 6 points, without reordering inputs', () => {
  const hand = 'AY1 AY9 GY2 GY9 FY3 FY9'.split(' ')
  const melds = [{ tiles: 'CY1 DX1 DY1'.split(' ') }, { tiles: 'DY8 EY8 FY8'.split(' ') }]
  const before = structuredClone({ hand, melds })
  const result = bestWinResult(hand, melds, flags)
  assert.deepEqual(result.fans, [{ name: '双同花', value: 2, count: 1, total: 2 }])
  assert.deepEqual([result.base, result.fanTotal, result.points], [3, 2, 6])
  assert.deepEqual({ hand, melds }, before)
  assert.deepEqual(allWinResults(hand, melds, flags), [result])
})

for (let start = 0; start < 14; start++) for (const step of [1, 2])
  for (const reverse of [false, true]) for (const [count, name, value] of [[2, '双同花', 2], [3, '三同花', 6], [4, '四同花', 12]]) {
    test(`mixed types: start=${start}, step=${step}, reverse=${reverse}, count=${count}`, () => {
      const colours = [0, 1, 2].map(i => (start + step * i) % 14)
      const numbers = [[1, 2, 3], [4, 5, 6], [3, 4, 5]]
      const groups = [codes(colours, [9, 9, 9]), ...numbers.slice(0, count - 1).map(ns => codes(colours, reverse ? ns.toReversed() : ns))]
      assert.equal(new Set(groups.flat()).size, 3 * count)
      const expected = [{ name, value, count: 1, total: value }]
      assert.deepEqual(flowers(groups), expected)
      assert.deepEqual(flowers(groups.toReversed().map(group => group.toReversed())), expected)
    })
  }

for (let start = 0; start < 14; start++) for (let shift = 1; shift < 7; shift++) {
  test(`complete colour cycle: start=${start}, shift=${shift}`, () => {
    const colours = Array.from({ length: 7 }, (_, i) => (start + 2 * i) % 14)
    const rotated = [...colours.slice(shift), ...colours.slice(0, shift)]
    const groups = [codes(colours, [1, 2, 3, 4, 5, 6, 7]), codes(rotated, [1, 2, 3, 4, 5, 6, 7])]
    assert.equal(new Set(groups.flat()).size, 14)
    assert.deepEqual(flowers(groups), [{ name: '双同花', value: 2, count: 1, total: 2 }])
  })
}

test('equal colour sets with different multiplicity, lengths, or absolute colours must not match', () => {
  const numbers = [1, 2, 3, 4, 5, 6, 7, 8]
  assert.deepEqual(flowers([codes(numbers.map(n => (n - 1) * 2 % 14), numbers), codes(numbers.map(n => n * 2 % 14), numbers)]), [])
  assert.deepEqual(flowers(['AX1 AX2 AX3'.split(' '), 'AX4 AX5 AX6 AX7'.split(' ')]), [])
  assert.deepEqual(flowers(['AX1 AY2 BX3'.split(' '), 'BX4 BY5 CX6'.split(' ')]), [])
})

test('independent pairs repeat without counting a group twice', () => {
  const groups = ['FY1 GY2 AY3', 'AY9 FY9 GY9', 'GX2 GY3 AX4', 'AX8 GX8 GY8'].map(s => s.split(' '))
  assert.equal(new Set(groups.flat()).size, 12)
  assert.deepEqual(flowers(groups), [{ name: '双同花', value: 2, count: 2, total: 4 }])
})
