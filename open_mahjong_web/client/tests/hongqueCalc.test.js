import test from 'node:test'
import assert from 'node:assert/strict'

import {
  bestWinResult,
  allWinResults,
  isWinningHand,
  winningDecompositions,
  meldCandidateTiles,
  inferMeldKind,
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

test('hongque: meld candidate tiles only extend into a legal group', () => {
  const afterFirst = meldCandidateTiles(['AX1'], new Set())
  assert.ok(afterFirst.has('AX2'))
  assert.ok(afterFirst.has('AX3'))
  assert.ok(afterFirst.has('AY1'))
  assert.ok(afterFirst.has('BX1'))
  assert.ok(!afterFirst.has('CY1')) // CY1 不能与 AX1 组成 3 张合法牌组

  const afterTwo = meldCandidateTiles(['AX1', 'AX2'], new Set())
  assert.deepEqual([...afterTwo].sort(), ['AX1', 'AX2', 'AX3'])
  assert.ok(!afterTwo.has('AY3'))

  // 已使用的牌不再作为候选
  const used = new Set(['AX3'])
  const withUsed = meldCandidateTiles(['AX1', 'AX2'], used)
  assert.ok(!withUsed.has('AX3'))
})

test('hongque: meld kind is inferred without open/concealed distinction', () => {
  assert.equal(inferMeldKind(['AX1', 'AX2', 'AX3']).label, '顺子')
  assert.equal(inferMeldKind(['BX3', 'BY3', 'CX3']).label, '刻子')
  assert.equal(inferMeldKind(['AX1', 'AY1', 'BX1', 'BY1', 'CX1', 'CY1', 'DX1', 'DY1', 'EX1', 'EY1', 'FX1', 'FY1', 'GX1', 'GY1']).label, '彩虹')
  assert.equal(inferMeldKind(['AX1', 'AX2', 'AY4']), null)
})
