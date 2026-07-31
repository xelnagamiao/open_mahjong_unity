import type {
  ActiveSessionSnapshot,
  MeldSnapshot,
  SeatSnapshot,
} from '../game/scene/types'
import { salasasaTileToMmcr } from '../salasasa/gameAdapter'

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
  return tile >= 100 ? tile % 100 : tile
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
  if (['hu_self', 'hu_first', 'hu_second', 'hu_third'].includes(action)) value = tick[4]
  else if (action === 'hu_riichi') value = tick[6]
  else if (action === 'ryuukyoku') value = tick[2]
  if (!Array.isArray(value) || value.length < 4) return null
  return value.slice(0, 4).map((item) => int(item))
}

function actionName(tick: RecordTick | undefined): string {
  if (!tick?.length) return '局初'
  const names: Record<string, string> = {
    d: '摸牌', gd: '杠后摸牌', bd: '补花摸牌', c: '出牌', bh: '补花',
    cl: '吃', cm: '吃', cr: '吃', p: '碰', g: '明杠', ag: '暗杠', jg: '加杠',
    hu_self: '自摸', hu_first: '和牌', hu_second: '和牌', hu_third: '和牌',
    liuju: '流局', ryuukyoku: '流局', end: '本局结束',
  }
  return names[String(tick[0])] || String(tick[0])
}

function sceneKindForAction(action: string): string {
  const kinds: Record<string, string> = {
    d: 'draw_tile', gd: 'draw_tile', bd: 'draw_tile', c: 'discard_tile', bh: 'flower',
    cl: 'chow', cm: 'chow', cr: 'chow', p: 'pung', g: 'melded_kong',
    ag: 'concealed_kong', jg: 'added_kong', hu_self: 'self_drawn_win',
    hu_first: 'discard_win', hu_second: 'discard_win', hu_third: 'discard_win',
    liuju: 'drawn_game', ryuukyoku: 'drawn_game', end: 'end',
  }
  return kinds[action] || action
}

function mainPhaseStartedBefore(ticks: RecordTick[], node: number): boolean {
  const openingActions = new Set(['bh', 'bd', 'ask_hand', 'ask_other', 'ca'])
  for (let index = 0; index < node; index += 1) {
    const action = String(ticks[index]?.[0] ?? '')
    if (action && !openingActions.has(action)) return true
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
      const bySeat = scoreChangeFromTick(tick)
      if (!bySeat) continue
      for (let original = 0; original < 4; original += 1) result[original] += bySeat[seats[original]] || 0
    }
    return result
  }

  private computeStartingScores(): number[] {
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
    let mainPhaseStarted = false
    const nodes = [0]
    const meta = new Set(['ask_hand', 'ask_other', 'ca', 'end', 'dora', 'riichi', 'state'])
    for (let node = 0; node < ticks.length; node += 1) {
      const tick = ticks[node]
      const action = String(tick?.[0] ?? '')
      if (!action) continue
      if (action === 'bh' || action === 'bd') {
        currentPlayer = tick.length >= 3 ? int(tick[2], currentPlayer) : currentPlayer
        continue
      }
      if (!mainPhaseStarted && !meta.has(action)) {
        currentPlayer = int(round.start_player_index, 0)
        mainPhaseStarted = true
      }
      if (action === 'c') {
        if (currentPlayer === selectedSeat && node > 0) nodes.push(node)
        currentPlayer = (currentPlayer + 1) % 4
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
    if (['hu_self', 'hu_first', 'hu_second', 'hu_third', 'hu_riichi'].includes(action)) {
      actorSeat = int(tick[1], actorSeat)
    }
    if (actorSeat < 0 || actorSeat > 3) actorSeat = 0

    const kind = sceneKindForAction(action)
    // ask/ca nodes are bookkeeping only. Advancing them should update the node
    // counter without replaying or fabricating a visible board action.
    if (['ask_hand', 'ask_other', 'ca'].includes(action)) return null

    const event: Record<string, any> = {
      kind,
      actor_seat: actorSeat,
      silent: false,
    }
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
    if (action === 'bd') {
      const startPlayer = int(round.start_player_index, 0)
      event.settle_drawn_tile = !mainPhaseStartedBefore(ticks, currentNode)
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
    let remaining = (round.tiles_list || []).length
    let mainPhaseStarted = false

    for (const tick of ticks.slice(0, node)) {
      const action = String(tick[0] ?? '')
      if (!action || ['ask_hand', 'ask_other', 'ca'].includes(action)) continue
      if (!mainPhaseStarted && !['bh', 'bd'].includes(action)) {
        currentPlayer = startPlayer
        mainPhaseStarted = true
      }
      const explicitActorActions = ['bh', 'bd', 'cl', 'cm', 'cr', 'p', 'g']
      let actor = explicitActorActions.includes(action) && tick.length >= 3
        ? int(tick[2], currentPlayer)
        : currentPlayer
      if (['hu_self', 'hu_first', 'hu_second', 'hu_third', 'riichi'].includes(action)) actor = int(tick[1], currentPlayer)
      if (actor < 0 || actor > 3) actor = currentPlayer
      const state = states[actor]
      lastActor = actor

      if (['d', 'gd', 'bd'].includes(action)) {
        if (state.drawn != null) state.hand.push(state.drawn)
        const drawnTile = int(tick[1])
        if (action === 'bd' && !mainPhaseStarted && actor !== startPlayer) {
          state.hand.push(drawnTile)
          state.drawn = null
        } else {
          state.drawn = drawnTile
        }
        remaining = Math.max(0, remaining - 1)
        currentPlayer = actor
      } else if (action === 'c') {
        const tile = int(tick[1])
        const fromDraw = bool(tick[2])
        if (fromDraw && state.drawn != null && normalizedTile(state.drawn) === normalizedTile(tile)) {
          state.drawn = null
        } else {
          removeExactOrNormalized(state.hand, tile)
          if (state.drawn != null) {
            state.hand.push(state.drawn)
            state.drawn = null
          }
        }
        state.river.push(tile)
        state.riverDrawn.push(fromDraw)
        lastDiscardPlayer = actor
        currentPlayer = (actor + 1) % 4
      } else if (action === 'bh') {
        const tile = int(tick[1])
        const fromDraw = tick.length >= 4 && bool(tick[3])
        if (fromDraw && state.drawn != null) state.drawn = null
        else removeExactOrNormalized(state.hand, tile)
        const recipient = tick.length >= 5 ? int(tick[4], actor) : actor
        if (recipient >= 0 && recipient < 4) states[recipient].flowers.push(tile)
        currentPlayer = actor
      } else if (['cl', 'cm', 'cr', 'p', 'g'].includes(action)) {
        const tile = int(tick[1])
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
        currentPlayer = actor
      } else if (action === 'ag') {
        const tile = int(tick[1])
        const explicit = tick.slice(3, 7).map((value) => int(value)).filter((value) => value > 0)
        const removed = explicit.length === 4 ? explicit : Array(4).fill(tile)
        for (const item of removed) {
          if (state.drawn != null && normalizedTile(state.drawn) === normalizedTile(item)) state.drawn = null
          else removeExactOrNormalized(state.hand, item)
        }
        state.melds.push({
          tile: salasasaTileToMmcr(normalizedTile(tile)),
          type: 'kong',
          concealed: true,
          chow_mode: 0,
          meld_from_rel: 0,
        })
        currentPlayer = actor
      } else if (action === 'jg') {
        const tile = int(tick[1])
        if (state.drawn != null && normalizedTile(state.drawn) === normalizedTile(tile)) state.drawn = null
        else removeExactOrNormalized(state.hand, tile)
        const meld = state.melds.find((item) =>
          item.type === 'triplet' && item.tile === salasasaTileToMmcr(normalizedTile(tile)))
        if (meld) {
          meld.type = 'kong'
          meld.meld_from_rel += 4
        }
        currentPlayer = actor
      }
    }

    const viewerSeat = seatMap[Math.max(0, Math.min(3, viewerOriginal))] ?? 0
    // 开局补花属于发牌阶段，不会改变庄家的首次行动权。若当前节点仍未
    // 进入正常行牌阶段，下一位行动者必须保持为 start_player_index。
    const snapshotCurrentPlayer = mainPhaseStarted ? currentPlayer : startPlayer
    const seats: SeatSnapshot[] = states.map((state, seat) => {
      const player = this.playerForSeat(round, seat)
      return {
        seat_index: seat,
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
          remaining_tile_count: remaining,
          current_player: snapshotCurrentPlayer,
          last_actor: lastActor,
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

  remainingWallAt(roundIndex: number, requestedNode: number): number[] {
    const safeRoundIndex = Math.max(0, Math.min(this.rounds.length - 1, roundIndex))
    const round = this.rounds[safeRoundIndex]
    const ticks = round.action_ticks || []
    const node = Math.max(0, Math.min(ticks.length, requestedNode))
    const wall = [...(round.tiles_list || [])]
    let useSecondFromBack = true
    for (const tick of ticks.slice(0, node)) {
      const action = String(tick?.[0] ?? '')
      if (action === 'd') {
        if (wall.length) wall.shift()
      } else if (action === 'gd' || action === 'bd') {
        if (wall.length) {
          const index = useSecondFromBack && wall.length > 1 ? wall.length - 2 : wall.length - 1
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
    const ticks = round.action_ticks || []
    const node = Math.max(0, Math.min(ticks.length, requestedNode))
    const original = round.tiles_list || []
    const remainingIndices = original.map((_, index) => index)
    const consumed = new Set<number>()
    let useSecondFromBack = true
    for (const tick of ticks.slice(0, node)) {
      const action = String(tick?.[0] ?? '')
      if (action === 'd' && remainingIndices.length) {
        consumed.add(remainingIndices.shift()!)
      } else if ((action === 'gd' || action === 'bd') && remainingIndices.length) {
        const index = useSecondFromBack && remainingIndices.length > 1
          ? remainingIndices.length - 2
          : remainingIndices.length - 1
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
