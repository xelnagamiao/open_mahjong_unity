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
  SalasasaResponse,
} from './types'

export function salasasaTileToMmcr(tile: number | null | undefined): number {
  if (!tile || tile < 0) return 0
  const normalized = tile >= 100 ? tile % 100 : tile
  const suit = Math.floor(normalized / 10)
  const rank = normalized % 10
  if (suit === 1 && rank >= 1 && rank <= 9) return 0x40 | rank
  if (suit === 2 && rank >= 1 && rank <= 9) return 0x60 | rank
  if (suit === 3 && rank >= 1 && rank <= 9) return 0xc0 | rank
  if (suit === 4 && rank >= 1 && rank <= 7) return 0xa0 | rank
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
  const hand = (player.hand_tiles ?? []).filter((tile) => tile < 50)
  const visibleTileCount = player.hand_tiles ? hand.length : player.hand_tiles_count
  const hasDrawnTile = visibleTileCount % 3 === 2
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
    discard_pile: (player.discard_tiles ?? []).map(salasasaTileToMmcr),
    melds,
    hand_tiles: player.hand_tiles ? body.map(salasasaTileToMmcr) : undefined,
    drawn_tile: drawnRaw ? salasasaTileToMmcr(drawnRaw) : null,
  }
}

function findSelfSeat(game: SalasasaGameInfo, userId: number): number {
  return game.players_info.find((player) => player.user_id === userId)?.player_index ?? 0
}

function viewerActions(
  actions: string[],
  targetTile?: number,
  concealedKongTiles: number[] = [],
  addedKongTiles: number[] = [],
): ViewerAction[] {
  const mapped: ViewerAction[] = []
  for (const action of actions) {
    switch (action) {
      case 'cut': mapped.push({ kind: 'discard_tile' }); break
      case 'pass': mapped.push({ kind: 'pass' }); break
      case 'buhua': mapped.push({ kind: 'flower', tile: salasasaTileToMmcr(targetTile) }); break
      case 'angang':
        for (const tile of concealedKongTiles) mapped.push({ kind: 'concealed_kong', tile: salasasaTileToMmcr(tile) })
        break
      case 'jiagang':
        for (const tile of addedKongTiles) mapped.push({ kind: 'added_kong', tile: salasasaTileToMmcr(tile) })
        break
      case 'gang': mapped.push({ kind: 'melded_kong', tile: salasasaTileToMmcr(targetTile) }); break
      case 'peng': mapped.push({ kind: 'pung', tile: salasasaTileToMmcr(targetTile) }); break
      case 'chi_left': mapped.push({ kind: 'chow', tile: salasasaTileToMmcr((targetTile ?? 0) - 1), ui64_value: 3 }); break
      case 'chi_mid': mapped.push({ kind: 'chow', tile: salasasaTileToMmcr(targetTile), ui64_value: 2 }); break
      case 'chi_right': mapped.push({ kind: 'chow', tile: salasasaTileToMmcr((targetTile ?? 0) + 1), ui64_value: 1 }); break
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
    ended?: SalasasaGameEndInfo
  } | null {
    switch (message.type) {
      case 'gamestate/guobiao/game_start':
        return message.game_info ? { snapshot: this.fromGameInfo(message.game_info) } : null
      case 'gamestate/guobiao/broadcast_hand_action':
        return message.ask_hand_action_info ? { event: this.fromHandPrompt(message.ask_hand_action_info) } : null
      case 'gamestate/guobiao/ask_other_action':
        return message.ask_other_action_info ? { event: this.fromOtherPrompt(message.ask_other_action_info) } : null
      case 'gamestate/guobiao/do_action':
        return message.do_action_info ? { events: this.fromActions(message.do_action_info) } : null
      case 'gamestate/guobiao/show_result':
        return message.show_result_info ? {
          event: this.fromResult(message.show_result_info),
          result: message.show_result_info,
        } : null
      case 'gamestate/guobiao/game_end':
        return message.game_end_info ? { event: this.fromEnd(message.game_end_info), ended: message.game_end_info } : null
      default:
        return null
    }
  }

  private fromGameInfo(game: SalasasaGameInfo): ActiveSessionSnapshot {
    if (game.room_rule !== 'guobiao') throw new Error('2D 客户端仅支持国标对局')
    this.gameInfoValue = game
    const selfSeat = findSelfSeat(game, this.userId)
    const selfPlayer = game.players_info.find((player) => player.user_id === this.userId)
    this.selfSeat = selfSeat
    this.selfHandRaw = [...(selfPlayer?.hand_tiles ?? [])]
    this.selfMeldTargets = [...(selfPlayer?.combination_tiles ?? [])]
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
      seats: game.players_info.map(playerToSeat),
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

  private fromHandPrompt(info: SalasasaAskHandInfo): GameEventPayload {
    const snapshot = this.ensureSnapshot()
    snapshot.state.remaining_tile_count = info.remain_tiles
    const viewer: ViewerSnapshot = {
      seat_index: snapshot.viewer.seat_index,
      pending: info.action_list.length ? 'decision' : 'none',
      decision_timer_ms: Math.max(0, info.remaining_time * 1000),
      available_actions: viewerActions(
        info.action_list,
        undefined,
        this.concealedKongCandidates(),
        this.addedKongCandidates(),
      ),
    }
    return this.event('control', 'hand_prompt', info.player_index, info.action_tick, viewer)
  }

  private fromOtherPrompt(info: SalasasaAskOtherInfo): GameEventPayload {
    const snapshot = this.ensureSnapshot()
    const viewer: ViewerSnapshot = {
      seat_index: snapshot.viewer.seat_index,
      pending: info.action_list.length ? 'decision' : 'none',
      decision_timer_ms: Math.max(0, info.remaining_time * 1000),
      available_actions: viewerActions(info.action_list, info.cut_tile),
    }
    return this.event('control', 'claim_prompt', this.lastDiscarder, info.action_tick, viewer, {
      tile: salasasaTileToMmcr(info.cut_tile),
    })
  }

  private fromActions(info: SalasasaDoActionInfo): GameEventPayload[] {
    const actions = info.action_list.length ? info.action_list : ['']
    const events = actions
      .filter((action) => !(action === 'buhua' && actions.some((item) => item === 'deal_buhua_tile')))
      .map((action) => this.fromAction(info, action))
    if (!info.is_claim) this.updateSelfHand(info)
    return events
  }

  private fromAction(info: SalasasaDoActionInfo, action: string): GameEventPayload {
    const viewer: ViewerSnapshot = {
      seat_index: this.ensureSnapshot().viewer.seat_index,
      pending: 'none',
      decision_timer_ms: null,
      available_actions: [],
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
        case 'cut': this.removeSelfTile(info.cut_tile ?? 0); break
        case 'deal_tile':
        case 'deal_gang_tile':
        case 'deal_buhua_tile':
          if (info.deal_tile) this.selfHandRaw.push(info.deal_tile)
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
