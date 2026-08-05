/**
 * Hongque scoring regression tests, ported from the authoritative Python
 * test_hongque_scoring.py and the rulebook v1.6 examples.
 *
 * Run: node --test server/calc/hongque.test.js
 */

'use strict'

const { test } = require('node:test')
const assert = require('node:assert/strict')
const hongque = require('./hongque')

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

test('rulebook: base times fan sum for a minimal concealed win', () => {
  const result = hongque.bestWinResult(['AX1', 'AX2', 'AX3'], [], flags)
  assert.ok(result)
  assert.equal(result.base, 5) // 3 base + 2 concealed
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

test('low-scoring open hand matches the authoritative fan result', () => {
  const result = hongque.bestWinResult(
    ['DY7', 'EY4', 'FY1'],
    [
      { kind: 'sequence', tiles: ['DX3', 'DX4', 'DX5'] },
      { kind: 'triplet', tiles: ['BX3', 'BY3', 'CX3'] },
      { kind: 'triplet', tiles: ['EX1', 'EY1', 'FX1'] },
    ],
    { selfDraw: false, beforeFirstDiscard: false, wallEmpty: false }
  )
  assert.ok(result)
   assert.equal(result.points, 3)
   assert.deepEqual(fanMap(result), { 清顺: 1 })
})

test('rulebook 1008 point example: all 14 colour levels of number 1', () => {
  const hand = []
  for (const letter of 'ABCDEFG') {
    for (const half of 'XY') hand.push(`${letter}${half}1`)
  }
  const result = hongque.bestWinResult(hand, [], {
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

test('exposed groups with concealed group complete the win', () => {
  const result = hongque.bestWinResult(
    ['AX1', 'AX2', 'AX3'],
    [{ kind: 'sequence', tiles: ['BX4', 'BX5', 'BX6'] }],
    flags
  )
  assert.ok(result)
  assert.deepEqual(result.pair, [])
  assert.deepEqual(
    result.groups.map((group) => [...group].sort()).sort(),
    [['AX1', 'AX2', 'AX3'], ['BX4', 'BX5', 'BX6']].map((group) => [...group].sort())
  )
  assert.equal(result.concealed, false)
})

test('same-number pair outside every group is not a win', () => {
  const melds = [{ kind: 'sequence', tiles: ['AX1', 'AX2', 'AX3'] }]
  assert.equal(hongque.isWinningHand(['BX5', 'CY5'], melds), false)
  assert.deepEqual(hongque.winningDecompositions(['BX5', 'CY5'], melds), [])
})

test('decompose returns every winning partition sorted best-first', () => {
  const results = hongque.allWinResults(['AX1', 'AX2', 'AX3'], [], flags)
  assert.ok(results.length >= 1)
  for (let i = 1; i < results.length; i++) {
    const prev = results[i - 1]
    const curr = results[i]
    assert.ok(
      (prev.points > curr.points) ||
      (prev.points === curr.points && prev.fanTotal >= curr.fanTotal)
    )
  }
})

test('duplicate tiles are rejected', () => {
  assert.equal(hongque.isWinningHand(['AX1', 'AX1', 'AX2'], []), false)
  assert.equal(hongque.bestWinResult(['AX1', 'AX1', 'AX2'], [], flags), null)
})
