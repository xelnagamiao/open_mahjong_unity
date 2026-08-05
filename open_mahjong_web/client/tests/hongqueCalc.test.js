import test from 'node:test'
import assert from 'node:assert/strict'

import {
  bestWinResult,
  allWinResults,
  isWinningHand,
  winningDecompositions,
} from '../src/game2d/calc/hongque/index.ts'

const flags = {
  selfDraw: true,
  beforeFirstDiscard: false,
  wallEmpty: false,
}

function fanMap(result) {
  const map = {}
  for (const fan of result.fans) map[fan.name] = fan.total
  return map
}

test('hongque: minimal concealed win base * fan sum', () => {
  const result = bestWinResult(['AX1', 'AX2', 'AX3'], [], flags)
  assert.ok(result)
  assert.equal(result.base, 5)
  assert.deepEqual(fanMap(result), {
    清一色: 18,
    三数: 6,
    金龙: 6,
    全带幺: 2,
    清顺: 1,
    全纯色: 1,
    门清: 1,
    平和: 1,
  })
  assert.equal(result.fanTotal, 36)
  assert.equal(result.points, 180)
})

test('hongque: rulebook 1008 point example (all 14 colour levels of number 1)', () => {
  const hand = []
  for (const letter of 'ABCDEFG') {
    for (const half of 'XY') hand.push(`${letter}${half}1`)
  }
  const result = bestWinResult(hand, [], {
    selfDraw: true,
    beforeFirstDiscard: true,
    wallEmpty: false,
  })
  assert.ok(result)
  assert.equal(result.base, 16)
  assert.equal(result.fanTotal, 63)
  assert.equal(result.points, 1008)
  assert.deepEqual(new Set(Object.keys(fanMap(result))), new Set([
    '天和', '清一数', '全彩', '金龙', '彩虹', '全带幺', '清刻',
  ]))
})

test('hongque: exposed groups complete the win, no pair head', () => {
  const result = bestWinResult(
    ['AX1', 'AX2', 'AX3'],
    [{ kind: 'sequence', tiles: ['BX4', 'BX5', 'BX6'] }],
    flags
  )
  assert.ok(result)
  assert.deepEqual(result.pair, [])
  assert.equal(result.concealed, false)
  assert.equal(result.points, 57)
})

test('hongque: same-number pair outside every group is not a win', () => {
  const melds = [{ kind: 'sequence', tiles: ['AX1', 'AX2', 'AX3'] }]
  assert.equal(isWinningHand(['BX5', 'CY5'], melds), false)
  assert.deepEqual(winningDecompositions(['BX5', 'CY5'], melds), [])
})

test('hongque: all decompositions sorted best-first', () => {
  const results = allWinResults(['AX1', 'AX2', 'AX3'], [], flags)
  assert.ok(results.length >= 1)
  for (let i = 1; i < results.length; i++) {
    assert.ok(
      (results[i - 1].points > results[i].points) ||
      (results[i - 1].points === results[i].points && results[i - 1].fanTotal >= results[i].fanTotal)
    )
  }
})

test('hongque: duplicate tiles are rejected', () => {
  assert.equal(isWinningHand(['AX1', 'AX1', 'AX2'], []), false)
  assert.equal(bestWinResult(['AX1', 'AX1', 'AX2'], [], flags), null)
})
