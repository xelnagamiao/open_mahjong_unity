import test from 'node:test'
import assert from 'node:assert/strict'
import { legalGroupUniverse, parseTile } from '../src/game2d/calc/hongque/index.ts'
import { meldDisplayOrder, handDisplayOrder, sortHandCodes } from '../src/game2d/calc/hongque/displayOrder.ts'

test('every legal Hongque meld has continuous adjacent colours and numbers', () => {
  const groups = legalGroupUniverse()
  assert.equal(groups.length, 4787)
  for (const group of groups) {
    const source = [...group]
    const actual = meldDisplayOrder(group)
    const tiles = actual.map(parseTile)
    const dc = (tiles[1].colour - tiles[0].colour + 14) % 14
    const dn = tiles[1].number - tiles[0].number
    assert.ok([0,1,2,12,13].includes(dc))
    assert.ok(Math.abs(dn) <= 4 && (dc !== 0 || dn !== 0))
    for (let i=1;i<tiles.length;i++) {
      assert.equal((tiles[i].colour-tiles[i-1].colour+14)%14,dc)
      assert.equal(tiles[i].number-tiles[i-1].number,dn)
    }
    assert.deepEqual(group, source)
    assert.deepEqual([...actual].sort(), [...source].sort())
    assert.deepEqual(meldDisplayOrder(actual),actual)
    assert.deepEqual(meldDisplayOrder([...source].reverse()),actual)
    assert.deepEqual(handDisplayOrder(source),[...source].sort())
  }
})

test('melds wrap but hands retain table ordering, without moving equal-colour numbers backwards', () => {
  assert.deepEqual(meldDisplayOrder(['AX5','GX5','GY5']),['AX5','GY5','GX5'])
  const hand=['AX6','FX2','GY4','AX1','GX5']
  assert.equal(sortHandCodes(hand),hand)
  assert.deepEqual(hand,['AX1','AX6','FX2','GX5','GY4'])
  assert.deepEqual(handDisplayOrder(hand),hand)
  assert.deepEqual(handDisplayOrder([]),[])
  assert.deepEqual(meldDisplayOrder(['AX1','CX7']),['AX1','CX7'])
})
