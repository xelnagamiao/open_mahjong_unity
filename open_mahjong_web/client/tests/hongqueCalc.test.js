import test from 'node:test'
import assert from 'node:assert/strict'

import {
  bestWinResult,
  allWinResults,
  isWinningHand,
  winningDecompositions,
  meldCandidateTiles,
  inferMeldKind,
  waitingTiles,
  waitingTilesAfterDiscards,
  calculateHongquePaili,
  hongqueHandShanten,
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
  assert.equal(result.fanTotal, 64)
  assert.equal(result.points, 1024)
  assert.deepEqual(new Set(Object.keys(fanMap(result))), new Set([
    '天和', '门清', '清一数', '全彩', '金龙', '彩虹', '全带幺', '清刻',
  ]))
})

test('hongque: heavenly win also counts menqing', () => {
  const hand = 'AX1 AY9 BX1 BY9 CX1 CY9 DX9 DY1 EX9 EY1 FX9 FY1 GX9 GY1'.split(' ')
  const result = bestWinResult(hand, [], {
    selfDraw: true,
    beforeFirstDiscard: true,
    wallEmpty: false,
  })
  assert.ok(result)
  const fans = fanMap(result)
  assert.equal(fans['天和'], 18)
  assert.equal(fans['门清'], 1)
  assert.equal(result.fanTotal, 51)
  assert.equal(result.points, 357)
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

test('hongque: 全带幺 accepts a 123 meld extended with 4', () => {
  const result = bestWinResult(
    [
      'BX1', 'BX2', 'BX3',
      'CX1', 'CX2', 'CX3',
      'DX7', 'DX8', 'DX9',
    ],
    [{ kind: 'sequence', tiles: ['AX1', 'AX2', 'AX3', 'AX4'] }],
    flags
  )
  assert.ok(result)
  assert.equal(fanMap(result)['全带幺'], 2)
  assert.ok(result.groups.some((group) => group.join(',') === 'AX1,AX2,AX3,AX4'))
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

test('hongque: same-flower and same-sequence fans are repeatable', () => {
  // AX1-3/BX1-3 与 AX7-9/BX7-9：两个双同顺；AX1-3/AX7-9 与 BX1-3/BX7-9：两个双同花。
  const hand = ['AX1', 'AX2', 'AX3', 'AX7', 'AX8', 'AX9', 'BX1', 'BX2', 'BX3', 'BX7', 'BX8', 'BX9']
  const result = bestWinResult(hand, [], flags)
  assert.ok(result)
  const map = fanMap(result)
  assert.equal(map['双同花'], 4)
  assert.equal(map['双同顺'], 4)
})

test('hongque: rainbow sequence still counts for 平和', () => {
  // 彩虹组本质是长顺子，不影响平和；该手牌最优拆解含彩虹组，仍应计平和。
  const hand = ['AY1', 'BY2', 'CY3', 'DY4', 'EY5', 'FY6', 'GY7', 'AY8', 'BY9', 'CY8', 'DY7', 'EY6']
  const result = bestWinResult(hand, [], flags)
  assert.ok(result)
  assert.equal(fanMap(result)['平和'], 1)
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

test('hongque: 双色 counts covered pure colours (1 12 2 pattern)', () => {
  // AX3 AY3 BX3 × 4：红(AX) + 红橙(AY 覆盖红/橙) + 橙(BX) → 覆盖 {红,橙}=2 → 双色
  const result = bestWinResult(
    ['AX3', 'AY3', 'BX3', 'AX4', 'AY4', 'BX4', 'AX5', 'AY5', 'BX5', 'AX6', 'AY6', 'BX6'],
    [],
    flags
  )
  assert.ok(result)
  const names = result.fans.map((fan) => fan.name)
  assert.ok(names.includes('双色'))
  assert.ok(!names.includes('三色'))
  // 七归一按 14 级独立计数：AX/AY/BX 各 4 张，不合并 → 不计七归一
  assert.ok(!names.includes('七归一'))
  assert.equal(result.fanTotal, 27)
  assert.equal(result.points, 216)
})

test('hongque: 七归一/九归一 count 14 levels independently', () => {
  // AX1..9 为同一级（9 张）→ 九归一；AY 不并入 AX
  const result = bestWinResult(
    ['AX1', 'AX2', 'AX3', 'AX4', 'AX5', 'AX6', 'AX7', 'AX8', 'AX9', 'AY1', 'AY2', 'AY3'],
    [],
    flags
  )
  assert.ok(result)
  const names = result.fans.map((fan) => fan.name)
  assert.ok(names.includes('九归一'))
  assert.ok(!names.includes('七归一'))
  // AX 覆盖红、AY 覆盖红+橙 → 双色（不是清一色）
  assert.ok(names.includes('双色'))
  assert.ok(!names.includes('清一色'))
})

test('hongque: 2 23 4 pattern is not 双色 (covers 3 pure colours)', () => {
  // BX(橙)+BY(橙黄 覆盖橙/黄)+DX(绿)+DY(绿青 覆盖绿/青) → 覆盖 4 色
  const result = bestWinResult(
    ['BX1', 'BX2', 'BX3', 'BY1', 'BY2', 'BY3', 'DX1', 'DX2', 'DX3', 'DY1', 'DY2', 'DY3'],
    [],
    flags
  )
  assert.ok(result)
  const names = result.fans.map((fan) => fan.name)
  assert.ok(!names.includes('双色'))
  assert.ok(!names.includes('三色'))
})

test('hongque: decomposition groups are ordered by number then colour', () => {
  // 彩虹 GY2 AY3 BY4 CY5 DY6 EY7 组内必须按数字 2..7 递增显示，
  // 不能按花色排成 AY3 BY4 CY5 DY6 EY7 GY2（旧实现会输出“红4 橙2 蓝8 紫6”式乱序）。
  const results = allWinResults(
    ['GY2', 'AY3', 'BY4', 'CY5', 'DY6', 'EY7', 'CX1', 'DX1', 'EX1', 'BX9', 'BY9', 'CX9'],
    [],
    flags
  )
  assert.ok(results.length >= 1)
  const hasOrderedRainbow = results.some((item) =>
    item.groups.some(
      (group) => JSON.stringify(group) === JSON.stringify(['GY2', 'AY3', 'BY4', 'CY5', 'DY6', 'EY7'])
    )
  )
  assert.ok(hasOrderedRainbow)
  // 组间按最小数字排序：数字 1 的刻子组排在彩虹（最小数字 2）之前。
  assert.deepEqual(results[0].groups[0], ['CX1', 'DX1', 'EX1'])
})

test('hongque: 四数 allows a missing slot in a four-term arithmetic frame', () => {
  const result = bestWinResult(
    'AX8 BX8 CX8 CY6 DX6 DX8 DX9 DY6 EX8 EX9 FX9 GX8'.split(' '),
    [],
    flags
  )
  assert.ok(result)
  const names = new Set(result.fans.map((fan) => fan.name))
  assert.ok(names.has('四数'))
  assert.ok(!names.has('三数'))
})

test('hongque: 三数 remains an exact arithmetic triple', () => {
  for (const numbers of [[1, 2, 3], [1, 4, 7], [3, 6, 9], [2, 5, 8]]) {
    const result = bestWinResult(numbers.map((number) => `AX${number}`), [], flags)
    assert.ok(result)
    const names = new Set(result.fans.map((fan) => fan.name))
    assert.ok(names.has('三数'))
    assert.ok(!names.has('四数'))
  }
})

test('hongque: already-winning hand still waits for unique extension tiles', () => {
  const hand = ['AX1', 'AX2', 'AX3']
  assert.equal(isWinningHand(hand, []), true)
  const waits = waitingTiles(hand, hand)
  assert.ok(waits.includes('AX4'))
  assert.ok(!waits.includes('AX1'))
  assert.ok(!waits.includes('AX2'))
  assert.ok(!waits.includes('AX3'))
  assert.equal(new Set(waits).size, waits.length)
})

test('hongque: discarded unique tile cannot be waited again', () => {
  const hand = ['AX1', 'AX2', 'AX3', 'AX4']
  const rows = waitingTilesAfterDiscards(hand, hand)
  const discardAx4 = rows.find((row) => row.discard === 'AX4')
  assert.ok(discardAx4)
  assert.ok(!discardAx4.waits.includes('AX4'))
  assert.ok(discardAx4.waits.includes('AX5') || isWinningHand(['AX1', 'AX2', 'AX3'], []))
})

test('hongque paili: winning 12-tile still lists unique extension waits', () => {
  const hand = 'AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8 CX9 DX1 DX2 DX3'.split(' ')
  const result = calculateHongquePaili(hand)
  assert.equal(result.mode, 'shanten')
  assert.equal(result.shanten, -1)
  assert.equal(result.is_hepai, true)
  assert.ok(result.total_accept > 0)
  assert.ok(!result.accept.some((item) => hand.includes(item.tile)))
})

test('hongque paili: 1-shanten hand lists improving tiles', () => {
  const hand = 'AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8 CX9 DY1 DY2 GY9'.split(' ')
  const result = calculateHongquePaili(hand)
  assert.equal(result.mode, 'shanten')
  assert.ok(result.shanten >= 1)
  assert.ok(result.total_accept > 0)
  assert.ok(result.accept.some((item) => item.tile === 'DY3'))
  assert.ok(!result.accept.some((item) => hand.includes(item.tile)))
})

test('hongque paili: 14-tile discard reports shanten and ukeire per cut', () => {
  const hand = 'AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8 GY9 DY1 DY2 EY5 FX3 GX8'.split(' ')
  const result = calculateHongquePaili(hand)
  assert.equal(result.mode, 'discard')
  assert.equal(result.discards.length, 14)
  assert.ok(result.discards.every((row) => typeof row.shanten === 'number'))
  const best = result.discards[0]
  assert.ok(best.shanten <= result.discards[result.discards.length - 1].shanten)
  if (best.shanten <= 2) assert.ok(best.total_accept >= 0)
})

test('hongque paili: 2-shanten hand lists improving tiles', () => {
  const hand = 'AX1 AX2 BX5 CX7 DY1 EY5 FY9 GX2 GY8 AY4 BY8 DX3'.split(' ')
  const result = calculateHongquePaili(hand)
  assert.equal(result.mode, 'shanten')
  assert.equal(result.shanten, 2)
  assert.ok(result.total_accept > 0)
  assert.ok(!result.accept.some((item) => hand.includes(item.tile)))
})

test('hongque paili: 3-shanten hand lists improving tiles', () => {
  const hand = 'GY6 FX6 CX7 CY6 CX5 FX7 DY3 FX4 GY5 DX2 DY6 EX8'.split(' ')
  const result = calculateHongquePaili(hand)
  assert.equal(result.mode, 'shanten')
  assert.equal(result.shanten, 3)
  assert.ok(result.total_accept > 0)
  assert.ok(!result.accept.some((item) => hand.includes(item.tile)))
})

test('hongque paili: 4-shanten hand lists improving tiles', () => {
  const hand = 'CX5 DY9 FY3 AY5 CY2 AX2 AX4 EY1 DX2 GY7 GX1 CX3'.split(' ')
  const result = calculateHongquePaili(hand)
  assert.equal(result.mode, 'shanten')
  assert.equal(result.shanten, 4)
  assert.ok(result.total_accept > 0)
  assert.ok(!result.accept.some((item) => hand.includes(item.tile)))
})

test('hongque paili: 5-shanten path still lists improving tiles when it appears', () => {
  // 12 张随机手在虹雀里几乎到不了 5 向听（实测 1500 副最高 4）。
  // 用一副 4 向听确认高向听分支有进张；5 向听走同一套换张搜索。
  const four = calculateHongquePaili('CX5 DY9 FY3 AY5 CY2 AX2 AX4 EY1 DX2 GY7 GX1 CX3'.split(' '))
  assert.equal(four.mode, 'shanten')
  assert.equal(four.shanten, 4)
  assert.ok(four.total_accept > 0)
  assert.equal(hongqueHandShanten('AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8 CX9 DY1 DY2 GY9'.split(' ')), 1)
})

test('hongque paili: ukeire timing across shanten levels', () => {
  const cases = [
    ['win12', 'AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8 CX9 DX1 DX2 DX3'],
    ['iishanten12', 'AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8 CX9 DY1 DY2 GY9'],
    ['two12', 'AX1 AX2 BX5 CX7 DY1 EY5 FY9 GX2 GY8 AY4 BY8 DX3'],
    ['three12', 'GY6 FX6 CX7 CY6 CX5 FX7 DY3 FX4 GY5 DX2 DY6 EX8'],
    ['four12', 'CX5 DY9 FY3 AY5 CY2 AX2 AX4 EY1 DX2 GY7 GX1 CX3'],
    ['discard14', 'AX1 AX2 AX3 BX4 BX5 BX6 CX7 CX8 GY9 DY1 DY2 EY5 FX3 GX8'],
  ]
  for (const [label, text] of cases) {
    const start = performance.now()
    const result = calculateHongquePaili(text.split(' '))
    const ms = performance.now() - start
    const shanten = result.mode === 'shanten' ? result.shanten : result.best_shanten
    const accepts = result.mode === 'shanten' ? result.total_accept : result.discards[0].total_accept
    console.log(`paili ${label}: shanten=${shanten} accepts=${accepts} ${ms.toFixed(1)}ms`)
    const limit = label === 'discard14' ? 5_000 : 2_000
    assert.ok(ms < limit, `${label} too slow: ${ms.toFixed(1)}ms`)
  }
})
