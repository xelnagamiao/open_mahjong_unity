import test from 'node:test'
import assert from 'node:assert/strict'
import { createDemoState, createRandomDemoState, discardDemoTile, advanceDemoTurn, claimDemoTile, demoSnapshot, applyDemoInput } from '../src/game2d/demo/tableDemo.ts'
import { salasasaTileToMmcr } from '../src/game2d/salasasa/gameAdapter.ts'

function verifyTiles(state) {
  const tiles = [...state.hand, ...(state.drawn == null ? [] : [state.drawn]), ...state.rivers.flat(), ...state.opponents.flat(), ...state.wall, ...state.melds.flatMap(meld => meld.tiles), ...state.opponentMelds.flat(1).flatMap(meld => meld.tiles)]
  assert.equal(tiles.length, 136, 'every physical tile must remain accounted for')
  for (const tile of new Set(tiles)) assert.equal(tiles.filter(value => value === tile).length, 4)
}

test('demo fixtures preserve four copies and are independent on reset', () => {
  for (const scenario of ['discard', 'claim']) {
    const state = createDemoState(scenario)
    verifyTiles(state)
    state.hand.pop()
    assert.equal(createDemoState(scenario).hand.length, 13)
  }
})

function seededRandom(seed) {
  return () => {
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0
    return seed / 0x100000000
  }
}

test('random setting tables preserve one deck, legal melds and eight unique flowers', () => {
  const kinds = new Set()
  const sources = new Set()
  for (let seed = 0; seed < 500; seed++) {
    const state = createRandomDemoState(seededRandom(seed))
    verifyTiles(state)
    assert.deepEqual(state.flowers.flat().sort((a, b) => a - b), [51, 52, 53, 54, 55, 56, 57, 58])
    const snapshot = demoSnapshot(state)
    for (let seat = 0; seat < 4; seat++) {
      const melds = seat === 0 ? state.melds : state.opponentMelds[seat]
      const hand = seat === 0 ? state.hand : state.opponents[seat]
      assert.ok(melds.length >= 1 && melds.length <= 3)
      assert.equal(hand.length + melds.length * 3, 13, 'a kong consumes one meld slot')
      assert.ok(state.rivers[seat].length >= 6 && state.rivers[seat].length <= 14)
      assert.deepEqual(snapshot.seats[seat].flower_tiles, state.flowers[seat].map(salasasaTileToMmcr))
      melds.forEach((meld, index) => {
        const projected = snapshot.seats[seat].melds[index]
        const flags = projected.physical_mask.filter((_, i) => i % 2 === 0)
        const faces = projected.physical_mask.filter((_, i) => i % 2 === 1)
        kinds.add(meld.kind + (meld.concealed ? '-concealed' : ''))
        assert.deepEqual([...faces].sort((a, b) => a - b), meld.tiles.map(salasasaTileToMmcr).sort((a, b) => a - b))
        if (meld.kind === 'chow') {
          const sorted = [...meld.tiles].sort((a, b) => a - b)
          assert.deepEqual(sorted, [sorted[0], sorted[0] + 1, sorted[0] + 2])
          assert.ok(sorted[0] < 40 && Math.floor(sorted[0] / 10) === Math.floor(sorted[2] / 10))
          assert.equal(projected.meld_from_rel, 3)
          assert.deepEqual(flags, [1, 0, 0], 'all chows display the claimed tile at the upper-seat end')
          assert.equal(faces[0], salasasaTileToMmcr(meld.tiles[meld.claimedIndex]))
        } else {
          assert.equal(new Set(meld.tiles).size, 1)
          assert.equal(meld.tiles.length, meld.kind === 'kong' ? 4 : 3)
          if (meld.concealed) assert.ok(flags.every(flag => flag === 2))
          else {
            assert.equal(flags.filter(flag => flag === 1).length, 1)
            sources.add(meld.from)
          }
        }
      })
    }
    assert.equal(state.hand.length + state.melds.length * 3 + 1, 14)
  }
  assert.deepEqual([...kinds].sort(), ['chow', 'kong', 'kong-concealed', 'pung'])
  assert.deepEqual([...sources].sort(), [1, 2, 3])
})

test('random tables remain playable and conserve tiles until the wall is exhausted', () => {
  for (const seed of [1, 7, 42]) {
    const state = createRandomDemoState(seededRandom(seed))
    const flowers = JSON.stringify(state.flowers)
    while (state.phase !== 'finished') {
      if (state.phase === 'discard') assert.equal(discardDemoTile(state, 0), true)
      else assert.equal(advanceDemoTurn(state), true)
      verifyTiles(state)
    }
    assert.equal(JSON.stringify(state.flowers), flowers)
  }
})

test('randomizing creates independent states and snapshots without mutating an earlier table', () => {
  const random = seededRandom(123)
  const first = createRandomDemoState(random)
  const before = JSON.stringify(first)
  const second = createRandomDemoState(random)
  assert.notDeepEqual(demoSnapshot(first), demoSnapshot(second))
  second.hand.pop()
  second.melds[0].tiles.pop()
  second.flowers[0].push(51)
  assert.equal(JSON.stringify(first), before)
  for (let i = 0; i < 10; i++) demoSnapshot(first)
  assert.equal(JSON.stringify(first), before)
})

test('claim fixture also places a claimed middle or high chow tile at the upper-seat end', () => {
  const state = createDemoState('claim')
  assert.equal(claimDemoTile(state, 'chow'), true)
  assert.deepEqual(demoSnapshot(state).seats[0].melds[0].physical_mask,
    [1, salasasaTileToMmcr(23), 0, salasasaTileToMmcr(21), 0, salasasaTileToMmcr(22)])
})

test('discard advances all three opponents and draws for the same player', () => {
  const state = createDemoState()
  const remaining = state.wall.length
  const tile = state.hand[2]
  assert.equal(discardDemoTile(state, 2), true)
  assert.equal(state.rivers[0].at(-1), tile)
  assert.equal(state.hand.length, 13)
  assert.equal(discardDemoTile(state, 0), false, 'reject duplicate submission during opponent turns')
  for (let i = 0; i < 4; i++) { assert.equal(advanceDemoTurn(state), true); verifyTiles(state) }
  assert.equal(state.phase, 'discard')
  assert.equal(state.currentSeat, 0)
  assert.equal(state.wall.length, remaining - 4)
  assert.equal(state.turn, 8)
  assert.notEqual(state.drawn, null)
})

test('chow/pung move the claimed discard once and retain a legal hand size', () => {
  for (const kind of ['chow', 'pung']) {
    const state = createDemoState('claim')
    assert.equal(claimDemoTile(state, kind), true)
    assert.equal(state.rivers[3].length, 6)
    assert.equal(state.hand.length, 11)
    assert.equal(state.drawn, null)
    assert.equal(state.melds.length, 1)
    assert.equal(state.phase, 'discard')
    assert.equal(claimDemoTile(state, kind), false)
    verifyTiles(state)
    discardDemoTile(state, 0)
    for (let i = 0; i < 4; i++) advanceDemoTurn(state)
    assert.equal(state.hand.length, 10)
    assert.notEqual(state.drawn, null)
    verifyTiles(state)
  }
})

test('pass leaves the river intact and draws without a meld', () => {
  const state = createDemoState('claim')
  claimDemoTile(state, 'pass')
  assert.equal(state.rivers[3].length, 7)
  assert.equal(state.melds.length, 0)
  assert.equal(state.phase, 'discard')
  verifyTiles(state)
})

test('invalid tile indices do not mutate the fixture', () => {
  const state = createDemoState()
  const before = JSON.stringify(state)
  for (const index of [-1, 14, 0.5, NaN]) assert.equal(discardDemoTile(state, index), false)
  assert.equal(JSON.stringify(state), before)
})

test('presentation projection and repeated layout reads cannot reset the hand', () => {
  const state = createDemoState('claim')
  claimDemoTile(state, 'pung')
  const before = JSON.stringify(state)
  for (let i = 0; i < 8; i++) {
    const snapshot = demoSnapshot(state)
    assert.equal(snapshot.viewer.seat_index, 0)
    assert.deepEqual(snapshot.viewer.available_actions, [{ kind: 'discard_tile' }])
    assert.deepEqual(snapshot.seats[0].hand_tiles, state.hand.map(salasasaTileToMmcr))
    assert.equal(snapshot.seats[0].melds[0].type, 'triplet')
    assert.equal(snapshot.state.remaining_tile_count, state.wall.length)
  }
  assert.equal(JSON.stringify(state), before)
})

test('wall exhaustion stops cleanly and never discards undefined', () => {
  const state = createDemoState()
  while (state.phase !== 'finished') {
    if (state.phase === 'discard') discardDemoTile(state, 0)
    else advanceDemoTurn(state)
    verifyTiles(state)
  }
  assert.equal(state.wall.length, 0)
  assert.equal(advanceDemoTurn(state), false)
})

test('the original scene can discard either the drawn tile or a matching hand tile', () => {
  for (const useDrawn of [true, false]) {
    const state = createDemoState()
    const snapshot = demoSnapshot(state)
    assert.equal(snapshot.seats[0].has_drawn_tile, true)
    assert.equal(snapshot.seats[0].drawn_tile, salasasaTileToMmcr(45))
    const payload = { kind: 'discard_tile', tile: salasasaTileToMmcr(45), use_drawn_tile: useDrawn }
    assert.equal(applyDemoInput(state, payload), true)
    assert.equal(state.rivers[0].at(-1), 45)
    assert.equal(state.drawn, null)
    assert.equal(applyDemoInput(state, payload), false)
    verifyTiles(state)
  }
})

test('the original scene receives its normal claim actions and meld snapshots', () => {
  for (const kind of ['chow', 'pung', 'pass']) {
    const state = createDemoState('claim')
    assert.deepEqual(demoSnapshot(state).viewer.available_actions.map(action => action.kind), ['pass', 'chow', 'pung'])
    assert.equal(applyDemoInput(state, { kind }), true)
    const snapshot = demoSnapshot(state)
    assert.equal(snapshot.viewer.pending, 'decision')
    assert.equal(snapshot.seats[0].melds.length, kind === 'pass' ? 0 : 1)
    if (kind !== 'pass') assert.deepEqual(snapshot.seats[0].melds[0].physical_tiles, state.melds[0].tiles.map(salasasaTileToMmcr))
    verifyTiles(state)
  }
})
