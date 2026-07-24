import type {
  ActiveSessionSnapshot,
  CompactSeatStatus,
  GameEventPayload,
  MeldSnapshot,
  SeatSnapshot,
  ViewerAction,
  ViewerSnapshot,
} from '../lib/types'
import type {
  SalasasaAskHandInfo,
  SalasasaAskOtherInfo,
  SalasasaDoActionInfo,
  SalasasaGameEndInfo,
  SalasasaGameInfo,
  SalasasaPlayerInfo,
  SalasasaResultInfo,
  SalasasaReadyStatusInfo,
  SalasasaResponse,
} from './types'
import type { WaitInfoData } from '../game/scene/WaitDisplay'
import { buildLocalWaitData, type LocalWaitInfoData } from '../calc/guobiao'

export function salasasaTileToMmcr(tile: number | null | undefined): number {
  if (!tile || tile < 0) return 0
  const normalized = tile >= 100 ? tile % 100 : tile
  const suit = Math.floor(normalized / 10)
  const rank = normalized % 10
  if (suit === 1 && rank >= 1 && rank <= 9) return 0x40 | rank
  if (suit === 2 && rank >= 1 && rank <= 9) return 0x60 | rank
  if (suit === 3 && rank >= 1 && rank <= 9) return 0xc0 | rank
  if (suit === 4 && rank >= 1 && rank <= 7) return 0xa0 | rank
  if (suit === 5 && rank >= 1 && rank <= 8) return 0xe0 | rank
  return 0
}

export function mmcrTileToSalasasa(tile: number | null | undefined): number {
  if (!tile) return 0
  const suit = tile & 0xe0
  const rank = tile & 0x0f
  if (suit === 0x40) return 10 + rank
  if (suit === 0x60) return 20 + rank
  if (suit === 0xc0) return 30 + rank
  if (suit === 0xa0) return 40 + rank
  if (suit === 0xe0) return 50 + rank
  return 0
}

function parseMeld(target: string, mask: number[] | undefined, ownerSeat: number): MeldSnapshot | null {
  if (!target) return null
  const prefix = target[0]
  const rawTile = Number(target.slice(1))
  const horizontalPairIndex = mask?.findIndex((value, index) => index % 2 === 0 && value === 1) ?? -1
  const horizontalTilePosition = horizontalPairIndex >= 0 ? Math.floor(horizontalPairIndex / 2) : 0
  const meldFromRel = ([1, 2, 3][horizontalTilePosition] ?? 1) as number
  void ownerSeat
  if (prefix.toLowerCase() === 's') {
    return {
      tile: salasasaTileToMmcr(rawTile),
      type: 'sequence',
      chow_mode: horizontalTilePosition + 1,
      meld_from_rel: 1,
    }
  }
  if (prefix === 'k') {
    return {
      tile: salasasaTileToMmcr(rawTile),
      type: 'triplet',
      chow_mode: 0,
      meld_from_rel: meldFromRel,
    }
  }
  if (prefix === 'g' || prefix === 'G') {
    return {
      tile: salasasaTileToMmcr(rawTile),
      type: 'kong',
      concealed: prefix === 'G',
      chow_mode: 0,
      meld_from_rel: prefix === 'G' ? 0 : meldFromRel,
    }
  }
  return null
}

function playerToSeat(player: SalasasaPlayerInfo): SeatSnapshot {
  const targets = player.combination_tiles ?? []
  const masks = player.combination_mask ?? []
  const melds = targets
    .map((target, index) => parseMeld(target, masks[index], player.player_index))
    .filter((meld): meld is MeldSnapshot => meld !== null)
  const hand = player.hand_tiles ?? []
  const visibleTileCount = player.hand_tiles ? hand.length : player.hand_tiles_count
  const hasDrawnTile = player.has_draw_slot ?? (visibleTileCount % 3 === 2)
  const drawnRaw = hasDrawnTile ? hand[hand.length - 1] : null
  const body = hasDrawnTile ? hand.slice(0, -1) : hand
  return {
    seat_index: player.player_index,
    score: player.score,
    afk: player.tag_list?.includes('offline') ?? false,
    disconnected: player.tag_list?.includes('offline') ?? false,
    hand_tile_count: hasDrawnTile ? Math.max(0, visibleTileCount - 1) : visibleTileCount,
    has_drawn_tile: hasDrawnTile,
    player_id: player.user_id,
    username: player.username,
    voice_id: player.voice_used ?? 1,
    discard_pile: (player.discard_tiles ?? []).map(salasasaTileToMmcr),
    melds,
    flower_tiles: (player.huapai_list ?? []).map(salasasaTileToMmcr),
    hand_tiles: player.hand_tiles ? body.map(salasasaTileToMmcr) : undefined,
    drawn_tile: drawnRaw ? salasasaTileToMmcr(drawnRaw) : null,
  }
}

function waitDataToMmcr(data: LocalWaitInfoData): WaitInfoData {
  if (!data) return null
  if (data.type === 'waits') {
    return {
      type: 'waits',
      details: data.details.map((detail) => ({
        ...detail,
        tile: salasasaTileToMmcr(detail.tile),
      })),
    }
  }
  return {
    type: 'waits_all',
    details: data.details.map((detail) => ({
      discard_tile: salasasaTileToMmcr(detail.discard_tile),
      adds: detail.adds.map((add) => ({
        ...add,
        tile: salasasaTileToMmcr(add.tile),
      })),
    })),
  }
}

function emptySeatLists(): number[][] {
  return [[], [], [], []]
}

function emptySeatCombos(): string[][] {
  return [[], [], [], []]
}

function findSelfSeat(game: SalasasaGameInfo, userId: number): number {
  return game.players_info.find((player) => Number(player.user_id) === Number(userId))?.player_index ?? 0
}

function normalizePlayersInfo(players: SalasasaPlayerInfo[], selfUserId: number): SalasasaPlayerInfo[] {
  const bySeat = new Map<number, SalasasaPlayerInfo>()
  for (const player of players) {
    const seat = Number(player.player_index)
    if (!Number.isInteger(seat) || seat < 0 || seat > 3) continue
    const current = bySeat.get(seat)
    const currentIsSelf = Number(current?.user_id) === Number(selfUserId)
    const playerIsSelf = Number(player.user_id) === Number(selfUserId)
    if (!current || playerIsSelf || !currentIsSelf) bySeat.set(seat, player)
  }
  return [...bySeat.values()].sort((left, right) => left.player_index - right.player_index)
}

function viewerActions(
  actions: string[],
  targetTile?: number,
  concealedKongTiles: number[] = [],
  addedKongTiles: number[] = [],
  flowerTiles: number[] = [],
): ViewerAction[] {
  const mapped: ViewerAction[] = []
  for (const action of actions) {
    switch (action) {
      case 'cut': mapped.push({ kind: 'discard_tile' }); break
      case 'pass': mapped.push({ kind: 'pass' }); break
      case 'buhua': {
        const flower = flowerTiles.at(-1) ?? targetTile
        mapped.push({ kind: 'flower', tile: salasasaTileToMmcr(flower) })
        break
      }
      case 'angang':
        for (const tile of concealedKongTiles) mapped.push({ kind: 'concealed_kong', tile: salasasaTileToMmcr(tile) })
        break
      case 'jiagang':
        for (const tile of addedKongTiles) mapped.push({ kind: 'added_kong', tile: salasasaTileToMmcr(tile) })
        break
      case 'gang': mapped.push({ kind: 'melded_kong', tile: salasasaTileToMmcr(targetTile) }); break
      case 'peng': mapped.push({ kind: 'pung', tile: salasasaTileToMmcr(targetTile) }); break
      // MeldButton 以被吃的切牌为基准，用 ui64_value(1/2/3) 推算手牌两张：
      // 1=<1>23(chi_right)  2=1<2>3(chi_mid)  3=12<3>(chi_left)
      // 不可传入顺子中间牌，否则预览会整体偏移（如 2s 吃显示成 45s）并可能变成非法牌 ID（白牌）。
      case 'chi_left': mapped.push({ kind: 'chow', tile: salasasaTileToMmcr(targetTile), ui64_value: 3 }); break
      case 'chi_mid': mapped.push({ kind: 'chow', tile: salasasaTileToMmcr(targetTile), ui64_value: 2 }); break
      case 'chi_right': mapped.push({ kind: 'chow', tile: salasasaTileToMmcr(targetTile), ui64_value: 1 }); break
      case 'hu_self': mapped.push({ kind: 'self_drawn_win' }); break
      case 'hu_first':
      case 'hu_second':
      case 'hu_third': mapped.push({ kind: 'discard_win', tile: salasasaTileToMmcr(targetTile) }); break
      default: break
    }
  }
  return mapped
}

function seatStatusFromSnapshot(snapshot: ActiveSessionSnapshot): CompactSeatStatus[] {
  return snapshot.seats.map((seat) => ({
    seat_index: seat.seat_index,
    score: seat.score,
    afk: seat.afk,
    disconnected: seat.disconnected,
    hand_tile_count: seat.hand_tile_count,
    has_drawn_tile: seat.has_drawn_tile,
    username: seat.username,
  }))
}

export class SalasasaGameAdapter {
  private snapshotValue: ActiveSessionSnapshot | null = null
  private gameInfoValue: SalasasaGameInfo | null = null
  private lastDiscarder = 0
  private lastDiscardTile = 0
  private selfSeat = 0
  private selfHandRaw: number[] = []
  private selfMeldTargets: string[] = []
  private selfHuapai: number[] = []
  /** Salasasa-format river tiles per seat (for tip remaining / 绝张). */
  private seatDiscards: number[][] = emptySeatLists()
  /** Salasasa-format meld keys per seat. */
  private seatCombinations: string[][] = emptySeatCombos()

  private readonly userId: number

  constructor(userId: number) {
    this.userId = userId
  }

  get snapshot(): ActiveSessionSnapshot | null { return this.snapshotValue }
  get gameInfo(): SalasasaGameInfo | null { return this.gameInfoValue }
  get gamestateId(): string | null { return this.gameInfoValue?.gamestate_id ?? null }

  accept(message: SalasasaResponse): {
    snapshot?: ActiveSessionSnapshot
    event?: GameEventPayload
    events?: GameEventPayload[]
    result?: SalasasaResultInfo
    ready?: SalasasaReadyStatusInfo
    ended?: SalasasaGameEndInfo
  } | null {
    switch (message.type) {
      case 'gamestate/guobiao/game_start':
        return message.game_info ? { snapshot: this.fromGameInfo(message.game_info) } : null
      case 'gamestate/guobiao/broadcast_hand_action':
        return message.ask_hand_action_info ? { event: this.fromHandPrompt(message.ask_hand_action_info) } : null
      case 'gamestate/guobiao/ask_other_action': {
        if (!message.ask_other_action_info) return null
        const event = this.fromOtherPrompt(message.ask_other_action_info)
        return event ? { event } : null
      }
      case 'gamestate/guobiao/do_action':
        return message.do_action_info ? { events: this.fromActions(message.do_action_info) } : null
      case 'gamestate/guobiao/show_result':
        return message.show_result_info ? {
          event: this.fromResult(message.show_result_info),
          result: message.show_result_info,
        } : null
      case 'gamestate/guobiao/game_end':
        return message.game_end_info ? { event: this.fromEnd(message.game_end_info), ended: message.game_end_info } : null
      case 'gamestate/guobiao/ready_status':
        return message.ready_status_info ? { ready: message.ready_status_info } : null
      default:
        return null
    }
  }

  private fromGameInfo(game: SalasasaGameInfo): ActiveSessionSnapshot {
    if (game.room_rule !== 'guobiao') throw new Error('2D 客户端仅支持国标对局')
    const playersInfo = normalizePlayersInfo(game.players_info, this.userId)
    const normalizedGame = { ...game, players_info: playersInfo }
    this.gameInfoValue = normalizedGame
    const selfSeat = findSelfSeat(normalizedGame, this.userId)
    const selfPlayer = playersInfo.find((player) => Number(player.user_id) === Number(this.userId))
    this.selfSeat = selfSeat
    this.selfHandRaw = [...(selfPlayer?.hand_tiles ?? [])]
    this.selfMeldTargets = [...(selfPlayer?.combination_tiles ?? [])]
    this.selfHuapai = [...(selfPlayer?.huapai_list ?? [])]
    this.seatDiscards = emptySeatLists()
    this.seatCombinations = emptySeatCombos()
    for (const player of playersInfo) {
      const seat = player.player_index
      if (seat < 0 || seat > 3) continue
      this.seatDiscards[seat] = [...(player.discard_tiles ?? [])]
      this.seatCombinations[seat] = [...(player.combination_tiles ?? [])]
    }
    const snapshot: ActiveSessionSnapshot = {
      phase: 'active',
      session_id: game.room_id,
      state: {
        round_counter: game.current_round,
        stage_counter: Math.max(1, game.action_tick),
        remaining_tile_count: game.tile_count,
        current_player: game.current_player_index,
        ended: false,
      },
      seats: playersInfo.map(playerToSeat),
      viewer: {
        seat_index: selfSeat,
        pending: 'none',
        decision_timer_ms: null,
        available_actions: [],
      },
    }
    this.snapshotValue = snapshot
    return snapshot
  }

  private ensureSnapshot(): ActiveSessionSnapshot {
    if (!this.snapshotValue) throw new Error('尚未收到对局初始状态')
    return this.snapshotValue
  }

  private event(
    category: string,
    kind: string,
    actorSeat: number,
    stageCounter: number,
    viewer: ViewerSnapshot,
    extra: Record<string, unknown> = {},
  ): GameEventPayload {
    const snapshot = this.ensureSnapshot()
    snapshot.state.stage_counter = Math.max(1, stageCounter)
    snapshot.state.last_actor = actorSeat
    snapshot.state.last_event_kind = kind
    snapshot.state.current_player = actorSeat
    snapshot.viewer = viewer
    return {
      category,
      event: {
        kind,
        actor_seat: actorSeat,
        stage_counter: snapshot.state.stage_counter,
        timestamp_ms: Date.now(),
        ...extra,
      },
      state: { ...snapshot.state },
      viewer,
      seat_status: seatStatusFromSnapshot(snapshot),
    }
  }

  private computeLocalWaitData(includeDiscards: boolean): WaitInfoData {
    if (!this.gameInfoValue?.tips) return null
    const seatCombinations = this.seatCombinations.map((list, seat) => (
      seat === this.selfSeat ? [...this.selfMeldTargets] : [...list]
    ))
    const local = buildLocalWaitData(
      {
        tips: true,
        hand: [...this.selfHandRaw],
        combinations: [...this.selfMeldTargets],
        flowerCount: this.selfHuapai.length,
        playerIndex: this.selfSeat,
        currentRound: Number(this.gameInfoValue.current_round ?? 1),
        hepaiLimit: Number(this.gameInfoValue.hepai_limit ?? 8),
        subRule: this.gameInfoValue.sub_rule ?? 'guobiao/standard',
        seatDiscards: this.seatDiscards.map((list) => [...list]),
        seatCombinations,
      },
      { includeDiscards },
    )
    return waitDataToMmcr(local)
  }

  private fromHandPrompt(info: SalasasaAskHandInfo): GameEventPayload {
    const snapshot = this.ensureSnapshot()
    snapshot.state.remaining_tile_count = info.remain_tiles
    // ask_hand 会同步给四家，但只有当前座位能得到操作按钮。
    const ownActions = info.player_index === this.selfSeat ? info.action_list : []
    const viewer: ViewerSnapshot = {
      seat_index: snapshot.viewer.seat_index,
      pending: ownActions.length ? 'decision' : 'none',
      decision_timer_ms: ownActions.length
        ? this.decisionTimerMs(info.remaining_time)
        : null,
      available_actions: viewerActions(
        ownActions,
        undefined,
        this.concealedKongCandidates(),
        this.addedKongCandidates(),
        this.flowerCandidates(),
      ),
      wait_data: ownActions.length
        ? this.computeLocalWaitData(ownActions.includes('cut'))
        : (snapshot.viewer.wait_data ?? null),
    }
    const playIndex = info.opening_buhua_complete
      ? (info.dealer_index ?? this.gameInfoValue?.dealer_index ?? info.player_index)
      : info.player_index
    return this.event('control', 'hand_prompt', playIndex, info.action_tick, viewer, {
      opening_buhua_complete: Boolean(info.opening_buhua_complete),
    })
  }

  private fromOtherPrompt(info: SalasasaAskOtherInfo): GameEventPayload | null {
    // 新服务端显式携带目标座位。忽略误投或旧连接残留的他家询问，
    // 避免把别人的吃碰杠和按钮显示在本家牌桌上。
    if (typeof info.player_index === 'number' && info.player_index !== this.selfSeat) return null
    const snapshot = this.ensureSnapshot()
    const viewer: ViewerSnapshot = {
      seat_index: snapshot.viewer.seat_index,
      pending: info.action_list.length ? 'decision' : 'none',
      decision_timer_ms: this.decisionTimerMs(info.remaining_time, Boolean(info.is_tactical_recheck)),
      available_actions: viewerActions(info.action_list, info.cut_tile),
      wait_data: snapshot.viewer.wait_data ?? null,
    }
    return this.event('control', 'claim_prompt', this.lastDiscarder, info.action_tick, viewer, {
      tile: salasasaTileToMmcr(info.cut_tile),
    })
  }

  private decisionTimerMs(remainingTime: number, tacticalRecheck = false): number {
    // Server timeout = per-action grace (step_time) + remaining round bank.
    // Tactical rechecks already send their complete grace window directly.
    const stepTime = tacticalRecheck ? 0 : Number(this.gameInfoValue?.step_time ?? 0)
    return Math.max(0, (Number(remainingTime) + stepTime) * 1000)
  }

  private fromActions(info: SalasasaDoActionInfo): GameEventPayload[] {
    const previousWaitData = this.ensureSnapshot().viewer.wait_data ?? null
    const actions = info.action_list.length ? info.action_list : ['']
    // 开局补花会把“移除花牌”和“补进岭上牌”合并在同一帧中。
    // 两个动作都必须保留，否则画面只会增加替代牌，原花牌仍卡在手牌区。
    const events = actions.flatMap((action) => {
      if (action === 'cut') {
        const cutTiles = this.resolvedCutTiles(info)
        if (Array.isArray(info.cut_tiles) && info.cut_tiles.length > 0) {
          return cutTiles.map((cutTile, index) => this.fromAction({
            ...info,
            cut_tile: cutTile,
            cut_tiles: undefined,
            // Multiple drawn tiles are settled in order; only the last remains
            // in the draw slot when the batch is rendered.
            cut_class: Boolean(info.cut_class) && index === cutTiles.length - 1,
          }, action))
        }
      }
      if (action === 'deal_tile' || action === 'deal_gang_tile' || action === 'deal_buhua_tile') {
        const dealTiles = this.resolvedDealTiles(info)
        if (Array.isArray(info.deal_tiles) && info.deal_tiles.length > 0) {
          return dealTiles.map((dealTile) => this.fromAction({
            ...info,
            deal_tile: dealTile,
            deal_tiles: undefined,
          }, action))
        }
      }
      return [this.fromAction(info, action)]
    })
    if (actions.includes('cut')) {
      const cutTiles = this.resolvedCutTiles(info)
      this.lastDiscarder = info.action_player
      this.lastDiscardTile = info.cut_tile ?? cutTiles.at(-1) ?? 0
    }
    this.updateTableState(info)
    if (!info.is_claim) this.updateSelfHand(info)

    const nextWaitData = this.resolveWaitDataAfterAction(info, actions, previousWaitData)
    for (const payload of events) {
      payload.viewer.wait_data = nextWaitData
    }
    this.ensureSnapshot().viewer.wait_data = nextWaitData ?? undefined
    return events
  }

  private resolveWaitDataAfterAction(
    info: SalasasaDoActionInfo,
    actions: string[],
    previousWaitData: WaitInfoData,
  ): WaitInfoData {
    if (!this.gameInfoValue?.tips) return null
    if (info.action_player === this.selfSeat && actions.includes('cut')) {
      return this.computeLocalWaitData(false)
    }
    if (info.action_player === this.selfSeat) {
      // Own non-cut actions clear hover waits until next hand_prompt.
      return null
    }
    // Others changed the table: refresh stable waits (fan / remaining / 绝张).
    if (previousWaitData?.type === 'waits') {
      return this.computeLocalWaitData(false)
    }
    return previousWaitData
  }

  private updateTableState(info: SalasasaDoActionInfo): void {
    // 战术鸣牌 is_claim 只是申请预告，真实副露在后续非 claim 帧落地。
    if (info.is_claim) return
    const seat = info.action_player
    if (seat < 0 || seat > 3) return
    for (const action of info.action_list.length ? info.action_list : []) {
      switch (action) {
        case 'cut':
          for (const tile of this.resolvedCutTiles(info)) {
            if (tile) this.seatDiscards[seat].push(tile)
          }
          break
        case 'peng':
        case 'gang':
        case 'chi_left':
        case 'chi_mid':
        case 'chi_right': {
          const from = typeof info.cut_from_player === 'number' ? info.cut_from_player : this.lastDiscarder
          const claimed = info.cut_tile ?? this.lastDiscardTile
          this.removeLastDiscard(from, claimed)
          if (info.combination_target) {
            this.seatCombinations[seat].push(info.combination_target)
          }
          break
        }
        case 'angang':
          if (info.combination_target) this.seatCombinations[seat].push(info.combination_target)
          break
        case 'jiagang': {
          const tile = Number(info.combination_target?.slice(1))
          this.seatCombinations[seat] = this.seatCombinations[seat].map((target) => (
            target === `k${tile}` ? (info.combination_target ?? `g${tile}`) : target
          ))
          break
        }
        case 'buhua':
          if (seat === this.selfSeat && info.buhua_tile) {
            this.selfHuapai.push(info.buhua_tile)
          }
          break
        default:
          break
      }
    }
  }

  private removeLastDiscard(seat: number, tile: number): void {
    if (seat < 0 || seat > 3 || !tile) return
    const pile = this.seatDiscards[seat]
    for (let i = pile.length - 1; i >= 0; i -= 1) {
      if (pile[i] === tile) {
        pile.splice(i, 1)
        return
      }
    }
  }

  private resolvedCutTiles(info: SalasasaDoActionInfo): number[] {
    if (Array.isArray(info.cut_tiles) && info.cut_tiles.length > 0) return [...info.cut_tiles]
    return typeof info.cut_tile === 'number' ? [info.cut_tile] : []
  }

  private resolvedDealTiles(info: SalasasaDoActionInfo): number[] {
    if (Array.isArray(info.deal_tiles) && info.deal_tiles.length > 0) return [...info.deal_tiles]
    return typeof info.deal_tile === 'number' ? [info.deal_tile] : []
  }

  private fromAction(
    info: SalasasaDoActionInfo,
    action: string,
  ): GameEventPayload {
    const viewer: ViewerSnapshot = {
      seat_index: this.ensureSnapshot().viewer.seat_index,
      pending: 'none',
      decision_timer_ms: null,
      available_actions: [],
      wait_data: null,
    }
    let kind = action
    let tile = info.cut_tile ?? info.deal_tile
    let ui64Value: number | undefined
    if (['chi_left', 'chi_mid', 'chi_right', 'peng', 'gang'].includes(action)) {
      if (typeof info.cut_from_player === 'number') this.lastDiscarder = info.cut_from_player
      if (typeof info.cut_tile === 'number') this.lastDiscardTile = info.cut_tile
    }
    switch (action) {
      case 'cut':
        kind = 'discard_tile'
        this.lastDiscarder = info.action_player
        this.lastDiscardTile = info.cut_tile ?? 0
        break
      case 'deal_tile':
      case 'deal_gang_tile':
      case 'deal_buhua_tile': kind = 'draw_tile'; tile = info.deal_tile; break
      case 'chi_left': kind = 'chow'; tile = this.lastDiscardTile - 1; ui64Value = 3; break
      case 'chi_mid': kind = 'chow'; tile = this.lastDiscardTile; ui64Value = 2; break
      case 'chi_right': kind = 'chow'; tile = this.lastDiscardTile + 1; ui64Value = 1; break
      case 'peng': kind = 'pung'; tile = info.cut_tile ?? this.lastDiscardTile; break
      case 'gang': kind = 'melded_kong'; tile = info.cut_tile ?? this.lastDiscardTile; break
      case 'angang': kind = 'concealed_kong'; tile = Number(info.combination_target?.slice(1)); break
      case 'jiagang': kind = 'added_kong'; tile = Number(info.combination_target?.slice(1)); break
      case 'buhua': kind = 'flower'; tile = info.buhua_tile; break
      case 'hu_self': kind = 'self_drawn_win'; break
      case 'hu_first':
      case 'hu_second':
      case 'hu_third': kind = 'discard_win'; tile = this.lastDiscardTile; break
      default: break
    }
    const category = info.is_claim ? 'claim' : 'transition'
    return this.event(category, kind, info.action_player, info.action_tick, viewer, {
      tile: salasasaTileToMmcr(tile),
      use_drawn_tile: info.cut_class ?? info.is_mo_gang ?? info.is_mo_buhua ?? false,
      ui64_value: ui64Value,
      silent: Boolean(info.silent),
    })
  }

  private concealedKongCandidates(): number[] {
    const counts = new Map<number, number>()
    for (const tile of this.selfHandRaw.filter((value) => value > 0 && value < 50)) {
      counts.set(tile, (counts.get(tile) ?? 0) + 1)
    }
    return [...counts.entries()].filter(([, count]) => count >= 4).map(([tile]) => tile)
  }

  private addedKongCandidates(): number[] {
    const inHand = new Set(this.selfHandRaw)
    return this.selfMeldTargets
      .filter((target) => target.startsWith('k'))
      .map((target) => Number(target.slice(1)))
      .filter((tile) => inHand.has(tile))
  }

  private flowerCandidates(): number[] {
    return this.selfHandRaw.filter((tile) => tile >= 51 && tile <= 58)
  }

  private removeSelfTile(tile: number, count = 1): void {
    for (let index = 0; index < count; index += 1) {
      const position = this.selfHandRaw.indexOf(tile)
      if (position < 0) return
      this.selfHandRaw.splice(position, 1)
    }
  }

  private updateSelfHand(info: SalasasaDoActionInfo): void {
    if (info.action_player !== this.selfSeat) return
    for (const action of info.action_list) {
      switch (action) {
        case 'cut':
          for (const tile of this.resolvedCutTiles(info)) this.removeSelfTile(tile)
          break
        case 'deal_tile':
        case 'deal_gang_tile':
        case 'deal_buhua_tile':
          for (const tile of this.resolvedDealTiles(info)) {
            if (tile) this.selfHandRaw.push(tile)
          }
          break
        case 'peng':
          this.removeSelfTile(info.cut_tile ?? this.lastDiscardTile, 2)
          if (info.combination_target) this.selfMeldTargets.push(info.combination_target)
          break
        case 'gang':
          this.removeSelfTile(info.cut_tile ?? this.lastDiscardTile, 3)
          if (info.combination_target) this.selfMeldTargets.push(info.combination_target)
          break
        case 'angang': {
          const tile = Number(info.combination_target?.slice(1))
          this.removeSelfTile(tile, 4)
          if (info.combination_target) this.selfMeldTargets.push(info.combination_target)
          break
        }
        case 'jiagang': {
          const tile = Number(info.combination_target?.slice(1))
          this.removeSelfTile(tile)
          this.selfMeldTargets = this.selfMeldTargets.map((target) => target === `k${tile}` ? `g${tile}` : target)
          break
        }
        case 'buhua': this.removeSelfTile(info.buhua_tile ?? 0); break
        case 'chi_left':
          this.removeSelfTile(this.lastDiscardTile - 2)
          this.removeSelfTile(this.lastDiscardTile - 1)
          if (info.combination_target) this.selfMeldTargets.push(info.combination_target)
          break
        case 'chi_mid':
          this.removeSelfTile(this.lastDiscardTile - 1)
          this.removeSelfTile(this.lastDiscardTile + 1)
          if (info.combination_target) this.selfMeldTargets.push(info.combination_target)
          break
        case 'chi_right':
          this.removeSelfTile(this.lastDiscardTile + 1)
          this.removeSelfTile(this.lastDiscardTile + 2)
          if (info.combination_target) this.selfMeldTargets.push(info.combination_target)
          break
        default: break
      }
    }
  }

  private fromResult(info: SalasasaResultInfo): GameEventPayload {
    const snapshot = this.ensureSnapshot()
    const winner = info.hepai_player_index ?? 0
    const isDraw = info.hepai_player_index === undefined || info.hepai_player_index === null
    const isSelfDrawn = info.hu_class === 'hu_self'
    if (info.player_to_score) {
      for (const seat of snapshot.seats) {
        const score = info.player_to_score[String(seat.seat_index)]
        if (typeof score === 'number') seat.score = score
      }
    }
    const viewer: ViewerSnapshot = {
      seat_index: snapshot.viewer.seat_index,
      pending: 'none',
      decision_timer_ms: null,
      available_actions: [],
    }
    return this.event('transition', isDraw ? 'drawn_game' : isSelfDrawn ? 'self_drawn_win' : 'discard_win', winner, info.action_tick ?? snapshot.state.stage_counter + 1, viewer, {
      tile: salasasaTileToMmcr(isSelfDrawn ? info.hepai_player_hand?.at(-1) : this.lastDiscardTile),
      revealed_hand_tiles: info.hepai_player_hand?.map(salasasaTileToMmcr),
      // Guobiao hu voice plays once on this show_result event (hand reveal).
      // Vue settlement panel must not replay hu; it only reveals fans + optional gong.
      silent: false,
      // Salasasa uses the Vue settlement panel. Keep the Pixi table unobstructed
      // during the hand-reveal pause instead of drawing mmcr's result text over it.
      suppress_result_display: true,
      win: {
        win_fan: info.hu_score ?? 0,
        win_base_point: info.hu_score ?? 0,
        win_fan_codes: [],
        win_fans: info.hu_fan ?? [],
      },
    })
  }

  private fromEnd(info: SalasasaGameEndInfo): GameEventPayload {
    const snapshot = this.ensureSnapshot()
    const scores = [0, 1, 2, 3].map((seat) => info.player_final_data[String(seat)]?.score ?? 0)
    snapshot.state.ended = true
    snapshot.state.final_scores = scores
    const viewer: ViewerSnapshot = {
      seat_index: snapshot.viewer.seat_index,
      pending: 'none',
      decision_timer_ms: null,
      available_actions: [],
    }
    return this.event('transition', 'end', 0, snapshot.state.stage_counter + 1, viewer, { scores })
  }

  encodeSceneInput(payload: Record<string, unknown>): Record<string, unknown> | null {
    const gamestateId = this.gamestateId
    if (!gamestateId) return null
    const kind = String(payload.kind ?? '')
    const stageCounter = Number(payload.stage_counter ?? this.snapshotValue?.state.stage_counter ?? 0)
    if (kind === 'discard_tile') {
      return {
        type: 'gamestate/GB/cut_tile',
        cutClass: Boolean(payload.use_drawn_tile),
        TileId: mmcrTileToSalasasa(Number(payload.tile)),
        cutIndex: null,
        gamestate_id: gamestateId,
        action_tick: stageCounter,
      }
    }
    const actionMap: Record<string, string> = {
      pass: 'pass',
      flower: 'buhua',
      concealed_kong: 'angang',
      added_kong: 'jiagang',
      melded_kong: 'gang',
      pung: 'peng',
      self_drawn_win: 'hu_self',
      discard_win: this.resolveDiscardWinAction(),
      chow: this.resolveChowAction(Number(payload.ui64_value)),
    }
    const action = actionMap[kind]
    if (!action) return null
    return {
      type: 'gamestate/GB/send_action',
      gamestate_id: gamestateId,
      action,
      targetTile: mmcrTileToSalasasa(Number(payload.tile)),
      chiComboIndex: 0,
      action_tick: stageCounter,
    }
  }

  readyMessage(): Record<string, unknown> | null {
    if (!this.gamestateId) return null
    return {
      type: 'gamestate/GB/send_action',
      gamestate_id: this.gamestateId,
      action: 'ready',
      targetTile: 0,
      chiComboIndex: 0,
      action_tick: this.snapshotValue?.state.stage_counter ?? 0,
    }
  }

  private resolveChowAction(mode: number): string {
    if (mode === 3) return 'chi_left'
    if (mode === 2) return 'chi_mid'
    return 'chi_right'
  }

  private resolveDiscardWinAction(): string {
    const selfSeat = this.snapshotValue?.viewer.seat_index ?? 0
    const delta = (selfSeat - this.lastDiscarder + 4) % 4
    if (delta === 1) return 'hu_first'
    if (delta === 2) return 'hu_second'
    return 'hu_third'
  }
}
