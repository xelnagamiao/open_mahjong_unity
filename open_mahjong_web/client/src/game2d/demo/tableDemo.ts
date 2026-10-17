import type { ActiveSessionSnapshot, MeldSnapshot } from '../game/scene/types'
import { salasasaTileToMmcr, mmcrTileToSalasasa } from '../salasasa/gameAdapter'

export type DemoScenario = 'discard' | 'claim' | 'random'
export type DemoMeld = {
  kind: 'chow' | 'pung' | 'kong'
  tiles: number[]
  from?: 1 | 2 | 3
  claimedIndex?: number
  concealed?: boolean
}
export type DemoState = {
  scenario: DemoScenario
  hand: number[]
  drawn: number | null
  melds: DemoMeld[]
  opponentMelds: DemoMeld[][]
  flowers: number[][]
  rivers: number[][]
  opponents: number[][]
  wall: number[]
  currentSeat: number
  phase: 'discard' | 'claim' | 'opponents' | 'finished'
  turn: number
  lastDiscarder: number | null
  message: string
}

export const DEMO_PLAYERS = [
  { name: '示例玩家1', score: 320 },
  { name: '示例玩家2', score: 304 },
  { name: '示例玩家3', score: 336 },
  { name: '示例玩家4', score: 320 },
]

export function tileName(tile: number): string {
  if (tile >= 41) return ['东风', '南风', '西风', '北风', '红中', '白板', '发财'][tile - 41] ?? '花牌'
  return `${['一', '二', '三', '四', '五', '六', '七', '八', '九'][tile % 10 - 1]}${['', '万', '筒', '条'][Math.floor(tile / 10)]}`
}

/** An offline interaction fixture. It deliberately does not adjudicate or score a game. */
export function createDemoState(scenario: DemoScenario = 'discard'): DemoState {
  if (scenario === 'random') return createRandomDemoState()
  const hand = scenario === 'claim'
    ? [11, 12, 13, 21, 22, 23, 23, 31, 32, 33, 45, 45, 45]
    : [11, 12, 13, 22, 23, 24, 31, 32, 35, 37, 38, 39, 45]
  const drawn = scenario === 'discard' ? 45 : null
  const rivers = [
    [41, 19, 29, 43, 18, 28],
    [42, 39, 47, 11, 17, 26],
    [44, 46, 21, 16, 34, 29],
    [43, 19, 28, 41, 36, 24],
  ]
  if (scenario === 'claim') rivers[3].push(23)
  const wall: number[] = []
  for (const base of [10, 20, 30, 40]) {
    for (let rank = 1; rank <= (base === 40 ? 7 : 9); rank++) wall.push(...Array(4).fill(base + rank))
  }
  // Remove the visible tiles first, so even the example respects the four-copy limit.
  for (const tile of [...hand, ...(drawn ? [drawn] : []), ...rivers.flat()]) {
    wall.splice(wall.indexOf(tile), 1)
  }
  let seed = 20261016
  for (let i = wall.length - 1; i > 0; i--) {
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0
    const j = seed % (i + 1)
    ;[wall[i], wall[j]] = [wall[j], wall[i]]
  }
  const opponents = [[], wall.splice(0, 13), wall.splice(0, 13), wall.splice(0, 13)]
  return {
    scenario, hand, drawn, rivers, opponents, wall, melds: [],
    opponentMelds: [[], [], [], []], flowers: [[], [], [], []], turn: 7,
    currentSeat: scenario === 'claim' ? 3 : 0,
    phase: scenario === 'claim' ? 'claim' : 'discard',
    lastDiscarder: scenario === 'claim' ? 3 : null,
    message: scenario === 'claim' ? '上家打出三筒，可以吃、碰或过。' : '轮到你出牌',
  }
}

/** A fresh setting-table preview, sharing one physical deck across all four seats. */
export function createRandomDemoState(random: () => number = Math.random): DemoState {
  const faces = [10, 20, 30, 40].flatMap(base =>
    Array.from({ length: base === 40 ? 7 : 9 }, (_, index) => base + index + 1))
  const pool = faces.flatMap(tile => Array<number>(4).fill(tile))
  const pick = (length: number) => Math.floor(random() * length)
  const take = (tile: number) => { pool.splice(pool.indexOf(tile), 1) }
  const draw = (count: number) => Array.from({ length: count }, () => pool.splice(pick(pool.length), 1)[0]!)
  const seatMelds: DemoMeld[][] = [[], [], [], []]
  const hands: number[][] = []
  const rivers: number[][] = []
  const flowers: number[][] = [[], [], [], []]

  for (let seat = 0; seat < 4; seat++) {
    const count = 1 + pick(3)
    for (let group = 0; group < count; group++) {
      const remaining = new Map(faces.map(tile => [tile, pool.filter(value => value === tile).length]))
      const candidates: DemoMeld[] = []
      for (const tile of faces) {
        if (tile < 40 && tile % 10 <= 7 && [tile, tile + 1, tile + 2].every(face => remaining.get(face)! > 0)) {
          candidates.push({ kind: 'chow', tiles: [tile, tile + 1, tile + 2] })
        }
        if (remaining.get(tile)! >= 3) candidates.push({ kind: 'pung', tiles: [tile, tile, tile] })
        if (remaining.get(tile) === 4) candidates.push({ kind: 'kong', tiles: [tile, tile, tile, tile] })
      }
      if (!candidates.length) break
      const roll = random()
      const kind = roll < 0.4 ? 'chow' : roll < 0.55 ? 'kong' : 'pung'
      const preferred = candidates.filter(meld => meld.kind === kind)
      const choices = preferred.length ? preferred : candidates
      const meld = choices[pick(choices.length)]!
      meld.from = meld.kind === 'chow' ? 3 : (1 + pick(3)) as 1 | 2 | 3
      meld.claimedIndex = meld.kind === 'chow' ? pick(3) : 0
      meld.concealed = meld.kind === 'kong' && random() < 0.5
      meld.tiles.forEach(take)
      seatMelds[seat]!.push(meld)
    }
    // A kong occupies one three-tile meld slot; its fourth tile is supplementary.
    hands.push(draw(13 - seatMelds[seat]!.length * 3).sort((a, b) => a - b))
    rivers.push(draw(6 + pick(9)))
  }
  for (let tile = 51; tile <= 58; tile++) flowers[pick(4)]!.push(tile)
  const drawn = draw(1)[0]!
  for (let i = pool.length - 1; i > 0; i--) {
    const j = pick(i + 1)
    ;[pool[i], pool[j]] = [pool[j]!, pool[i]!]
  }
  return {
    scenario: 'random', hand: hands[0]!, drawn, melds: seatMelds[0]!,
    opponentMelds: [[], ...seatMelds.slice(1)], flowers, rivers,
    opponents: [[], ...hands.slice(1)], wall: pool,
    currentSeat: 0, phase: 'discard', turn: Math.max(...rivers.map(river => river.length)) + 1,
    lastDiscarder: null, message: '轮到你出牌',
  }
}

export function discardDemoTile(state: DemoState, index: number): boolean {
  if (state.phase !== 'discard') return false
  const tiles = [...state.hand, ...(state.drawn == null ? [] : [state.drawn])]
  if (!Number.isInteger(index) || index < 0 || index >= tiles.length) return false
  const [tile] = tiles.splice(index, 1)
  state.hand = tiles.sort((a, b) => a - b)
  state.drawn = null
  state.rivers[0].push(tile)
  state.lastDiscarder = 0
  state.currentSeat = 1
  state.phase = 'opponents'
  state.message = `你打出${tileName(tile)}`
  return true
}

export function advanceDemoTurn(state: DemoState): boolean {
  if (state.phase !== 'opponents') return false
  const tile = state.wall.shift()
  if (tile == null) {
    state.phase = 'finished'
    state.message = '本次演示结束，点击随机牌桌可重新体验。'
    return true
  }
  const seat = state.currentSeat
  if (seat === 0) {
    state.drawn = tile
    state.phase = 'discard'
    state.turn++
    state.message = `摸到${tileName(tile)}，轮到你出牌`
  } else {
    const hand = state.opponents[seat]
    hand.push(tile)
    const [discard] = hand.splice((state.turn + seat) % hand.length, 1)
    state.rivers[seat].push(discard)
    state.lastDiscarder = seat
    state.message = `${DEMO_PLAYERS[seat].name}打出${tileName(discard)}`
    state.currentSeat = (seat + 1) % 4
  }
  return true
}

function demoMeldSnapshot(meld: DemoMeld): MeldSnapshot {
  const sorted = [...meld.tiles].sort((a, b) => a - b)
  const display = [...meld.tiles]
  const claimed = display[meld.claimedIndex ?? (meld.kind === 'chow' ? 2 : 0)]!
  const from = meld.from ?? 3
  if (meld.kind === 'chow') {
    display.splice(display.indexOf(claimed), 1)
    display.unshift(claimed)
  }
  const sideways = meld.kind === 'chow' || from === 1 ? 0 : from === 2 ? 1 : display.length - 1
  return {
    type: meld.kind === 'chow' ? 'sequence' : meld.kind === 'pung' ? 'triplet' : 'kong',
    tile: salasasaTileToMmcr(meld.kind === 'chow' ? sorted[1]! : sorted[0]!),
    physical_tiles: meld.tiles.map(salasasaTileToMmcr),
    physical_mask: display.flatMap((tile, index) => [meld.concealed ? 2 : index === sideways ? 1 : 0, salasasaTileToMmcr(tile)]),
    concealed: meld.concealed ?? false,
    chow_mode: meld.kind === 'chow' ? sorted.indexOf(claimed) + 1 : 0,
    meld_from_rel: meld.concealed ? 0 : from,
  }
}

export function claimDemoTile(state: DemoState, kind: 'chow' | 'pung' | 'pass'): boolean {
  if (state.phase !== 'claim') return false
  if (kind === 'pass') {
    state.phase = 'opponents'
    state.currentSeat = 0
    return advanceDemoTurn(state)
  }
  const consumed = kind === 'chow' ? [21, 22] : [23, 23]
  const hand = [...state.hand]
  for (const tile of consumed) {
    const index = hand.indexOf(tile)
    if (index < 0) return false
    hand.splice(index, 1)
  }
  if (state.rivers[3].at(-1) !== 23) return false
  state.rivers[3].pop()
  state.hand = hand
  state.melds.push({ kind, tiles: [...consumed, 23] })
  state.currentSeat = 0
  state.phase = 'discard'
  state.lastDiscarder = null
  state.message = `${kind === 'chow' ? '吃' : '碰'}牌成功，请打出一张手牌`
  return true
}

/** Accept the original Pixi scene's input without contacting the game server. */
export function applyDemoInput(state: DemoState, payload: Record<string, unknown>): boolean {
  if (payload.kind === 'discard_tile') {
    const tile = mmcrTileToSalasasa(Number(payload.tile))
    if (payload.use_drawn_tile) {
      return state.drawn === tile && discardDemoTile(state, state.hand.length)
    }
    return discardDemoTile(state, state.hand.indexOf(tile))
  }
  if (payload.kind === 'chow' || payload.kind === 'pung' || payload.kind === 'pass') {
    return claimDemoTile(state, payload.kind)
  }
  return false
}

/** Supply the unmodified 2D scene with its normal hand, meld and action fields. */
export function demoSnapshot(state: DemoState): ActiveSessionSnapshot {
  return {
    phase: 'active', session_id: 0,
    state: {
      round_counter: 0, stage_counter: state.turn, remaining_tile_count: state.wall.length,
      current_player: state.currentSeat, last_discarder: state.lastDiscarder,
    },
    seats: DEMO_PLAYERS.map((player, seat) => ({
      seat_index: seat, score: player.score, afk: false, player_id: null, username: player.name,
      hand_tile_count: seat === 0 ? state.hand.length : state.opponents[seat].length,
      has_drawn_tile: seat === 0 && state.drawn !== null,
      hand_tiles: seat === 0 ? state.hand.map(salasasaTileToMmcr) : undefined,
      drawn_tile: seat === 0 && state.drawn !== null ? salasasaTileToMmcr(state.drawn) : null,
      discard_pile: state.rivers[seat].map(salasasaTileToMmcr),
      melds: (seat === 0 ? state.melds : state.opponentMelds[seat]!).map(demoMeldSnapshot),
      flower_tiles: state.flowers[seat]!.map(salasasaTileToMmcr),
    })),
    viewer: {
      seat_index: 0, pending: ['discard', 'claim'].includes(state.phase) ? 'decision' : 'none',
      decision_timer_ms: null,
      available_actions: state.phase === 'discard' ? [{ kind: 'discard_tile' }]
        : state.phase === 'claim' ? [
          { kind: 'pass' },
          { kind: 'chow', tile: salasasaTileToMmcr(23), ui64_value: 3 },
          { kind: 'pung', tile: salasasaTileToMmcr(23) },
        ] : [],
    },
  }
}
