import test from 'node:test'
import assert from 'node:assert/strict'
import { challengeQuestion, newQuestion, candidates, correctAnswer, speedPoints } from '../../shared/menchin.mjs'
import { solveHand } from '../src/utils/scoring/handShapes.ts'

test('single-suit questions have exactly the waits found by the platform hand solver', () => {
  const suits = new Set()
  const waitCounts = new Set()
  for (let group = 0; group < 60; group++) {
    const seen = new Set()
    for (let i = 0; i < 10; i++) {
      const { hand, waits } = newQuestion(group % 3 + 1, seen)
      assert.equal(hand.length, 13)
      assert.equal(seen.size, i + 1)
      assert.deepEqual(hand, [...hand].sort((a, b) => a - b))
      const suit = Math.floor(hand[0] / 10)
      suits.add(suit)
      assert.ok(hand.every(tile => Math.floor(tile / 10) === suit && hand.filter(t => t === tile).length <= 4))
      const expected = candidates(hand).filter(tile => hand.filter(t => t === tile).length < 4 &&
        solveHand([...hand, tile], [], { riichiSevenPairs: true }).length > 0)
      assert.deepEqual(waits, expected)
      assert.ok(waits.length > 0)
      waitCounts.add(waits.length)
      assert.ok(correctAnswer([...waits].reverse(), waits))
      assert.ok(!correctAnswer(waits.slice(1), waits))
    }
  }
  assert.equal(suits.size, 3)
  assert.ok(waitCounts.size >= 3)
})

test('four challenge levels match the platform solver, including undealt waits and shuffled multi-waits', t => {
  let seed = 907
  t.mock.method(Math, 'random', () => {
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0
    return seed / 0x100000000
  })
  for (const level of [1, 2, 3, 4]) {
    const seen = new Set()
    let noWait = false, shuffled = false
    for (let i = 0; i < 150; i++) {
      const { hand, waits } = challengeQuestion(level, seen)
      assert.equal(seen.size, i + 1)
      assert.equal(hand.length, 13)
      const expected = candidates(hand).filter(tile => hand.filter(t => t === tile).length < 4 &&
        solveHand([...hand, tile], [], { riichiSevenPairs: true }).length > 0)
      assert.deepEqual(waits, expected)
      if (waits.length === 0) noWait = true
      if (level >= 3) assert.ok(waits.length >= 3)
      if (hand.some((tile, j) => j > 0 && tile < hand[j - 1])) shuffled = true
    }
    assert.equal(noWait, level === 2)
    assert.equal(shuffled, level === 4)
  }
})

test('speed scoring counts remaining tenths, the difficulty multiplier and deadline bounds', () => {
  assert.equal(speedPoints(1, 20200), 200)
  assert.equal(speedPoints(2, 13700), 137)
  assert.equal(speedPoints(3, 13700), 274)
  assert.equal(speedPoints(4, 13700), 411)
  assert.equal(speedPoints(1, 1), 1)
  assert.equal(speedPoints(1, 101), 2)
  assert.equal(speedPoints(4, 0), 0)
  assert.equal(speedPoints(4, -1000), 0)
})
