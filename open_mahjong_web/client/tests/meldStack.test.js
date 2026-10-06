import test from 'node:test'
import assert from 'node:assert/strict'
import { buildMeldStacks, hasMeldStacks, isMeldStack } from '../src/game2d/lib/meldStack.js'
import { buildSettlementMeldGroups } from '../src/game2d/lib/settlementHand.js'

test('101/102 are the actual reserved signs, separate from old signs and tile ids', () => {
  assert.equal(isMeldStack(101), true)
  assert.equal(isMeldStack(102), true)
  for (const sign of [0, 1, 2, 3, 4, 100, 103]) assert.equal(isMeldStack(sign), false)
  assert.equal(hasMeldStacks([0, 101, 1, 102]), false)
})

test('mixed consecutive stacks retain original-order anchors and layer counts', () => {
  const mask = [0, 11, 101, 12, 102, 13, 1, 21, 102, 22, 4, 0, 101, 23]
  const copy = [...mask]
  const stacks = buildMeldStacks(mask)
  assert.deepEqual(stacks.map(({ anchorIndex, layer, faceDown }) => [anchorIndex, layer, faceDown]),
    [[0, 1, false], [0, 2, true], [3, 1, true], [3, 2, false]])
  assert.deepEqual(stacks.map(({ pairIndex }) => pairIndex), [1, 2, 4, 6])
  assert.deepEqual(mask, copy)
})

for (const sign of [0, 1, 2, 3]) test(`101/102 can attach to old base sign ${sign}`, () => {
  const stacks = buildMeldStacks([sign, 11, 101, 12, 102, 13])
  assert.deepEqual(stacks.map(({ anchorIndex, layer }) => [anchorIndex, layer]), [[0, 1], [0, 2]])
})

test('malformed and orphan stacks do not create ordinary slots or cross-meld anchors', () => {
  for (const mask of [null, [], [101, 11, 102, 12], [0, 11, 101],
    [0, -1, 101, 12], [0, 11, 99, 12, 101, 13]]) assert.deepEqual(buildMeldStacks(mask), [])
  assert.equal(buildMeldStacks([0, 11, 101, -1, 102, 12])[0].layer, 1)
  assert.deepEqual(buildSettlementMeldGroups([[101, 12], [0, 11]])[0].tiles.map(({ tile }) => tile), [11])
})

test('stacked concealed unknown tiles show backs without revealing ids', () => {
  const [group] = buildSettlementMeldGroups([[2, 0, 102, 0]])
  assert.equal(group.tiles[0].faceDown, true)
  assert.deepEqual(group.tiles[0].stackedTiles, [{ tile: 0, faceDown: true, layer: 1, onAddedKong: false }])
})

test('settlement stacks reuse base columns and preserve claimed orientation', () => {
  const [group] = buildSettlementMeldGroups([[0, 11, 101, 12, 102, 13, 1, 21, 102, 22]])
  assert.equal(group.tiles.length, 2)
  assert.equal(group.tiles[1].sideways, true)
  assert.deepEqual(group.tiles[0].stackedTiles.map(({ layer, faceDown }) => [layer, faceDown]), [[1, false], [2, true]])
  assert.equal(group.tiles[1].stackedTiles[0].layer, 1)
})

test('new stacks on sign 3 preserve the old added-kong attachment', () => {
  const [group] = buildSettlementMeldGroups([[0, 25, 0, 25, 3, 125, 101, 11, 102, 12, 1, 25]])
  assert.equal(group.tiles.length, 3)
  assert.equal(group.tiles[2].stackedTile, 125)
  assert.deepEqual(group.tiles[2].stackedTiles.map(({ onAddedKong, layer }) => [onAddedKong, layer]), [[true, 1], [true, 2]])
})

test('hidden stack stays hidden over the revealed middle tile of a concealed kong', () => {
  const [group] = buildSettlementMeldGroups([[2, 47, 2, 47, 102, 11, 2, 47, 2, 47]])
  assert.deepEqual(group.tiles.map(({ faceDown }) => faceDown), [true, false, false, true])
  assert.equal(group.tiles[1].stackedTiles[0].faceDown, true)
})
