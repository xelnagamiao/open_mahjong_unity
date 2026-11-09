import test from 'node:test'
import assert from 'node:assert/strict'
import { Assets, Container, Texture } from 'pixi.js'
import { Hand } from '../src/game2d/game/scene/Hand.ts'
import { RiverMatchHighlight } from '../src/game2d/game/scene/RiverMatchHighlight.ts'
import { Tile, TILE_MATCH_TINT } from '../src/game2d/game/scene/Tile.ts'
import { salasasaTileToMmcr } from '../src/game2d/salasasa/gameAdapter.ts'

for (const face of [11, 12, 13, 15, 25, 35, 'Back']) Assets.cache.set(`regular-${face}`, Texture.WHITE)

const man5 = salasasaTileToMmcr(15), pin5 = salasasaTileToMmcr(25)
const highlighted = tile => tile.children[0].tint === TILE_MATCH_TINT

function table(t) {
  const parent = new Container(), highlight = new RiverMatchHighlight()
  const rivers = Array.from({ length: 4 }, () => ({ tileList: [], onTileRegistered: null }))
  const hands = Array.from({ length: 4 }, (_, seat) => new Hand(seat, parent, null, null, false, highlight))
  highlight.bind(rivers, hands)
  const extra = []
  t.after(() => {
    for (const hand of hands) {
      for (const tile of [...hand.leftList, ...hand.rightList]) if (!tile.destroyed) tile.destroy({ children: true })
    }
    for (const tile of extra) if (!tile.destroyed) tile.destroy({ children: true })
    parent.destroy({ children: true })
  })
  function discard(seat, tid) {
    const tile = new Tile(tid)
    rivers[seat].tileList.push(tile)
    rivers[seat].onTileRegistered(tile)
    extra.push(tile)
    return tile
  }
  return { parent, highlight, rivers, hands, discard }
}

test('hovering a hand tile highlights the same tile in all four rivers and melds, and leaving clears both', t => {
  const { hands, discard } = table(t)
  const rivers = hands.map((_, seat) => discard(seat, man5))
  for (const hand of hands) hand.addMeld('pung', man5, { meldFromRel: 1 })
  const other = discard(1, pin5)
  const hovered = new Tile(man5)
  hands[0].addRightList(hovered)
  hovered.emit('pointerover', {})
  for (const tile of [...rivers, ...hands.flatMap(hand => hand.leftList)]) assert.equal(highlighted(tile), true)
  assert.equal(highlighted(other), false)
  hovered.emit('pointerout', {})
  for (const tile of [...rivers, ...hands.flatMap(hand => hand.leftList)]) assert.equal(highlighted(tile), false)
})

test('new added-kong tiles, stacked physical melds and public kong tiles inherit an active highlight', t => {
  const { hands, highlight } = table(t)
  highlight.highlight(man5)
  hands[1].addMeld('kong', man5, { meldFromRel: 5 })
  hands[2].addMeld('pung', man5, { physicalMask: [0, man5, 101, man5, 1, man5, 0, pin5] })
  hands[3].setPublicKongTiles([man5, pin5])
  for (const tile of [...hands[1].leftList, ...hands[2].leftList, ...hands[3].publicKongTiles])
    assert.equal(highlighted(tile), tile.tid === man5)
  highlight.clear()
  for (const tile of [...hands[1].leftList, ...hands[2].leftList, ...hands[3].publicKongTiles])
    assert.equal(highlighted(tile), false)
})

test('highlight resets safely when a replay rebuilds the table and restores the underlying tint', t => {
  const { hands, highlight, rivers } = table(t)
  hands[1].addMeld('pung', man5, { meldFromRel: 1 })
  const tile = hands[1].leftList[0]
  tile.setPersistentTint(0xcccccc)
  highlight.highlight(man5)
  assert.equal(highlighted(tile), true)
  highlight.bind(rivers, [])
  assert.equal(highlighted(tile), false)
  assert.equal(tile.children[0].tint, 0xcccccc)
  tile.destroy({ children: true })
  highlight.bind(rivers, hands)
  highlight.highlight(man5)
  assert.equal(highlighted(hands[1].leftList[1]), true)
})

test('red fives share the existing normalized match key while distinct suits remain distinct', t => {
  const { hands, highlight } = table(t)
  hands[1].addMeld('pung', salasasaTileToMmcr(105), { meldFromRel: 1 })
  hands[2].addMeld('pung', salasasaTileToMmcr(205), { meldFromRel: 1 })
  hands[3].addMeld('pung', salasasaTileToMmcr(305), { meldFromRel: 1 })
  highlight.highlight(man5)
  assert.equal(hands[1].leftList.every(highlighted), true)
  assert.equal(hands[2].leftList.some(highlighted), false)
  assert.equal(hands[3].leftList.some(highlighted), false)
})
