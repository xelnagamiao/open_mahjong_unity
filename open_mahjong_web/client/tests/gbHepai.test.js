import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

import { hepaiCheck } from '../src/game2d/calc/guobiao/gbHepai.ts'

test('mixed shifted chows: gap-step (隔步 3-5-7) does NOT score sansesanbugao', () => {
  // 567m+789m+567p+345s+88p 自摸 5s — 起始 3/5/7：国标三色三步高仅依次递增一位，不计
  const result = hepaiCheck(
    [15, 16, 17, 17, 18, 19, 25, 26, 27, 28, 28, 33, 34, 35],
    [],
    ['自摸'],
    35,
    false,
  )
  assert.ok(
    !result.fanNames.some((n) => n.includes('三色三步高')),
    `隔步不应计三色三步高, got ${JSON.stringify(result)}`,
  )
  assert.ok(result.fan < 8)
})

test('open 111+999 plus 2345678 must not score 九莲宝灯', () => {
  const openMelds = ['k11', 'k19']
  const waits = [
    { hand: [12, 13, 14, 15, 16, 17, 18, 12], tile: 12, expectedFan: 27 },
    { hand: [12, 13, 14, 15, 16, 17, 18, 15], tile: 15, expectedFan: 26 },
    { hand: [12, 13, 14, 15, 16, 17, 18, 18], tile: 18, expectedFan: 27 },
  ]
  for (const { hand, tile, expectedFan } of waits) {
    const result = hepaiCheck(hand, openMelds, ['点和'], tile, false)
    assert.ok(
      !result.fanNames.some((name) => name.includes('九莲宝灯')),
      `open nine-gates shape must not score 九莲宝灯, got ${JSON.stringify(result)}`,
    )
    assert.equal(result.fan, expectedFan, `unexpected fan for win ${tile}: ${JSON.stringify(result)}`)
  }

  const closed = hepaiCheck(
    [11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 19, 19, 19, 15],
    [],
    ['点和'],
    15,
    false,
  )
  assert.ok(
    closed.fanNames.some((name) => name.includes('九莲宝灯')),
    `closed nine-gates must still score, got ${JSON.stringify(closed)}`,
  )
})

test('mixed shifted chows: consecutive-step (连步) still scores', () => {
  // rulebook-ish: chi 234p + chi 456m + 789m + 234s + 66s ron 2s
  const result = hepaiCheck(
    [17, 18, 19, 32, 33, 34, 36, 36],
    ['s22', 's14'],
    ['点和'],
    32,
    false,
  )
  assert.ok(
    result.fanNames.some((n) => n.includes('三色三步高')),
    `expected 三色三步高, got ${JSON.stringify(result)}`,
  )
})

test('all annotated Python scoring examples stay in parity', () => {
  const pythonReference = fileURLToPath(
    new URL(
      '../../../open_mahjong_server/server/game_calculation/guobiao_hepai_check.py',
      import.meta.url,
    ),
  )
  const source = readFileSync(pythonReference, 'utf8')
  const examplePattern = /test_save\s*=\s*(\[\[.*?\]\])\s*(?:#\s*)?(\d+)\s*(?:#.*)?$/
  const examples = source.split(/\r?\n/).flatMap((line) => {
    const match = line.match(examplePattern)
    return match ? [{ input: JSON.parse(match[1]), expectedFan: Number(match[2]) }] : []
  })

  assert.equal(examples.length, 237, 'Python reference example count changed')

  const failures = []
  for (const { input, expectedFan } of examples) {
    const [combinations, hand, getTile, wayToHepai] = input
    const result = hepaiCheck(hand, combinations, wayToHepai, getTile)
    if (result.fan !== expectedFan) {
      failures.push({ input, expectedFan, actualFan: result.fan, fanNames: result.fanNames })
    }
  }

  assert.deepEqual(failures, [])
})
