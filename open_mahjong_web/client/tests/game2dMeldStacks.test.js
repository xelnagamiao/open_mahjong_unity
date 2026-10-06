import test from 'node:test'
import assert from 'node:assert/strict'
import { Assets, Container, Texture } from 'pixi.js'
import { Hand } from '../src/game2d/game/scene/Hand.ts'
import { Tile } from '../src/game2d/game/scene/Tile.ts'
import { TILE_WIDTH, TILE_HEIGHT } from '../src/game2d/game/scene/constants.ts'
import { SalasasaGameAdapter, salasasaTileToMmcr } from '../src/game2d/salasasa/gameAdapter.ts'

for (const tile of [11, 12, 13, 21, 22, 23, 25, 125]) Assets.cache.set(`regular-${tile}`, Texture.WHITE)

for (const direction of [0, 1, 2, 3]) test(`101/102 inherit base rotation without extending meld width at seat ${direction}`, (t) => {
  const parent = new Container()
  const plain = new Hand(direction, parent)
  const stacked = new Hand(direction, parent)
  t.after(() => {
    for (const hand of [plain, stacked]) for (const tile of hand.leftList) tile.destroy({ children: true })
    parent.destroy({ children: true })
  })
  plain.addMeld('pung', 11, { physicalMask: [0, 11, 1, 21, 0, 23] })
  stacked.addMeld('pung', 11, { physicalMask: [0, 11, 101, 12, 102, 13, 1, 21, 102, 22, 0, 23] })
  assert.equal(stacked.leftListLength, plain.leftListLength)
  assert.deepEqual(stacked.leftList.slice(0, 3).map(tile => [tile.pos, tile.posy, tile.rotation]),
    plain.leftList.map(tile => [tile.pos, tile.posy, tile.rotation]))
  const [upright, sideways, , first, second, third] = stacked.leftList
  assert.equal(first.pos, upright.pos)
  assert.equal(second.pos, upright.pos)
  assert.equal(first.posy, upright.posy - TILE_HEIGHT)
  assert.equal(second.posy, upright.posy - 2 * TILE_HEIGHT)
  assert.equal(third.pos, sideways.pos)
  assert.equal(third.posy, sideways.posy - TILE_WIDTH)
  assert.equal(first.meldStackRotation, 0)
  assert.equal(third.meldStackRotation, -Math.PI / 2)
  assert.equal(first.shown, true)
  assert.equal(second.shown, false)
  assert.equal(third.shown, false)
  const movement = Tile.prototype.generalMove
  const rotations = new Map()
  Tile.prototype.generalMove = function(target, x, y, rotation) {
    rotations.set(this, rotation)
    return Promise.resolve()
  }
  try {
    stacked.updateDisplay()
    assert.equal(rotations.get(first), 0, 'nonzero height must not force upright 101 to become sideways')
    assert.equal(rotations.get(third), -Math.PI / 2)
  } finally { Tile.prototype.generalMove = movement }
})

test('stacks on sign 3 follow the old added-kong tile', (t) => {
  const parent = new Container(), hand = new Hand(0, parent)
  t.after(() => { for (const tile of hand.leftList) tile.destroy({ children: true }); parent.destroy({ children: true }) })
  hand.addMeld('kong', 25, { physicalMask: [0, 25, 0, 25, 3, 125, 101, 11, 102, 12, 1, 25] })
  assert.equal(hand.leftList.length, 6)
  const added = hand.leftList[3]
  assert.equal(hand.leftList[4].pos, added.pos)
  assert.equal(hand.leftList[4].posy, added.posy - TILE_WIDTH)
  assert.equal(hand.leftList[5].posy, added.posy - 2 * TILE_WIDTH)
  assert.equal(hand.leftListLength, TILE_WIDTH * 2 + TILE_HEIGHT)
})

test('empty and orphan reserve stacks cannot use anchors from the preceding meld', (t) => {
  const parent = new Container(), hand = new Hand(0, parent)
  t.after(() => { for (const tile of hand.leftList) tile.destroy({ children: true }); parent.destroy({ children: true }) })
  hand.addMeld('pung', 11, { physicalMask: [0, 11, 0, 11, 1, 11] })
  const before = hand.leftListLength
  hand.addMeld('pung', 12, { physicalMask: [101, 12, 4, 0, 102, 13] })
  assert.equal(hand.leftListLength, before)
  assert.equal(hand.leftList.length, 3)
})

test('snapshot adaptation keeps 101/102 in the display mask without shifting the claimed source', () => {
  const adapter = new SalasasaGameAdapter(100)
  const mask = [0, 11, 101, 12, 102, 13, 1, 11, 0, 11]
  const snapshot = adapter.accept({ type: 'gamestate/guobiao/game_start', game_info: {
    room_id: 1, gamestate_id: 'spare-signs', current_player_index: 0, action_tick: 1,
    max_round: 16, tile_count: 80, current_round: 1, step_time: 5, round_time: 60,
    room_type: 'custom', room_rule: 'guobiao', players_info: [0, 1, 2, 3].map(seat => ({
      player_index: seat, original_player_index: seat, user_id: 100 + seat, username: `p${seat}`,
      hand_tiles_count: 10, hand_tiles: [], discard_tiles: [], score: 0,
      combination_tiles: seat === 0 ? ['k11'] : [], combination_mask: seat === 0 ? [mask] : [],
    })),
  } }).snapshot
  const meld = snapshot.seats[0].melds[0]
  assert.equal(meld.meld_from_rel, 2, 'spare stacks are not base tile positions')
  assert.deepEqual(meld.physical_mask, mask.map((value, index) => index % 2 ? salasasaTileToMmcr(value) : value))
})
