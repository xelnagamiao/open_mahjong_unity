import { randomInt } from 'node:crypto'

const ROUNDS = 20
const DRAW_MS = 10_000
const CLAIM_MS = 5_000
const RESULT_MS = 3_000
const ranks = Array.from({ length: 9 }, (_, i) => 11 + i)
const sorted = tiles => [...tiles].sort((a, b) => a - b)

function shuffledWall() {
  const wall = ranks.flatMap(tile => Array(4).fill(tile))
  for (let end = wall.length - 1; end > 0; end--) {
    const i = randomInt(end + 1)
    const tile = wall[end]
    wall[end] = wall[i]
    wall[i] = tile
  }
  return wall
}

export function countsOf(hand) {
  const counts = Array(9).fill(0)
  for (const tile of hand) counts[tile - 11]++
  return counts
}

function consistsOfMelds(counts) {
  const i = counts.findIndex(n => n > 0)
  if (i < 0) return true
  if (counts[i] >= 3) {
    counts[i] -= 3
    const match = consistsOfMelds(counts)
    counts[i] += 3
    if (match) return true
  }
  if (i > 6 || !counts[i + 1] || !counts[i + 2]) return false
  for (let n = i; n <= i + 2; n++) counts[n]--
  const match = consistsOfMelds(counts)
  for (let n = i; n <= i + 2; n++) counts[n]++
  return match
}

export function canWin(hand, meldCount) {
  return completedShape(countsOf(hand), meldCount)
}

export function completedShape(counts, meldCount) {
  if (counts.reduce((sum, n) => sum + n, meldCount * 3) !== 14) return false
  if (meldCount === 0 && counts.every(n => n % 2 === 0)) return true
  for (let i = 0; i < counts.length; i++) {
    if (counts[i] < 2) continue
    counts[i] -= 2
    const complete = consistsOfMelds(counts)
    counts[i] += 2
    if (complete) return true
  }
  return false
}

function beginRound(d, now) {
  const wall = [...d.wallFactory()]
  if (wall.length !== 36 || countsOf(wall).some(n => n !== 4)) throw new Error('万字牌山无效')
  d.round++
  d.dealer = (d.round - 1) % 2
  d.hands = [[], []]
  d.hands[d.dealer] = wall.splice(0, 13)
  d.hands[1 - d.dealer] = wall.splice(0, 13)
  d.wall = wall
  d.rivers = [[], []]
  d.melds = [[], []]
  d.result = null
  draw(d, d.dealer, false, now)
}

export function createDuel({ now = Date.now(), wallFactory = shuffledWall } = {}) {
  const duel = { wallFactory, round: 0, revision: 0, scores: [0, 0] }
  beginRound(duel, now)
  return duel
}

function setTurn(d, phase, turn, duration, now) {
  d.phase = phase
  d.turn = turn
  d.deadline = duration === null ? null : now + duration
  d.revision++
}

function draw(d, seat, replacement, now) {
  d.lastDiscard = null
  if (!d.wall.length) return finishRound(d, 'draw', null, null, null, now)
  const tile = replacement ? d.wall.pop() : d.wall.shift()
  d.hands[seat].push(tile)
  d.drawn = tile
  setTurn(d, 'discard', seat, DRAW_MS, now)
}

function finishRound(d, reason, winner, actor, tile, now) {
  if (winner !== null) d.scores[winner]++
  d.result = { reason, winner, actor, tile }
  if (actor !== null) {
    const reveal = sorted(d.hands[actor])
    if (d.phase === 'discard') reveal.splice(reveal.lastIndexOf(tile), 1)
    reveal.push(tile)
    d.result.hand = reveal
  }
  if (d.round === ROUNDS) setTurn(d, 'finished', null, null, now)
  else setTurn(d, 'between', null, RESULT_MS, now)
}

function availableActions(d, seat) {
  if (d.turn !== seat) return []
  const hand = d.hands[seat]
  const counts = countsOf(hand)
  if (d.phase === 'discard') {
    const actions = ranks.filter(tile => counts[tile - 11]).map(tile => ({ type: 'discard', tile }))
    if (d.drawn === null) return actions
    actions.push({ type: 'win' })
    if (d.wall.length) {
      for (const tile of ranks) if (counts[tile - 11] === 4) actions.push({ type: 'anGang', tile })
      for (const meld of d.melds[seat]) {
        const tile = meld.tiles[0]
        if (meld.kind === 'peng' && counts[tile - 11]) actions.push({ type: 'jiaGang', tile })
      }
    }
    return actions
  }
  const actions = [{ type: 'win' }, { type: 'pass' }]
  if (d.phase === 'rob' || !d.wall.length) return actions
  const tile = d.lastDiscard.tile
  if (counts[tile - 11] >= 2) actions.push({ type: 'peng' })
  if (counts[tile - 11] === 3) actions.push({ type: 'mingGang' })
  for (let start = Math.max(11, tile - 2); start <= Math.min(tile, 17); start++) {
    const sequence = [start, start + 1, start + 2]
    if (sequence.every(n => n === tile || counts[n - 11])) actions.push({ type: 'chi', tile: start })
  }
  return actions
}

export function duelView(d, seat) {
  if (seat !== 0 && seat !== 1) throw new Error('座位无效')
  const drawn = d.turn === seat && d.phase === 'discard' ? d.drawn : null
  const hand = sorted(d.hands[seat])
  if (drawn !== null) {
    hand.splice(hand.lastIndexOf(drawn), 1)
    hand.push(drawn)
  }
  return {
    round: d.round, rounds: ROUNDS, dealer: d.dealer, turn: d.turn, phase: d.phase,
    revision: d.revision, deadline: d.deadline, wallCount: d.wall.length, scores: [...d.scores],
    rivers: d.rivers.map(river => river.map(discard => ({ ...discard }))),
    melds: d.melds.map((melds, owner) => melds.map(meld => ({
      ...meld, tiles: owner !== seat && owner !== d.result?.actor && meld.kind === 'anGang' ? [0, 0, 0, 0] : [...meld.tiles],
    }))),
    hand, handCounts: d.hands.map(h => h.length), drawn,
    lastDiscard: d.lastDiscard && { ...d.lastDiscard },
    result: d.result && { ...d.result, ...(d.result.hand ? { hand: [...d.result.hand] } : {}) },
    actions: availableActions(d, seat),
  }
}

function remove(hand, tile, count = 1) {
  for (let i = 0; i < count; i++) hand.splice(hand.indexOf(tile), 1)
}

function discard(d, seat, tile, now) {
  remove(d.hands[seat], tile)
  d.rivers[seat].push({ tile, claimed: false })
  d.lastDiscard = { seat, tile }
  d.drawn = null
  setTurn(d, 'claim', 1 - seat, CLAIM_MS, now)
}

function pass(d, seat, now) {
  if (d.phase === 'rob') {
    const { seat: owner, tile } = d.lastDiscard
    const meld = d.melds[owner].find(m => m.kind === 'peng' && m.tiles[0] === tile)
    remove(d.hands[owner], tile)
    meld.kind = 'jiaGang'
    meld.tiles.push(tile)
    draw(d, owner, true, now)
  } else {
    draw(d, seat, false, now)
  }
}

function claim(d, seat, action, now) {
  const { seat: from, tile } = d.lastDiscard
  const hand = d.hands[seat]
  let tiles
  if (action.type === 'chi') {
    tiles = [action.tile, action.tile + 1, action.tile + 2]
    for (const id of tiles) if (id !== tile) remove(hand, id)
  } else {
    tiles = Array(action.type === 'mingGang' ? 4 : 3).fill(tile)
    remove(hand, tile, tiles.length - 1)
  }
  d.melds[seat].push({ kind: action.type, tiles, from })
  d.rivers[from].at(-1).claimed = true
  d.lastDiscard = null
  if (action.type === 'mingGang') draw(d, seat, true, now)
  else setTurn(d, 'discard', seat, DRAW_MS, now)
}

export function duelAct(d, seat, action, now = Date.now()) {
  if (seat !== 0 && seat !== 1) throw new Error('座位无效')
  if (action?.revision !== d.revision) throw new Error('牌局已更新')
  if (d.turn !== seat) throw new Error('未轮到你操作')
  if (now >= d.deadline) throw new Error('操作已超时')
  if (!availableActions(d, seat).some(a => a.type === action.type && a.tile === action.tile)) throw new Error('当前操作无效')

  const hand = d.hands[seat]
  switch (action.type) {
    case 'win': {
      const selfDraw = d.phase === 'discard'
      const tile = selfDraw ? d.drawn : d.lastDiscard.tile
      const valid = canWin(selfDraw ? hand : [...hand, tile], d.melds[seat].length)
      const reason = !valid ? 'falseWin' : selfDraw ? 'tsumo' : d.phase === 'rob' ? 'rob' : 'ron'
      finishRound(d, reason, valid ? seat : 1 - seat, seat, tile, now)
      return
    }
    case 'discard': return discard(d, seat, action.tile, now)
    case 'pass': return pass(d, seat, now)
    case 'anGang':
      remove(hand, action.tile, 4)
      d.melds[seat].push({ kind: 'anGang', tiles: Array(4).fill(action.tile), from: seat })
      return draw(d, seat, true, now)
    case 'jiaGang':
      d.lastDiscard = { seat, tile: action.tile }
      d.drawn = null
      return setTurn(d, 'rob', 1 - seat, CLAIM_MS, now)
    default: return claim(d, seat, action, now)
  }
}

export function duelTick(d, now = Date.now()) {
  if (d.deadline === null || now < d.deadline) return false
  if (d.phase === 'between') beginRound(d, now)
  else if (d.phase === 'discard') discard(d, d.turn, d.drawn ?? Math.max(...d.hands[d.turn]), now)
  else pass(d, d.turn, now)
  return true
}
