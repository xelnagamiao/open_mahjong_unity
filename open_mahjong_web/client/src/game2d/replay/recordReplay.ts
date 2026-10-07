import type {
  ActiveSessionSnapshot,
  MeldSnapshot,
  SeatSnapshot,
} from '../game/scene/types'
import { salasasaTileToMmcr, mmcrTileToSalasasa } from '../salasasa/gameAdapter'
import { hongzhongHintsAt as selectHongzhongHints } from '../../utils/hongzhongReplay.js'
import { duplicateReplayWall } from '../../utils/duplicateReplayWall.js'
import { isExternalRecord } from '../../utils/recordConvert/externalPlayers.js'
import { isGuangdongMilRecord } from '../../utils/guangdongReplay.js'
import { hangzhouActionLabel } from '../../utils/hangzhouReplay.js'
import { isSichuanRecord, isSichuanBloodBattle, isSichuanBloodFlow, nextSichuanPlayer, sichuanScoreChanges } from '../../utils/sichuanReplay.js'
import { isChangchunRecord, changchunWallAt, applyChangchunPhysical, CHANGCHUN_EVENT_NAMES } from '../../utils/changchunReplay.js'

export type RecordTick = unknown[]

export interface PublicRecordPlayer {
  user_id: number
  username: string
  score: number
  rank: number
  original_player_index: number | null
  voice_used?: number | null
}

export interface RecordRound {
  guizhou?: Record<string, unknown>
  yixing?: Record<string, unknown>
  wenzhou?: Record<string, unknown>
  wenzhou_waits?: Record<string, Record<string, unknown[]>>
  hangzhou?: Record<string, unknown>
  round_index?: number
  current_round?: number
  seats?: number[]
  dealer_index?: number
  start_player_index?: number
  p0_tiles?: number[]
  p1_tiles?: number[]
  p2_tiles?: number[]
  p3_tiles?: number[]
  tiles_list?: number[]
  duplicate_walls?: number[][]
  action_ticks?: RecordTick[]
}

export interface PublicGameRecord {
  game_id: string
  created_at: string
  rule: string
  sub_rule?: string | null
  room_type?: string | null
  match_type?: string | null
  players: PublicRecordPlayer[]
  record: {
    game_title?: Record<string, unknown>
    game_round?: Record<string, RecordRound>
  }
}

interface ReplaySeatState {
  hand: number[]
  drawn: number | null
  score: number
  river: number[]
  riverDrawn: boolean[]
  melds: MeldSnapshot[]
  flowers: number[]
}

export interface ReplayPosition {
  snapshot: ActiveSessionSnapshot
  actionLabel: string
}

export interface ReplayWallTile {
  tile: number
  consumed: boolean
  originalPlayer?: number
}

const FLOWER_MIN = 51
const FLOWER_MAX = 58

function int(value: unknown, fallback = 0): number {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? Math.trunc(parsed) : fallback
}

function bool(value: unknown): boolean {
  return value === true || String(value).toUpperCase() === 'T' || value === 1 || value === '1'
}

function normalizedTile(tile: number): number {
  return tile === 105 ? 15 : tile === 205 ? 25 : tile === 305 ? 35 : tile
}

function removeExactOrNormalized(tiles: number[], tile: number): number | null {
  let index = tiles.indexOf(tile)
  if (index < 0) index = tiles.findIndex((item) => normalizedTile(item) === normalizedTile(tile))
  if (index < 0) return null
  return tiles.splice(index, 1)[0] ?? null
}

function takeForClaim(tick: RecordTick, action: string, claimedTile: number, hand: number[]): number[] {
  const count = action === 'g' ? 3 : 2
  const explicit = tick.slice(3, 3 + count).map((value) => int(value)).filter((value) => value > 0)
  if (explicit.length === count) {
    return explicit.map((tile) => removeExactOrNormalized(hand, tile) ?? tile)
  }
  let wanted: number[]
  if (action === 'cl') wanted = [normalizedTile(claimedTile) - 2, normalizedTile(claimedTile) - 1]
  else if (action === 'cm') wanted = [normalizedTile(claimedTile) - 1, normalizedTile(claimedTile) + 1]
  else if (action === 'cr') wanted = [normalizedTile(claimedTile) + 1, normalizedTile(claimedTile) + 2]
  else wanted = Array(count).fill(normalizedTile(claimedTile))
  return wanted.map((tile) => removeExactOrNormalized(hand, tile) ?? tile)
}

function scoreChangeFromTick(tick: RecordTick): number[] | null {
  const action = String(tick[0] ?? '')
  let value: unknown
  if (action === 'riichi') {
    const result = [0, 0, 0, 0]
    const seat = int(tick[1], -1)
    if (seat >= 0 && seat < 4) result[seat] = -1000
    return result
  }
  if (['hu_self', 'hu_first', 'hu_second', 'hu_third'].includes(action)) value = tick[4]
  else if (action === 'hu_riichi') value = tick[6]
  else if (action === 'ryuukyoku') value = tick[2]
  else if (action === 'tuidao' && tick[1] === 'kong_score') value = tick[2]
  else if (action === 'hongzhong' && ['kong_score', 'kong_refund'].includes(String(tick[1]))) value = tick[2]
  else if (action === 'guangdong' && ['kong_score', 'refund_kongs'].includes(String(tick[1]))) value = tick[2]
  else if (action === 'guizhou' && tick[1] === 'draw_score') value = tick[2]
  else value = sichuanScoreChanges(tick)
  if (action === 'cc' && (tick[1] as any)?.kind === 'kong_score') value = [0, 1, 2, 3].map(i => (tick[1] as any).delta?.[i] ?? 0)
  if (!Array.isArray(value) || value.length < 4) return null
  return value.slice(0, 4).map((item) => int(item))
}

function actionName(tick: RecordTick | undefined): string {
  if (!tick?.length) return '局初'
  if (tick[0] === 'hangzhou') return hangzhouActionLabel(tick) || '杭州状态'
  if (tick[0] === 'cc') return CHANGCHUN_EVENT_NAMES[(tick[1] as any)?.kind] || '长春状态'
  const names: Record<string, string> = {
    d: '摸牌', gd: '杠后摸牌', bd: '补花摸牌', c: '出牌', bh: '补花', reset: '跳转',
    cl: '吃', cm: '吃', cr: '吃', p: '碰', g: '明杠', ag: '暗杠', jg: '加杠', rk: '抢杠',
    hu_self: '自摸', hu_first: '和牌', hu_second: '和牌', hu_third: '和牌',
    hu_riichi: tick[2] === 'hu_self' ? '自摸' : '荣和', riichi: '立直', dora: '翻宝牌',
    liuju: '流局', ryuukyoku: '流局', end: '本局结束',
  }
  return names[String(tick[0])] || String(tick[0])
}

function sceneKindForAction(action: string): string {
  const kinds: Record<string, string> = {
    d: 'draw_tile', gd: 'draw_tile', bd: 'draw_tile', c: 'discard_tile', bh: 'flower',
    cl: 'chow', cm: 'chow', cr: 'chow', p: 'pung', g: 'melded_kong',
    ag: 'concealed_kong', jg: 'added_kong', rk: 'rob_kong_tile', hu_self: 'self_drawn_win',
    hu_first: 'discard_win', hu_second: 'discard_win', hu_third: 'discard_win',
    liuju: 'drawn_game', ryuukyoku: 'drawn_game', end: 'end',
  }
  return kinds[action] || action
}

export const RECORD_SILENT_ACTIONS = new Set(['reset', 'ask_hand', 'ask_other', 'ca', 'tuidao', 'state', 'guizhou', 'yixing', 'wenzhou', 'hangzhou', 'hongzhong', 'guangdong'])

export function isRecordSilentTick(tick?: RecordTick): boolean {
  const action = String(tick?.[0] ?? '')
  return !action || RECORD_SILENT_ACTIONS.has(action)
}

function hasResetBefore(ticks: RecordTick[], node: number): boolean {
  for (let index = 0; index < node; index += 1) {
    if (String(ticks[index]?.[0] ?? '') === 'reset') return true
  }
  return false
}

export class RecordReplay {
  readonly detail: PublicGameRecord
  readonly rounds: RecordRound[]
  private readonly startingScoresByOriginal: number[]

  constructor(detail: PublicGameRecord) {
    this.detail = detail
    this.rounds = Object.entries(detail.record.game_round || {})
      .sort((left, right) => {
        const li = int(left[1].round_index, int(left[0].match(/\d+$/)?.[0]))
        const ri = int(right[1].round_index, int(right[0].match(/\d+$/)?.[0]))
        return li - ri
      })
      .map((entry) => entry[1])
    if (!this.rounds.length) throw new Error('这份牌谱没有可播放的小局')
    this.startingScoresByOriginal = this.computeStartingScores()
  }

  /** 只在打开牌谱时选默认玩家；后续切局由 seats 映射跟随该玩家。 */
  defaultViewerOriginal(userId: unknown): number {
    const uid = Number(userId)
    const title = this.detail.record.game_title
    if (!Number.isInteger(uid) || uid <= 0 || isExternalRecord(title)) return 0
    for (let original = 0; original < 4; original += 1) {
      if (Number(title?.[`p${original}_uid`]) === uid) return original
    }
    const player = this.detail.players.find(item => Number(item.user_id) === uid)
    const original = player?.original_player_index
    return typeof original === 'number' && Number.isInteger(original) && original >= 0 && original < 4
      ? original : 0
  }

  private seatsOf(round: RecordRound): number[] {
    const seats = Array.isArray(round.seats) ? round.seats.map((value) => int(value, -1)) : [0, 1, 2, 3]
    return seats.length === 4 && new Set(seats).size === 4 && seats.every((seat) => seat >= 0 && seat < 4)
      ? seats
      : [0, 1, 2, 3]
  }

  private scoreChangesByOriginal(round: RecordRound, endNode = round.action_ticks?.length ?? 0): number[] {
    const seats = this.seatsOf(round)
    const result = [0, 0, 0, 0]
    for (const tick of (round.action_ticks || []).slice(0, endNode)) {
      if (tick[0] === 'wenzhou' && tick[1] === 'state' && Array.isArray(tick[3]) && Array.isArray(round.wenzhou?.start_scores)) {
        const scores = tick[3] as number[], start = round.wenzhou.start_scores as number[]
        for (let original = 0; original < 4; original++) result[original] = int(scores[seats[original]]) - int(start[seats[original]])
        continue
      }
      const bySeat = scoreChangeFromTick(tick)
      if (!bySeat) continue
      for (let original = 0; original < 4; original += 1) result[original] += bySeat[seats[original]] || 0
    }
    return result
  }

  private computeStartingScores(): number[] {
    const title = this.detail.record.game_title
    if (title?.rule === 'riichi' || this.detail.rule === 'riichi' || title?.rule === 'hongkong' || this.detail.rule === 'hongkong') {
      const fallback = title?.rule === 'riichi' || this.detail.rule === 'riichi' ? 25000 : 0
      if (Array.isArray(title?.starting_scores) && title.starting_scores.length === 4) {
        return title.starting_scores.map(value => int(value, fallback))
      }
      if (title?.starting_score != null) return Array(4).fill(int(title.starting_score, fallback))
    }
    const finalByOriginal = [0, 0, 0, 0]
    for (let original = 0; original < 4; original += 1) {
      const player = this.detail.players.find((item) => item.original_player_index === original)
        || this.detail.players[original]
      finalByOriginal[original] = int(player?.score)
    }
    const totalChanges = [0, 0, 0, 0]
    for (const round of this.rounds) {
      const changes = this.scoreChangesByOriginal(round)
      for (let original = 0; original < 4; original += 1) totalChanges[original] += changes[original]
    }
    return finalByOriginal.map((score, original) => score - totalChanges[original])
  }

  private scoresAt(roundIndex: number, node: number): number[] {
    const finalScores = this.detail.record.game_title?.riichi_final_scores
    const ticks = this.rounds[roundIndex].action_ticks || []
    if (roundIndex === this.rounds.length - 1 && node === ticks.length && ticks.at(-1)?.[0] === 'end'
      && Array.isArray(finalScores) && finalScores.length === 4) {
      return finalScores.map(value => int(value))
    }
    const byOriginal = [...this.startingScoresByOriginal]
    for (let index = 0; index < roundIndex; index += 1) {
      const changes = this.scoreChangesByOriginal(this.rounds[index])
      for (let original = 0; original < 4; original += 1) byOriginal[original] += changes[original]
    }
    const current = this.scoreChangesByOriginal(this.rounds[roundIndex], node)
    for (let original = 0; original < 4; original += 1) byOriginal[original] += current[original]
    return byOriginal
  }

  playerForSeat(round: RecordRound, seat: number): PublicRecordPlayer | undefined {
    const original = this.seatsOf(round).findIndex((mappedSeat) => mappedSeat === seat)
    return this.detail.players.find((player) => player.original_player_index === original)
      || this.detail.players[original]
  }

  roundScoreChangesByOriginal(roundIndex: number): number[] {
    const safeRoundIndex = Math.max(0, Math.min(this.rounds.length - 1, roundIndex))
    return this.scoreChangesByOriginal(this.rounds[safeRoundIndex])
  }

  initialHandsAt(roundIndex: number): number[][] {
    const safeRoundIndex = Math.max(0, Math.min(this.rounds.length - 1, roundIndex))
    const round = this.rounds[safeRoundIndex]
    return [0, 1, 2, 3].map((seat) => (
      ((round[`p${seat}_tiles` as keyof RecordRound] as number[] | undefined) || [])
        .map(salasasaTileToMmcr)
    ))
  }

  xunmuNodes(roundIndex: number, viewerOriginal = 0): number[] {
    const safeRoundIndex = Math.max(0, Math.min(this.rounds.length - 1, roundIndex))
    const round = this.rounds[safeRoundIndex]
    const ticks = round.action_ticks || []
    const selectedSeat = this.seatsOf(round)[Math.max(0, Math.min(3, viewerOriginal))] ?? 0
    let currentPlayer = int(round.start_player_index, 0)
    const sichuan = isSichuanRecord(this.detail)
    const bloodBattle = isSichuanBloodBattle(this.detail)
    const retired = new Set<number>()
    const nodes = [0]
    for (let node = 0; node < ticks.length; node += 1) {
      const tick = ticks[node]
      const action = String(tick?.[0] ?? '')
      if (!action) continue
      if (action === 'reset') {
        currentPlayer = int(tick[1], currentPlayer)
        continue
      }
      if (action === 'bh' || action === 'bd') {
        currentPlayer = tick.length >= 3 ? int(tick[2], currentPlayer) : currentPlayer
        continue
      }
      if (action === 'c') {
        if (currentPlayer === selectedSeat && node > 0) nodes.push(node)
        currentPlayer = sichuan ? nextSichuanPlayer(currentPlayer, retired) : (currentPlayer + 1) % 4
      } else if (sichuan && ['hu_self', 'hu_first', 'hu_second', 'hu_third'].includes(action)
        && !(Array.isArray(tick[3]) && tick[3].includes('错和'))) {
        const winner = int(tick[1], currentPlayer)
        if (bloodBattle) retired.add(winner)
        currentPlayer = nextSichuanPlayer(winner, retired)
      } else if (['cl', 'cm', 'cr', 'p', 'g'].includes(action)) {
        currentPlayer = int(tick[2], currentPlayer)
      }
    }
    return nodes
  }

  /** Convert one stored record node to the same event shape used by a live 2D game. */
  eventForStep(
    roundIndex: number,
    currentNode: number,
    viewerOriginal = 0,
    revealAllHands = true,
  ): Record<string, any> | null {
    const safeRoundIndex = Math.max(0, Math.min(this.rounds.length - 1, roundIndex))
    const round = this.rounds[safeRoundIndex]
    const ticks = round.action_ticks || []
    if (currentNode < 0 || currentNode >= ticks.length) return null
    const tick = ticks[currentNode]
    const action = String(tick?.[0] ?? '')
    if (!action) return null

    const before = this.build(safeRoundIndex, currentNode, viewerOriginal, revealAllHands)
    const after = this.build(safeRoundIndex, currentNode + 1, viewerOriginal, revealAllHands)
    const explicitActorActions = ['bh', 'bd', 'cl', 'cm', 'cr', 'p', 'g']
    let actorSeat = explicitActorActions.includes(action) && tick.length >= 3
      ? int(tick[2], before.snapshot.state.current_player ?? 0)
      : int(before.snapshot.state.current_player, 0)
    if (['hu_self', 'hu_first', 'hu_second', 'hu_third', 'hu_riichi', 'riichi', 'rk'].includes(action)) {
      actorSeat = int(tick[1], actorSeat)
    }
    if (actorSeat < 0 || actorSeat > 3) actorSeat = 0

    const kind = sceneKindForAction(action === 'hu_riichi' ? String(tick[2]) : action)
    if (RECORD_SILENT_ACTIONS.has(action)) return null
    // 暗扣切牌直接呈现后快照，不能用实际牌值播放普通亮牌动画。
    if (action === 'c' && tick[3] === 'C') return null

    const event: Record<string, any> = {
      kind,
      actor_seat: actorSeat,
      silent: false,
    }
    if (action === 'rk') { event.tile = salasasaTileToMmcr(int(tick[2])); event.use_drawn_tile = int(tick[3]) !== 0; event.silent = true }
    if (tick.length > 1 && ['d', 'gd', 'bd', 'c', 'bh', 'cl', 'cm', 'cr', 'p', 'g', 'ag', 'jg'].includes(action)) {
      event.tile = salasasaTileToMmcr(int(tick[1]))
    }
    // chowFromRiver receives the sequence's central tile, while a stored
    // cl/cm/cr tick records the claimed discard. Convert the latter just as
    // the live Salasasa adapter does, otherwise it searches the hand for the
    // wrong two tiles and aborts without creating a meld.
    if (action === 'cl') event.tile = salasasaTileToMmcr(normalizedTile(int(tick[1])) - 1)
    if (action === 'cr') event.tile = salasasaTileToMmcr(normalizedTile(int(tick[1])) + 1)
    if (action === 'c') event.use_drawn_tile = bool(tick[2])
    if (action === 'bh') event.use_drawn_tile = tick.length >= 4 && bool(tick[3])
    if (['cl', 'cm', 'cr', 'p', 'g'].includes(action)) {
      event.discarder_seat = before.snapshot.state.last_discarder
    }
    if (action === 'bd') {
      const startPlayer = int(round.start_player_index, 0)
      event.settle_drawn_tile = !hasResetBefore(ticks, currentNode)
        && actorSeat !== startPlayer
    }
    if (action === 'cl') event.ui64_value = 3
    if (action === 'cm') event.ui64_value = 2
    if (action === 'cr') event.ui64_value = 1
    if (action === 'ag' || action === 'jg') {
      const actorState = before.snapshot.seats.find((seat) => seat.seat_index === actorSeat)
      event.use_drawn_tile = actorState?.drawn_tile != null
        && actorState.drawn_tile === event.tile
    }
    if (action === 'ag') {
      const meld = after.snapshot.seats.find((seat) => seat.seat_index === actorSeat)?.melds.at(-1)
      if (meld?.concealed_face_down) event.concealed_face_down = meld.concealed_face_down
    }
    if (kind === 'self_drawn_win' || kind === 'discard_win') {
      const beforeActor = before.snapshot.seats.find((seat) => seat.seat_index === actorSeat)
      const afterActor = after.snapshot.seats.find((seat) => seat.seat_index === actorSeat)
      event.tile = kind === 'self_drawn_win' ? beforeActor?.drawn_tile ?? undefined : undefined
      event.revealed_hand_tiles = [
        ...(afterActor?.hand_tiles || beforeActor?.hand_tiles || []),
        ...(afterActor?.drawn_tile != null ? [afterActor.drawn_tile] : []),
      ]
    }
    if (kind === 'drawn_game') event.suppress_result_display = false

    const nextState = { ...after.snapshot.state }
    const seatStatus = after.snapshot.seats.map((seat) => ({
      seat_index: seat.seat_index,
      duplicate_remaining_tile_count: seat.duplicate_remaining_tile_count,
      user_id: seat.player_id,
      username: seat.username,
      score: seat.score,
      rank: seat.rank,
      afk: false,
      disconnected: false,
    }))
    return {
      category: 'transition',
      event,
      viewer: after.snapshot.viewer,
      seat_status: seatStatus,
      state: nextState,
      reveal_all_hands: revealAllHands,
    }
  }

  build(
    roundIndex: number,
    requestedNode: number,
    viewerOriginal = 0,
    revealAllHands = true,
  ): ReplayPosition {
    const safeRoundIndex = Math.max(0, Math.min(this.rounds.length - 1, roundIndex))
    const round = this.rounds[safeRoundIndex]
    const ticks = round.action_ticks || []
    const node = Math.max(0, Math.min(ticks.length, requestedNode))
    const seatMap = this.seatsOf(round)
    const scoresByOriginal = this.scoresAt(safeRoundIndex, node)
    const states: ReplaySeatState[] = [0, 1, 2, 3].map((seat) => {
      const initial = (round[`p${seat}_tiles` as keyof RecordRound] as number[] | undefined) || []
      const startHasDrawn = initial.length % 3 === 2
      const original = seatMap.findIndex((mappedSeat) => mappedSeat === seat)
      return {
        hand: startHasDrawn ? initial.slice(0, -1) : [...initial],
        drawn: startHasDrawn ? initial.at(-1) ?? null : null,
        score: scoresByOriginal[original] || 0,
        river: [],
        riverDrawn: [],
        melds: [],
        flowers: [],
      }
    })
    const startPlayer = int(round.start_player_index, 0)
    let currentPlayer = startPlayer
    let lastActor: number | null = null
    let lastDiscardPlayer = -1
    let lastWinnableTile: number | null = null
    let remaining = Array.isArray(round.duplicate_walls) ? round.duplicate_walls.flat().length : (round.tiles_list || []).length
    const changchun = isChangchunRecord(this.detail)
    const shanxi = this.detail.record.game_title?.rule === 'shanxi' || this.detail.rule === 'shanxi'
    const guizhou = this.detail.record.game_title?.rule === 'guizhou' || this.detail.rule === 'guizhou'
    const yixing = this.detail.record.game_title?.rule === 'yixing' || this.detail.rule === 'yixing'
    const wenzhou = this.detail.record.game_title?.rule === 'wenzhou' || this.detail.rule === 'wenzhou'
    const hangzhou = this.detail.record.game_title?.rule === 'hangzhou' || this.detail.rule === 'hangzhou'
    const hongzhong = this.detail.record.game_title?.rule === 'hongzhong' || this.detail.rule === 'hongzhong'
    const sichuan = isSichuanRecord(this.detail)
    const bloodBattle = isSichuanBloodBattle(this.detail)
    const bloodFlow = isSichuanBloodFlow(this.detail)
    const retired = new Set<number>()
    const firstDiscards = [false, false, false, false]
    const initialQuads = states.map((state) => {
      const hand = [...state.hand, ...(state.drawn == null ? [] : [state.drawn])]
      return new Set(hand.filter((tile) => hand.filter((t) => t === tile).length === 4))
    })
    let shanxiTailSingle = false
    let seenReset = false
    const guangdongMil = isGuangdongMilRecord(this.detail)
    const isGuangdong = this.detail.rule === 'guangdong' || this.detail.record.game_title?.rule === 'guangdong'
    const isTuidao = isGuangdong && !guangdongMil
    if (isTuidao) remaining = Math.max(0, remaining - 13)
    let addedKongSeat = -1

    for (const tick of ticks.slice(0, node)) {
      const action = String(tick[0] ?? '')
      if (!action || ['ask_hand', 'ask_other', 'ca', 'tuidao', 'state'].includes(action)) continue
      if (action === 'cc' && changchun) {
        const event = tick[1] as any
        if (event && typeof event === 'object') {
          applyChangchunPhysical(states, event, salasasaTileToMmcr)
          if (event.kind === 'rob_claim') { lastDiscardPlayer = event.player; lastWinnableTile = event.tile }
          if (event.kind === 'added_robbed') { addedKongSeat = event.player; lastWinnableTile = event.tile; lastDiscardPlayer = -1 }
          if (['special', 'added_offer', 'added_commit'].includes(event.kind)) currentPlayer = event.player
          if (event.kind === 'tail_pass') currentPlayer = (event.player + 1) % 4
          if (Number.isInteger(event.player)) lastActor = event.player
        }
        continue
      }
      if (action === 'guangdong') {
        if (guangdongMil && tick[1] === 'horses') {
          const horses = tick[2] as { tiles?: unknown[] } | null
          remaining = Math.max(0, remaining - (Array.isArray(horses?.tiles) ? horses.tiles.length : 0))
        }
        continue
      }
      if (action === 'reset') {
        currentPlayer = int(tick[1], currentPlayer)
        seenReset = true
        continue
      }
      if (action === 'hongzhong') {
        if (hongzhong && tick[1] === 'birds') {
          const birds = tick[2] as { tiles?: unknown[] } | null
          remaining = Math.max(0, remaining - (Array.isArray(birds?.tiles) ? birds.tiles.length : 0))
        }
        continue
      }
      if (action === 'hangzhou') {
        if (hangzhou && tick[1] === 'tail_burn') remaining = Math.max(0, remaining - 1)
        continue
      }
      if (action === 'wenzhou') {
        if (tick[1] === 'meld') {
          const actor = int(tick[2]), mask = tick[3] as number[], code = String(tick[4] || '')
          const state = states[actor], rawTile = int(code.slice(1))
          if (state && Array.isArray(mask) && state.melds.length) {
            const prefix = code[0]
            const logical = salasasaTileToMmcr(rawTile)
            let index = prefix === 'g' ? state.melds.findIndex(m => m.tile === logical && (m.type === 'kong' || m.type === 'triplet')) : -1
            if (index < 0) index = state.melds.length - 1
            const previous = state.melds[index]
            state.melds[index] = { ...previous, tile: salasasaTileToMmcr(rawTile),
              type: prefix === 's' ? 'sequence' : prefix === 'k' ? 'triplet' : 'kong',
              concealed: prefix === 'G', physical_mask: mask.map((value, i) => i % 2 ? salasasaTileToMmcr(value) : value),
              concealed_face_down: mask.filter((_, i) => i % 2 === 0).map(flag => flag === 2),
            }
          }
        } else if (tick[1] === 'kong_claim_source') {
          const actor = int(tick[2]), tile = int(tick[3]), source = states[actor]
          if (source) {
            if (source.drawn === tile && bool(tick[4])) source.drawn = null
            else removeExactOrNormalized(source.hand, tile)
            if (source.drawn != null) { source.hand.push(source.drawn); source.drawn = null }
            source.river.push(tile); source.riverDrawn.push(bool(tick[4]))
            lastDiscardPlayer = actor; lastWinnableTile = tile; addedKongSeat = -1
          }
        } else if (tick[1] === 'win_source') {
          const payer = int(tick[3], -1), tile = int(tick[5])
          lastWinnableTile = tile
          if (payer >= 0 && payer < 4 && tick[4] === 'rob_kong') {
            const source = states[payer]
            if (source.drawn === tile) source.drawn = null
            else removeExactOrNormalized(source.hand, tile)
            addedKongSeat = payer
          }
        }
        continue
      }
      if (action === 'yixing') {
        if (tick[1] === 'win_source') {
          const payer = int(tick[3], -1), tile = int(tick[5])
          lastWinnableTile = tile
          if (payer >= 0 && payer < 4 && tick[4] === 'rob_kong') {
            const source = states[payer]
            if (source.drawn === tile) source.drawn = null
            else removeExactOrNormalized(source.hand, tile)
            addedKongSeat = payer
          }
        }
        continue
      }
      if (action === 'guizhou') {
        if (tick[1] === 'reveal_kongs' && Array.isArray(tick[2])) {
          const masks = tick[2] as number[][][]
          states.forEach((state, seat) => state.melds.forEach((meld, index) => {
            if (meld.concealed && masks[seat]?.[index])
              meld.concealed_face_down = masks[seat][index].filter((_, i) => i % 2 === 0).map((flag) => flag === 2)
          }))
        }
        if (tick[1] === 'win_source') {
          const winner = int(tick[2], -1), payer = tick[3] == null ? -1 : int(tick[3], -1), tile = int(tick[5])
          lastWinnableTile = tile
          if (payer >= 0 && payer < 4 && bool(tick[6])) {
            const source = states[payer]
            if (tick[4] === 'rob_kong') {
              if (source.drawn === tile) source.drawn = null
              else removeExactOrNormalized(source.hand, tile)
            } else if (tick[4] === 'discard') {
              source.river.pop(); source.riverDrawn.pop()
            }
          }
          lastActor = payer >= 0 ? payer : winner
        }
        continue
      }
      const explicitActorActions = ['bh', 'bd', 'cl', 'cm', 'cr', 'p', 'g']
      let actor = explicitActorActions.includes(action) && tick.length >= 3
        ? int(tick[2], currentPlayer)
        : currentPlayer
      if (['hu_self', 'hu_first', 'hu_second', 'hu_third', 'hu_riichi', 'riichi', 'rk'].includes(action)) actor = int(tick[1], currentPlayer)
      if (actor < 0 || actor > 3) actor = currentPlayer
      const state = states[actor]
      lastActor = actor

      if (['d', 'gd', 'bd'].includes(action)) {
        addedKongSeat = -1
        if (state.drawn != null) state.hand.push(state.drawn)
        const drawnTile = int(tick[1])
        lastWinnableTile = drawnTile
        if (action === 'bd' && !seenReset && actor !== startPlayer) {
          state.hand.push(drawnTile)
          state.drawn = null
        } else {
          state.drawn = drawnTile
        }
        remaining = Math.max(0, remaining - 1)
        if (shanxi && action === 'gd') shanxiTailSingle = !shanxiTailSingle
        currentPlayer = actor
      } else if (action === 'c') {
        firstDiscards[actor] = true
        initialQuads[actor].clear()
        addedKongSeat = -1
        const tile = int(tick[1])
        const fromDraw = bool(tick[2])
        lastWinnableTile = tile
        const drawnMatches = state.drawn != null
          && normalizedTile(state.drawn) === normalizedTile(tile)
        if (fromDraw && drawnMatches) {
          state.drawn = null
        } else {
          const removed = removeExactOrNormalized(state.hand, tile)
          // Same opening-dealer case as buhua: the 2D snapshot parks tile 14
          // in the draw slot, while the first cut is recorded as a hand cut (F).
          if (removed == null && drawnMatches) state.drawn = null
          else if (state.drawn != null) {
            state.hand.push(state.drawn)
            state.drawn = null
          }
        }
        state.river.push(tick[3] === 'C' ? 0 : tile)
        state.riverDrawn.push(fromDraw)
        lastDiscardPlayer = actor
        currentPlayer = sichuan ? nextSichuanPlayer(actor, retired) : (actor + 1) % 4
      } else if (action === 'bh') {
        const tile = int(tick[1])
        const fromDraw = tick.length >= 4 && bool(tick[3])
        const drawnMatches = state.drawn != null
          && normalizedTile(state.drawn) === normalizedTile(tile)
        if (fromDraw && drawnMatches) {
          state.drawn = null
        } else {
          const removed = removeExactOrNormalized(state.hand, tile)
          // The opening dealer starts with 14 tiles. The 2D snapshot separates
          // tile 14 into the draw slot, while opening buhua is recorded as a
          // hand replacement (F). Unity keeps all 14 tiles in one list, so its
          // fallback removal also finds a flower in the final slot. Mirror that
          // behavior when rebuilding a replay node.
          if (removed == null && drawnMatches) state.drawn = null
        }
        const recipient = tick.length >= 5 ? int(tick[4], actor) : actor
        if (recipient >= 0 && recipient < 4) states[recipient].flowers.push(tile)
        currentPlayer = actor
      } else if (['cl', 'cm', 'cr', 'p', 'g'].includes(action)) {
        const tile = int(tick[1])
        initialQuads[actor].clear()
        takeForClaim(tick, action, tile, state.hand)
        if (state.drawn != null) {
          state.hand.push(state.drawn)
          state.drawn = null
        }
        if (lastDiscardPlayer >= 0) {
          states[lastDiscardPlayer].river.pop()
          states[lastDiscardPlayer].riverDrawn.pop()
        }
        const type = action === 'p' ? 'triplet' : action === 'g' ? 'kong' : 'sequence'
        const meldTile = action === 'cl'
          ? normalizedTile(tile) - 1
          : action === 'cr'
            ? normalizedTile(tile) + 1
            : normalizedTile(tile)
        const fromRel = lastDiscardPlayer >= 0 ? (actor - lastDiscardPlayer + 4) % 4 : 1
        state.melds.push({
          tile: salasasaTileToMmcr(meldTile),
          type,
          chow_mode: action === 'cl' ? 3 : action === 'cm' ? 2 : action === 'cr' ? 1 : 0,
          meld_from_rel: fromRel || 1,
        })
        lastDiscardPlayer = -1
        currentPlayer = actor
      } else if (action === 'ag') {
        const tile = int(tick[1])
        const hiddenOpening = guizhou && !firstDiscards[actor] && initialQuads[actor].has(tile)
          && firstDiscards.some((discarded, seat) => seat !== actor && !discarded)
        initialQuads[actor].delete(tile)
        const explicit = tick.slice(3, 7).map((value) => int(value)).filter((value) => value > 0)
        const removed = explicit.length === 4 ? explicit : Array(4).fill(tile)
        for (const item of removed) {
          if (state.drawn != null && normalizedTile(state.drawn) === normalizedTile(item)) state.drawn = null
          else removeExactOrNormalized(state.hand, item)
        }
        // A kong made from the old hand ends the current draw slot too. Keep
        // that unrelated physical draw in the hand until the supplement arrives.
        if (hongzhong && state.drawn != null) {
          state.hand.push(state.drawn)
          state.drawn = null
        }
        state.melds.push({
          tile: salasasaTileToMmcr(normalizedTile(tile)),
          type: 'kong',
          concealed: true,
          ...(guizhou ? { concealed_face_down: [true, hiddenOpening, hiddenOpening, true] } : (hangzhou || changchun || wenzhou) ? { concealed_face_down: [true, true, true, true] } : yixing ? { concealed_face_down: [true, false, true, true] } : (hongzhong || guangdongMil) ? { concealed_face_down: [true, false, false, true] } : {}),
          chow_mode: 0,
          meld_from_rel: 0,
        })
        currentPlayer = actor
      } else if (action === 'rk') {
        const tile = int(tick[2])
        lastWinnableTile = tile
        if (sichuan) addedKongSeat = actor
        if (int(tick[3]) !== 0 && state.drawn === tile) state.drawn = null
        else removeExactOrNormalized(state.hand, tile)
        currentPlayer = actor
      } else if (action === 'jg') {
        const physical = wenzhou ? int(tick[3], int(tick[1])) : int(tick[1])
        const tile = wenzhou && int(tick[1]) === 46 && int(round.wenzhou?.caishen) !== 46 ? int(round.wenzhou?.white_natural) : int(tick[1])
        if (isGuangdong || wenzhou || sichuan) { lastWinnableTile = physical; addedKongSeat = actor }
        if (state.drawn != null && normalizedTile(state.drawn) === normalizedTile(physical)) state.drawn = null
        else removeExactOrNormalized(state.hand, physical)
        if (hongzhong && state.drawn != null) {
          state.hand.push(state.drawn)
          state.drawn = null
        }
        const meld = state.melds.find((item) =>
          item.type === 'triplet' && item.tile === salasasaTileToMmcr(normalizedTile(tile)))
        if (meld) {
          meld.type = 'kong'
          meld.meld_from_rel += 4
        }
        currentPlayer = actor
      } else if (sichuan && ['hu_self', 'hu_first', 'hu_second', 'hu_third'].includes(action)) {
        const fans = Array.isArray(tick[3]) ? tick[3] : []
        if (fans.includes('错和')) continue
        const selfDrawn = action === 'hu_self'
        const tile = int(tick[5], lastWinnableTile ?? 0)
        if (!selfDrawn) {
          const source = int(tick[7], addedKongSeat >= 0 ? addedKongSeat : lastDiscardPlayer)
          const multi = bool(tick[6])
          const recycle = tick.length >= 9 ? bool(tick[8]) : !multi
          const robbed = addedKongSeat >= 0 && addedKongSeat === source
            || fans.some(fan => ['抢杠', '抢杠和', 'chankan'].includes(String(fan)))
          if (source >= 0 && source < 4 && robbed) {
            // The committed fourth tile is restored to a pung once; subsequent
            // winners must not consume an unrelated river tile instead.
            const meld = states[source].melds.find(item => item.type === 'kong'
              && item.meld_from_rel >= 4 && item.tile === salasasaTileToMmcr(normalizedTile(tile)))
            if (meld) { meld.type = 'triplet'; meld.meld_from_rel -= 4 }
          } else if (source >= 0 && source < 4 && recycle) {
            const river = states[source].river
            if (river.at(-1) === tile) {
              river.pop(); states[source].riverDrawn.pop()
              if (lastDiscardPlayer === source) lastDiscardPlayer = -1
            }
          }
        }
        if (bloodFlow) {
          // Blood-flow wins keep the waiting hand active. Winning tiles live in
          // the public win/flower area instead of becoming another held draw.
          if (selfDrawn) state.drawn = null
          if (tile > 0) state.flowers.push(tile)
        }
        if (bloodBattle) retired.add(actor)
        currentPlayer = nextSichuanPlayer(actor, retired)
      } else if ((isGuangdong || shanxi || yixing || wenzhou || changchun) && ['hu_first', 'hu_second', 'hu_third'].includes(action)) {
        const tile = int(tick[5], lastWinnableTile ?? 0)
        if (tile > 0 && state.drawn == null) state.drawn = tile
        if (addedKongSeat >= 0 && (guangdongMil || yixing || wenzhou || (Array.isArray(tick[3]) && tick[3].includes('抢杠')))) {
          const meld = states[addedKongSeat].melds.find(item => item.type === 'kong'
            && item.meld_from_rel >= 4 && item.tile === salasasaTileToMmcr(normalizedTile(tile)))
          if (meld) { meld.type = 'triplet'; meld.meld_from_rel -= 4 }
        } else if (lastDiscardPlayer >= 0) {
          states[lastDiscardPlayer].river.pop(); states[lastDiscardPlayer].riverDrawn.pop()
        }
        addedKongSeat = -1
      } else if (guizhou && ['hu_first', 'hu_second', 'hu_third'].includes(action)) {
        // Source metadata can precede the visible win by one replay node.
        // Add the display tile when that winner's hu tick is consumed.
        if (lastWinnableTile != null && state.drawn == null) state.drawn = lastWinnableTile
      } else if (action === 'hu_riichi' && ['hu_first', 'hu_second', 'hu_third'].includes(String(tick[2]))) {
        const yaku = Array.isArray(tick[5]) ? tick[5] : []
        if (!yaku.includes('错和') && lastWinnableTile != null && state.hand.length % 3 === 1 && state.drawn == null) {
          state.drawn = lastWinnableTile
        }
      }
    }

    const viewerSeat = seatMap[Math.max(0, Math.min(3, viewerOriginal))] ?? 0
    const duplicateWall = duplicateReplayWall(round, node, this.detail.record.game_title?.duplicate_rules_version)
    const duplicateRemaining = duplicateWall ? [0, 0, 0, 0] : undefined
    if (duplicateRemaining) {
      for (const item of duplicateWall!) {
        if (!item.consumed) duplicateRemaining[item.originalPlayer] += 1
      }
    }
    const seats: SeatSnapshot[] = states.map((state, seat) => {
      const player = this.playerForSeat(round, seat)
      return {
        seat_index: seat,
        duplicate_remaining_tile_count: duplicateRemaining?.[seatMap.indexOf(seat)],
        score: state.score,
        afk: false,
        hand_tile_count: state.hand.length,
        has_drawn_tile: state.drawn != null,
        player_id: player?.user_id ?? null,
        username: player?.username ?? `玩家 ${seat + 1}`,
        voice_id: int(player?.voice_used, 1),
        discard_pile: state.river.map(salasasaTileToMmcr),
        discard_drawn_flags: state.riverDrawn,
        melds: state.melds,
        flower_tiles: state.flowers.map(salasasaTileToMmcr),
        hand_tiles: state.hand.map(salasasaTileToMmcr),
        drawn_tile: state.drawn == null ? null : salasasaTileToMmcr(state.drawn),
      }
    })
    return {
      actionLabel: actionName(node > 0 ? ticks[node - 1] : undefined),
      snapshot: {
        phase: 'active',
        session_id: safeRoundIndex + 1,
        state: {
          round_counter: int(round.current_round, safeRoundIndex + 1),
          stage_counter: node,
          remaining_tile_count: changchun ? changchunWallAt(round, node).playable : Math.max(0, remaining - (shanxi ? 14 + Number(shanxiTailSingle) : wenzhou ? 4 : hangzhou ? 20 : 0)),
          current_player: currentPlayer,
          last_actor: lastActor,
          last_discarder: lastDiscardPlayer >= 0 ? lastDiscardPlayer : null,
          last_event_kind: node > 0 ? sceneKindForAction(String(ticks[node - 1]?.[0] ?? '')) : 'round_start',
        },
        seats,
        viewer: {
          seat_index: viewerSeat,
          pending: undefined,
          decision_timer_ms: null,
          available_actions: [],
        },
        reveal_all_hands: revealAllHands,
      },
    }
  }

  private replacementTileIndex(length: number, secondFromBack: boolean): number {
    // MIL 推倒和的杠后补牌取墙尾，必须与服务端及 Unity 清单一致。
    if (this.detail.rule === 'guangdong' || this.detail.record.game_title?.rule === 'guangdong') return length - 1
    if (this.detail.rule === 'yixing' || this.detail.record.game_title?.rule === 'yixing') return length - 1
    if (this.detail.rule === 'wenzhou' || this.detail.record.game_title?.rule === 'wenzhou') return length - 1
    if (this.detail.rule === 'hangzhou' || this.detail.record.game_title?.rule === 'hangzhou') return length - 1
    return secondFromBack && length > 1 ? length - 2 : length - 1
  }

  hongzhongHintsAt(roundIndex: number, requestedNode: number, seat: number): Record<string, unknown> | null {
    if ((this.detail.rule !== 'hongzhong' && this.detail.record.game_title?.rule !== 'hongzhong') || !Number.isInteger(seat) || seat < 0 || seat > 3) return null
    const safeRoundIndex = Math.max(0, Math.min(this.rounds.length - 1, roundIndex))
    const round = this.rounds[safeRoundIndex]
    const state = this.build(safeRoundIndex, requestedNode).snapshot.seats.find(item => item.seat_index === seat)
    if (!state || state.melds.some(meld => meld.type === 'sequence')) return null
    const hand = [...(state.hand_tiles || []), ...(state.drawn_tile == null ? [] : [state.drawn_tile])].map(mmcrTileToSalasasa)
    const melds = state.melds.map(meld => `${meld.type === 'triplet' ? 'k' : meld.concealed ? 'G' : 'g'}${mmcrTileToSalasasa(meld.tile)}`)
    return selectHongzhongHints(round, requestedNode, seat, hand, melds)
  }

  remainingWallAt(roundIndex: number, requestedNode: number): number[] {
    const safeRoundIndex = Math.max(0, Math.min(this.rounds.length - 1, roundIndex))
    const round = this.rounds[safeRoundIndex]
    const duplicate = duplicateReplayWall(round, requestedNode, this.detail.record.game_title?.duplicate_rules_version)
    if (duplicate) return duplicate.filter(item => !item.consumed).map(item => salasasaTileToMmcr(item.tile))
    const ticks = round.action_ticks || []
    const node = Math.max(0, Math.min(ticks.length, requestedNode))
    if (isChangchunRecord(this.detail)) return changchunWallAt(round, node).remaining.map(salasasaTileToMmcr)
    const sichuan = isSichuanRecord(this.detail)
    const wall = [...(round.tiles_list || [])]
    let useSecondFromBack = true
    for (const tick of ticks.slice(0, node)) {
      const action = String(tick?.[0] ?? '')
      if (action === 'd') {
        if (wall.length) wall.shift()
      } else if (action === 'guangdong' && tick[1] === 'horses' && isGuangdongMilRecord(this.detail)) {
        const horses = tick[2] as { tiles?: unknown[] } | null
        if (Array.isArray(horses?.tiles)) wall.splice(0, horses.tiles.length)
      } else if (action === 'hongzhong' && tick[1] === 'birds' && (this.detail.rule === 'hongzhong' || this.detail.record.game_title?.rule === 'hongzhong')) {
        const birds = tick[2] as { tiles?: unknown[] } | null
        if (Array.isArray(birds?.tiles)) wall.splice(0, birds.tiles.length)
      } else if (action === 'hangzhou' && tick[1] === 'tail_burn' && (this.detail.rule === 'hangzhou' || this.detail.record.game_title?.rule === 'hangzhou')) {
        if (wall.length > 1) wall.splice(wall.length - 2, 1)
      } else if (action === 'gd' || action === 'bd') {
        if (wall.length) {
          const index = action === 'gd' && sichuan ? 0
            : this.replacementTileIndex(wall.length, useSecondFromBack)
          wall.splice(index, 1)
          useSecondFromBack = !useSecondFromBack
        }
      }
    }
    return wall.map(salasasaTileToMmcr)
  }

  wallViewAt(roundIndex: number, requestedNode: number): ReplayWallTile[] {
    const safeRoundIndex = Math.max(0, Math.min(this.rounds.length - 1, roundIndex))
    const round = this.rounds[safeRoundIndex]
    const duplicate = duplicateReplayWall(round, requestedNode, this.detail.record.game_title?.duplicate_rules_version)
    if (duplicate) return duplicate.map(item => ({ ...item, tile: salasasaTileToMmcr(item.tile) }))
    const ticks = round.action_ticks || []
    const node = Math.max(0, Math.min(ticks.length, requestedNode))
    if (isChangchunRecord(this.detail)) return changchunWallAt(round, node).wall.map(item => ({ ...item, tile: salasasaTileToMmcr(item.tile) }))
    const sichuan = isSichuanRecord(this.detail)
    const original = round.tiles_list || []
    const remainingIndices = original.map((_, index) => index)
    const consumed = new Set<number>()
    let useSecondFromBack = true
    for (const tick of ticks.slice(0, node)) {
      const action = String(tick?.[0] ?? '')
      if (action === 'd' && remainingIndices.length) {
        consumed.add(remainingIndices.shift()!)
      } else if (action === 'guangdong' && tick[1] === 'horses' && isGuangdongMilRecord(this.detail)) {
        const horses = tick[2] as { tiles?: unknown[] } | null
        if (Array.isArray(horses?.tiles)) for (const index of remainingIndices.splice(0, horses.tiles.length)) consumed.add(index)
      } else if (action === 'hongzhong' && tick[1] === 'birds' && (this.detail.rule === 'hongzhong' || this.detail.record.game_title?.rule === 'hongzhong')) {
        const birds = tick[2] as { tiles?: unknown[] } | null
        if (Array.isArray(birds?.tiles)) for (const index of remainingIndices.splice(0, birds.tiles.length)) consumed.add(index)
      } else if (action === 'hangzhou' && tick[1] === 'tail_burn' && (this.detail.rule === 'hangzhou' || this.detail.record.game_title?.rule === 'hangzhou')) {
        if (remainingIndices.length > 1) consumed.add(remainingIndices.splice(remainingIndices.length - 2, 1)[0])
      } else if ((action === 'gd' || action === 'bd') && remainingIndices.length) {
        const index = action === 'gd' && sichuan ? 0
          : this.replacementTileIndex(remainingIndices.length, useSecondFromBack)
        consumed.add(remainingIndices.splice(index, 1)[0])
        useSecondFromBack = !useSecondFromBack
      }
    }
    return original.map((tile, index) => ({
      tile: salasasaTileToMmcr(tile),
      consumed: consumed.has(index),
    }))
  }
}
