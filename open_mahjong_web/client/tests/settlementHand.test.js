import test from 'node:test'
import assert from 'node:assert/strict'
import {
  mmcrSettlementSortKey,
  salasasaSettlementSortKey,
  splitSettlementHand,
  splitWinningTileFromRevealedHand,
} from '../src/game2d/lib/settlementHand.js'

test('ron seven pairs keeps the unmatched East and renders the winning East separately', () => {
  const preWinHand = [13, 13, 19, 19, 21, 21, 25, 25, 38, 38, 47, 47, 41]
  const closed = splitSettlementHand(preWinHand, 41, 0, salasasaSettlementSortKey)

  assert.equal(closed.length, 13)
  assert.equal(closed.filter((tile) => tile === 41).length, 1)
})

test('tsumo removes exactly one separately rendered winning tile', () => {
  const completeHand = [13, 13, 19, 19, 21, 21, 25, 25, 38, 38, 47, 47, 41, 41]
  const closed = splitSettlementHand(completeHand, 41, 0, salasasaSettlementSortKey)

  assert.equal(closed.length, 13)
  assert.equal(closed.filter((tile) => tile === 41).length, 1)
})

test('open ron keeps the complete pre-win concealed hand', () => {
  const preWinHand = [11, 12, 13, 21, 21, 31, 31, 41, 41, 45]
  const closed = splitSettlementHand(preWinHand, 45, 1, salasasaSettlementSortKey)

  assert.equal(closed.length, 10)
  assert.equal(closed.filter((tile) => tile === 45).length, 1)
})

test('complete ron separates the discarded 8 from the 67 wait', () => {
  const completeHand = [11, 11, 11, 12, 13, 14, 15, 15, 15, 16, 17, 21, 21, 18]
  const closed = splitSettlementHand(completeHand, 18, 0, salasasaSettlementSortKey)

  assert.equal(closed.length, 13)
  assert.equal(closed.includes(18), false)
  assert.deepEqual(closed.slice(9, 11), [16, 17])
  assert.deepEqual(
    splitWinningTileFromRevealedHand(completeHand, 18, 13)
      .sort((a, b) => salasasaSettlementSortKey(a) - salasasaSettlementSortKey(b)),
    closed,
  )
})

test('settlement sorting is Man, Pin, Sou, winds, then dragons', () => {
  const salasasaTiles = [47, 31, 45, 11, 44, 21, 41, 46, 43, 42]
  assert.deepEqual(
    salasasaTiles.sort((a, b) => salasasaSettlementSortKey(a) - salasasaSettlementSortKey(b)),
    [11, 21, 31, 41, 42, 43, 44, 45, 46, 47],
  )

  const mmcrTiles = [0xa7, 0xc1, 0xa5, 0x41, 0xa4, 0x61, 0xa1, 0xa6, 0xa3, 0xa2]
  assert.deepEqual(
    mmcrTiles.sort((a, b) => mmcrSettlementSortKey(a) - mmcrSettlementSortKey(b)),
    [0x41, 0x61, 0xc1, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7],
  )
})
