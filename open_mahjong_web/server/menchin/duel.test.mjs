import assert from 'node:assert/strict'
import test from 'node:test'
import { solveHand } from '../../client/src/utils/scoring/handShapes.ts'
import { createDuel, duelAct, duelTick, duelView } from './duel.mjs'

const tiles = digits => [...digits].map(d => 10 + Number(d))
const fullWall = () => Array.from({ length: 36 }, (_, i) => 11 + Math.floor(i / 4))

function deal({ first = '', second = '', draw = '9', front = '', back = '' } = {}) {
  const pool = fullWall()
  const a = tiles(first), b = tiles(second)
  const reserved = tiles(draw + front + back)
  for (const tile of [...a, ...b, ...reserved]) {
    const at = pool.indexOf(tile)
    assert.notEqual(at, -1, 'fixture contains more than four of a tile')
    pool.splice(at, 1)
  }
  while (a.length < 13) a.push(pool.shift())
  while (b.length < 13) b.push(pool.shift())
  const wall = [...a, ...b, ...tiles(draw + front), ...pool, ...tiles(back)]
  return createDuel({ now: 0, wallFactory: () => wall })
}

const act = (d, type, tile) => duelAct(d, d.turn, { revision: d.revision, type, tile }, d.deadline - 1)
const options = d => duelView(d, d.turn).actions
const types = d => options(d).map(a => a.type)

function assertConservation(d) {
  const remaining = [...d.wall, ...d.hands.flat(), ...d.melds.flat().flatMap(m => m.tiles),
    ...d.rivers.flat().filter(r => !r.claimed).map(r => r.tile)]
  assert.deepEqual(remaining.sort((a, b) => a - b), fullWall())
}

test('36 tiles, only player-visible information, stale/invalid/late actions leave the state intact', () => {
  const d = deal()
  const a = duelView(d, 0), b = duelView(d, 1)
  assert.deepEqual(a.handCounts, [14, 13])
  assert.equal(a.wallCount, 9)
  assert.equal(a.deadline, 10000)
  assert.equal(a.hand.at(-1), a.drawn)
  assert.equal(b.hand.length, 13)
  assert.equal(b.drawn, null)
  assert.deepEqual(b.actions, [])
  assert.equal('wall' in b || 'hands' in b || 'waits' in b, false)
  assertConservation(d)
  for (const [seat, action, now] of [
    [1, { type: 'win', revision: d.revision }, 4],
    [0, { type: 'discard', tile: 20, revision: d.revision }, 4],
    [0, { type: 'win', revision: d.revision - 1 }, 4],
    [0, { type: 'win', revision: d.revision }, d.deadline],
  ]) {
    assert.throws(() => duelAct(d, seat, action, now))
    assert.deepEqual(duelView(d, 0), a)
  }
  const first = a.drawn
  assert.equal(duelTick(d, 9999), false)
  assert.equal(duelTick(d, 10000), true)
  assert.equal(d.phase, 'claim')
  assert.equal(d.deadline, 15000)
  assert.deepEqual(d.rivers[0], [{ tile: first, claimed: false }])
  assert.ok(types(d).includes('win'))
  assert.equal(duelTick(d, 15000), true)
  assert.equal(d.turn, 1)
  assert.equal(d.deadline, 25000)
  assertConservation(d)
})

test('standard, MCR seven pairs with a quad, and false declarations are resolved only on the win action', () => {
  for (const [hand, expected] of [['12323434567899', 'tsumo'], ['11112244778899', 'tsumo'], ['11112222555589', 'falseWin']]) {
    const d = deal({ first: hand.slice(0, -1), draw: hand.at(-1) })
    assert.ok(types(d).includes('win'))
    act(d, 'win')
    assert.equal(d.result.reason, expected)
    assert.deepEqual(d.scores, expected === 'tsumo' ? [1, 0] : [0, 1])
    assert.equal(d.phase, 'between')
    assert.equal(duelView(d, 1).result.hand.length, 14)
    assert.equal(duelView(d, 1).result.hand.at(-1), d.result.tile)
  }
  const d = deal({ second: '1111222255558', draw: '9' })
  act(d, 'discard', 19)
  assert.ok(types(d).includes('win'))
  act(d, 'win')
  assert.equal(d.result.reason, 'falseWin')
  assert.deepEqual(d.scores, [1, 0])
  assert.deepEqual(duelView(d, 0).result.hand, tiles('11112222555589'))
})

test('chow variants are explicit, claimed river entries persist, no win on a post-chow discard', () => {
  const d = deal({ second: '1245', draw: '3' })
  act(d, 'discard', 13)
  assert.deepEqual(options(d).filter(a => a.type === 'chi').map(a => a.tile), [11, 12, 13])
  act(d, 'chi', 12)
  assert.deepEqual(d.melds[1], [{ kind: 'chi', tiles: [12, 13, 14], from: 0 }])
  assert.equal(d.rivers[0][0].claimed, true)
  assert.equal(d.turn, 1)
  assert.ok(types(d).every(type => type === 'discard'))
  assert.equal(duelView(d, 1).drawn, null)
  assertConservation(d)
  assert.ok(duelTick(d, d.deadline))
  assert.equal(d.lastDiscard.seat, 1)
})

test('concealed and exposed kongs draw from the tail and preserve all tiles', () => {
  for (const kind of ['anGang', 'mingGang']) {
    const d = kind === 'anGang' ? deal({ first: '1111', back: '5' }) : deal({ second: '111', draw: '1', back: '5' })
    if (kind === 'mingGang') act(d, 'discard', 11)
    const owner = d.turn
    act(d, kind, kind === 'anGang' ? 11 : undefined)
    assert.equal(d.wall.length, 8)
    assert.equal(duelView(d, owner).drawn, 15)
    assert.equal(d.turn, owner)
    const meld = duelView(d, 1 - owner).melds[owner][0]
    assert.deepEqual(meld.tiles, kind === 'anGang' ? [0, 0, 0, 0] : [11, 11, 11, 11])
    assertConservation(d)
  }
})

test('pung upgrades require a real fourth tile; robbing the kong allows a previous discard as a wait', () => {
  for (const reaction of ['win', 'pass']) {
    const d = deal({ first: '2323445667899', second: '115', draw: '1', front: '51', back: '8' })
    act(d, 'discard', 11)
    act(d, 'peng')
    act(d, 'discard', 15)
    act(d, 'pass')
    act(d, 'discard', 15)
    act(d, 'pass')
    assert.equal(d.drawn, 11)
    act(d, 'jiaGang', 11)
    assert.equal(d.phase, 'rob')
    assert.deepEqual(types(d), ['win', 'pass'])
    assert.equal(d.melds[1][0].kind, 'peng')
    const remaining = d.wall.length
    act(d, reaction)
    if (reaction === 'win') {
      assert.equal(d.result.reason, 'rob')
      assert.deepEqual(d.scores, [1, 0])
      assert.equal(d.wall.length, remaining)
    } else {
      assert.equal(d.melds[1][0].kind, 'jiaGang')
      assert.equal(d.drawn, 18)
      assert.equal(d.wall.length, remaining - 1)
    }
    assertConservation(d)
  }
})

test('the last draw allows self-draw and its final discard still allows a winning claim', () => {
  const d = deal({ first: '2323445667899', back: '1' })
  while (d.wall.length > 0) duelTick(d, d.deadline)
  assert.equal(d.turn, 1)
  assert.ok(types(d).includes('win'))
  assert.equal(d.drawn, 11)
  act(d, 'discard', 11)
  assert.deepEqual(types(d), ['win', 'pass'])
  act(d, 'win')
  assert.equal(d.result.reason, 'ron')
  assert.deepEqual(d.scores, [1, 0])
})

test('20 alternating starting seats; exhausted walls score zero, a win in the last round decides the match', () => {
  const d = deal({ first: '1111224477889', draw: '9' })
  for (let round = 1; round <= 20; round++) {
    assert.equal(d.round, round)
    assert.equal(d.dealer, (round - 1) % 2)
    assert.equal(d.turn, d.dealer)
    assert.equal(duelView(d, d.dealer).hand.length, 14)
    if (round === 20) {
      act(d, 'win')
      break
    }
    while (d.phase !== 'between') {
      assertConservation(d)
      assert.ok(duelTick(d, d.deadline))
    }
    assert.equal(d.result.reason, 'draw')
    assert.deepEqual(d.scores, [0, 0])
    assert.equal(duelTick(d, d.deadline - 1), false)
    assert.ok(duelTick(d, d.deadline))
  }
  assert.deepEqual(d.scores, [0, 1])
  assert.equal(d.phase, 'finished')
  assert.equal(d.deadline, null)
  assert.equal(duelTick(d, Number.MAX_SAFE_INTEGER), false)
})

test('declarations match the independent platform MCR solver on closed and exposed hands', () => {
  let openCount = 0
  for (let i = 0; i < 400; i++) {
    const d = createDuel({ now: 0 })
    if (i % 2) {
      act(d, 'discard', d.drawn)
      const call = options(d).find(a => a.type === 'chi' || a.type === 'peng')
      if (call) {
        act(d, call.type, call.tile)
        act(d, 'discard', d.hands[d.turn][0])
        act(d, 'pass')
        act(d, 'discard', d.drawn)
        openCount++
      }
    }
    const seat = d.turn, other = 1 - seat
    const hand = d.phase === 'discard' ? d.hands[seat] : [...d.hands[seat], d.lastDiscard.tile]
    const fixed = d.melds[seat].map(m => m.kind === 'chi' ? 's' + m.tiles[1] : 'k' + m.tiles[0])
    const valid = solveHand(hand, fixed, { mcrSevenPairs: true }).length > 0
    act(d, 'win')
    assert.equal(d.result.winner, valid ? seat : other)
    assertConservation(d)
  }
  assert.ok(openCount > 0)
})
