/**
 * 把 Unity 组件模拟器快照译成 MahjongScene 的 ActiveSessionSnapshot。
 * 只读 GsmSim / Game3DSim / ActionButtonSim 等字段，不走 2D recordReplay.ts / SalasasaGameAdapter。
 * 牌码换算与 gameAdapter 相同：Salasasa 11–19/21–29/… ↔ MMCR。
 */
const POSITIONS = ['self', 'right', 'top', 'left']
const CLAIM_ACTIONS = new Set([
  'chi_left', 'chi_mid', 'chi_right', 'peng', 'gang',
  'hu', 'hu_first', 'hu_second', 'hu_third',
])

export function salasasaTileToMmcr(tile) {
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

export function mmcrTileToSalasasa(tile) {
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

function asList(value) {
  if (value == null) return []
  return Array.isArray(value) ? value : [value]
}

function asInt(value, fallback = 0) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? Math.trunc(parsed) : fallback
}

export function emptyUnitySceneSnapshot(selfIndex = 0) {
  return {
    phase: 'active',
    session_id: 0,
    state: {
      round_counter: 1,
      stage_counter: 1,
      remaining_tile_count: 0,
      current_player: selfIndex,
      ended: false,
      final_scores: null,
    },
    seats: [0, 1, 2, 3].map((seat) => ({
      seat_index: seat,
      score: 0,
      afk: false,
      disconnected: false,
      hand_tile_count: 0,
      has_drawn_tile: false,
      player_id: seat + 1,
      username: '',
      rank: '',
      voice_id: 1,
      discard_pile: [],
      melds: [],
      flower_tiles: [],
    })),
    viewer: {
      seat_index: selfIndex,
      pending: 'none',
      decision_timer_ms: null,
      available_actions: [],
    },
    result_event: null,
    reveal_all_hands: false,
  }
}

export function hasUnitySnapshot(components) {
  const c = components || {}
  return !!(c.GsmSim || c.Game3DSim || c.BoardCanvasSim)
}

export function seatOfPosition(gsm, pos) {
  const map = gsm.indexToPosition || {}
  for (const [key, value] of Object.entries(map)) {
    if (value === pos && /^\d+$/.test(key)) return Number(key)
  }
  const self = asInt(gsm.selfIndex, 0)
  const offset = POSITIONS.indexOf(pos)
  return offset < 0 ? self : (self + offset) % 4
}

export function positionOfSeat(gsm, seat) {
  const map = gsm.indexToPosition || {}
  return map[seat] || map[String(seat)] || POSITIONS[(asInt(seat) - asInt(gsm.selfIndex, 0) + 4) % 4]
}

function parseMeld(code, mask) {
  if (!code || typeof code !== 'string') return null
  const prefix = code[0]
  const rawTile = Number(code.slice(1))
  const tile = salasasaTileToMmcr(rawTile)
  const list = asList(mask)
  const horizontalPairIndex = list.findIndex((value, index) => index % 2 === 0 && value === 1)
  const horizontalTilePosition = horizontalPairIndex >= 0 ? Math.floor(horizontalPairIndex / 2) : 0
  const meldFromRel = [1, 2, 3][horizontalTilePosition] ?? 1
  if (prefix === 's' || prefix === 'S') {
    return { tile, type: 'sequence', chow_mode: horizontalTilePosition + 1, meld_from_rel: 1 }
  }
  if (prefix === 'k') {
    return {
      tile,
      type: list.includes(3) ? 'kong' : 'triplet',
      chow_mode: 0,
      meld_from_rel: meldFromRel,
      added_from_drawn_tile: list.includes(3),
    }
  }
  if (prefix === 'g' || prefix === 'G') {
    return {
      tile,
      type: 'kong',
      concealed: prefix === 'G',
      chow_mode: 0,
      meld_from_rel: prefix === 'G' ? 0 : meldFromRel,
      added_from_drawn_tile: list.includes(3),
    }
  }
  return null
}

function splitHand(tiles, dumped) {
  const list = asList(tiles).map((tile) => asInt(tile)).filter((tile) => tile > 0)
  const lastDrawn = dumped && dumped.last_drawn_tile != null ? asInt(dumped.last_drawn_tile) : null
  if (dumped && dumped.has_draw_slot && lastDrawn && list.includes(lastDrawn)) {
    const next = list.slice()
    const index = next.lastIndexOf(lastDrawn)
    next.splice(index, 1)
    return { hand: next, drawn: lastDrawn }
  }
  if (list.length % 3 === 2) {
    return { hand: list.slice(0, -1), drawn: list[list.length - 1] }
  }
  return { hand: list, drawn: null }
}

function dumpPlayer(dump, gsm, pos) {
  const players = (dump && dump.players) || []
  const seat = seatOfPosition(gsm, pos)
  return players.find((player) => asInt(player.index, -1) === seat) || null
}

function clientActions(legal, gsm, selfIndex) {
  const seat = ((legal && legal.seats) || {})[String(selfIndex)] || {}
  if (asList(seat.client_actions).length) return asList(seat.client_actions).map(String)
  return asList(gsm.allowActionList).map(String)
}

function flattenButtonActions(components) {
  const buttons = ((components.ActionButtonSim || {}).buttons) || []
  const out = []
  for (const button of buttons) {
    for (const action of asList(button.actions)) out.push(String(action))
  }
  return out
}

function viewerActionsFromUnity(actions, extras) {
  const mapped = []
  const claim = extras.claimTile
  const angang = extras.angangTiles || []
  const jiagang = extras.jiagangTiles || []
  const flowers = extras.flowerTiles || []
  for (const action of actions) {
    switch (action) {
      case 'cut':
        mapped.push({ kind: 'discard_tile' })
        break
      case 'pass':
        mapped.push({ kind: 'pass' })
        break
      case 'force_pass':
        mapped.push({ kind: 'force_pass' })
        break
      case 'buhua':
        mapped.push({ kind: 'flower', tile: salasasaTileToMmcr(flowers.at(-1) || claim) })
        break
      case 'angang':
        for (const tile of angang) mapped.push({ kind: 'concealed_kong', tile: salasasaTileToMmcr(tile), server_action: action })
        break
      case 'jiagang':
        for (const tile of jiagang) mapped.push({ kind: 'added_kong', tile: salasasaTileToMmcr(tile), server_action: action })
        break
      case 'gang':
        mapped.push({ kind: 'melded_kong', tile: salasasaTileToMmcr(claim), server_action: action })
        break
      case 'peng':
        mapped.push({ kind: 'pung', tile: salasasaTileToMmcr(claim), server_action: action })
        break
      case 'chi_left':
        mapped.push({ kind: 'chow', tile: salasasaTileToMmcr(claim), ui64_value: 3, server_action: action })
        break
      case 'chi_mid':
        mapped.push({ kind: 'chow', tile: salasasaTileToMmcr(claim), ui64_value: 2, server_action: action })
        break
      case 'chi_right':
        mapped.push({ kind: 'chow', tile: salasasaTileToMmcr(claim), ui64_value: 1, server_action: action })
        break
      case 'hu_self':
        mapped.push({ kind: 'self_drawn_win', server_action: action })
        break
      case 'hu_flower':
      case 'initial_hu':
      case 'sea_bottom':
        mapped.push({ kind: 'self_drawn_win', server_action: action })
        break
      case 'hu':
      case 'hu_first':
      case 'hu_second':
      case 'hu_third':
        mapped.push({
          kind: extras.robKong ? 'rob_added_kong_win' : 'discard_win',
          tile: salasasaTileToMmcr(claim),
          server_action: action,
        })
        break
      case 'buzhang':
        mapped.push({ kind: 'flower', tile: salasasaTileToMmcr(flowers.at(-1) || claim), server_action: action })
        break
      case 'jiuzhongjiupai':
        mapped.push({ kind: 'self_drawn_win', server_action: action })
        break
      default:
        break
    }
  }
  return mapped
}

export function waitDataFromTips(tips) {
  const tiles = asList(tips && tips.waiting_tiles).map((tile) => asInt(tile)).filter((tile) => tile > 0)
  if (!tiles.length || !(tips && tips.visible)) return null
  return {
    type: 'waits',
    details: tiles.map((tile) => ({
      tile: salasasaTileToMmcr(tile),
      base_f: 0,
      selfdrawn_f: 0,
      remaining_count: 0,
    })),
  }
}

export function buildUnitySceneSnapshot({
  components = {},
  legal = {},
  dump = {},
  interactive = false,
  showOtherHands = false,
  atLastFrame = false,
} = {}) {
  if (!hasUnitySnapshot(components)) return null
  const gsm = components.GsmSim || {}
  const board = components.BoardCanvasSim || {}
  const canvas = components.GameCanvasSim || {}
  const g3 = components.Game3DSim || {}
  const timer = components.TimerSim || {}
  const endResult = components.EndResultSim || {}
  const tips = components.TipsSim || {}
  const selfIndex = asInt(gsm.selfIndex, 0)
  const infoMap = gsm.player_to_info || {}
  const revealOthers = !!(showOtherHands && atLastFrame)
  const seats = POSITIONS.map((pos) => {
    const seatIndex = seatOfPosition(gsm, pos)
    const info = infoMap[pos] || {}
    const pane = g3[pos] || {}
    const dumped = atLastFrame ? dumpPlayer(dump, gsm, pos) : null
    const selfHand = canvas.self_hand_slots || gsm.selfHandTiles || []
    const openTiles = pos === 'self'
      ? selfHand
      : (revealOthers && dumped && dumped.hand_tiles) || []
    const split = splitHand(openTiles, dumped)
    const reveal = pos === 'self' || (revealOthers && asList(openTiles).length > 0)
    const codes = asList(pane.melds || info.combination_tiles)
    const masks = asList(info.combination_masks || (dumped && dumped.combination_mask))
    const melds = codes.map((code, index) => parseMeld(code, masks[index])).filter(Boolean)
    const river = asList(pane.river || info.discard_tiles).map((tile) => salasasaTileToMmcr(asInt(tile)))
    const flowers = asList(pane.flowers || info.huapai_list).map((tile) => salasasaTileToMmcr(asInt(tile)))
    const rawCount = asInt(pane.hand_count || info.hand_tiles_count, split.hand.length + (split.drawn != null ? 1 : 0))
    const hiddenDrawn = rawCount % 3 === 2
    return {
      seat_index: seatIndex,
      score: asInt(info.score ?? board.scores?.[pos], 0),
      afk: false,
      disconnected: false,
      hand_tile_count: reveal ? split.hand.length : (hiddenDrawn ? Math.max(0, rawCount - 1) : rawCount),
      has_drawn_tile: reveal ? split.drawn != null : hiddenDrawn,
      player_id: asInt(info.user_id, seatIndex + 1),
      username: info.username || canvas.panels?.[pos]?.username || pos,
      rank: info.wind || board.winds?.[pos] || '',
      voice_id: 1,
      discard_pile: river,
      melds,
      flower_tiles: flowers,
      hand_tiles: reveal ? split.hand.map(salasasaTileToMmcr) : undefined,
      drawn_tile: reveal && split.drawn != null ? salasasaTileToMmcr(split.drawn) : null,
    }
  })

  const seatLegal = ((legal && legal.seats) || {})[String(selfIndex)] || {}
  const actions = Array.from(new Set([
    ...clientActions(legal, gsm, selfIndex),
    ...flattenButtonActions(components),
  ]))
  const claimTile = asInt(
    seatLegal.claim_tile ?? gsm.currentAskCutTileId ?? gsm.lastCutCardID,
    0,
  )
  const viewerActs = interactive
    ? viewerActionsFromUnity(actions, {
      claimTile,
      angangTiles: asList((seatLegal.target_tiles || {}).angang),
      jiagangTiles: asList((seatLegal.target_tiles || {}).jiagang),
      flowerTiles: asList((infoMap.self || {}).huapai_list),
      robKong: actions.some((item) => String(item).startsWith('hu'))
        && !actions.includes('cut')
        && !gsm.lastDiscardPlayerPosition
        && claimTile > 0,
    })
    : []
  if (
    interactive
    && viewerActs.some((item) => item.kind !== 'discard_tile')
    && !viewerActs.some((item) => item.kind === 'pass' || item.kind === 'force_pass')
  ) {
    viewerActs.push({ kind: 'pass' })
  }
  const canAct = interactive && viewerActs.length > 0
  const currentPos = gsm.CurrentPlayer || board.huang_tiao
  const currentSeat = POSITIONS.includes(currentPos) ? seatOfPosition(gsm, currentPos) : asInt(currentPos, selfIndex)
  const claiming = actions.some((item) => CLAIM_ACTIONS.has(item)) || !!(g3.claim_glow && g3.claim_glow.on)
  const lastPos = gsm.lastDiscardPlayerPosition
  const lastDiscarder = claiming && lastPos ? seatOfPosition(gsm, lastPos) : null
  const remain = asInt(board.remain_tiles ?? gsm.remainTiles, 0)
  const roundMatch = String(board.round_text || '').match(/(\d+)/)
  const round = roundMatch ? asInt(roundMatch[1], 1) : 1
  const waitData = waitDataFromTips(tips)
  const resultEvent = !interactive && endResult.show_result
    ? {
      kind: String(endResult.hu_class || '') === 'hu_self' ? 'self_drawn_win' : 'discard_win',
      stage_counter: Math.max(1, asInt(gsm.LastAskActionTick, 1)),
      actor_seat: asInt(endResult.hepai_player_index, 0),
      timestamp_ms: 0,
      win: {
        win_fan: asList(endResult.hu_fan).length,
        win_base_point: 0,
        win_fan_codes: [],
        win_fans: asList(endResult.hu_fan).map(String),
      },
    }
    : null

  return {
    phase: 'active',
    session_id: 1,
    state: {
      round_counter: round,
      stage_counter: Math.max(1, asInt(gsm.LastAskActionTick, 1)),
      remaining_tile_count: remain,
      current_player: currentSeat,
      last_actor: lastDiscarder,
      last_discarder: lastDiscarder,
      last_event_kind: lastDiscarder != null ? 'discard_tile' : null,
      ended: !interactive && !!endResult.show_result,
      final_scores: null,
    },
    seats,
    viewer: {
      seat_index: selfIndex,
      pending: canAct ? 'wait' : 'none',
      decision_timer_ms: timer.running ? Math.max(0, asInt(timer.remaining_time, 0) * 1000) : null,
      available_actions: viewerActs,
      wait_data: waitData,
    },
    result_event: resultEvent,
    reveal_all_hands: revealOthers,
  }
}

export function encodeUnityAction(payload, extras = {}) {
  const kind = String((payload && payload.kind) || '')
  const claim = asInt(extras.claimTile, 0)
  if (kind === 'discard_tile') {
    return {
      action_type: 'cut',
      tile_id: mmcrTileToSalasasa(Number(payload.tile)),
      cutClass: Boolean(payload.use_drawn_tile),
    }
  }
  const mapped = {
    pass: 'pass',
    force_pass: 'force_pass',
    flower: 'buhua',
    concealed_kong: 'angang',
    added_kong: 'jiagang',
    melded_kong: 'gang',
    pung: 'peng',
    self_drawn_win: extras.huSelf || 'hu_self',
  }
  let action = payload.server_action || mapped[kind]
  if (kind === 'chow') {
    const mode = asInt(payload.ui64_value, 1)
    action = payload.server_action || (mode === 3 ? 'chi_left' : mode === 2 ? 'chi_mid' : 'chi_right')
  } else if (kind === 'discard_win' || kind === 'rob_added_kong_win') {
    action = payload.server_action || extras.huClaim || 'hu'
  }
  if (!action) return null
  const fromTile = mmcrTileToSalasasa(Number(payload.tile))
  if (action === 'angang' || action === 'jiagang') {
    return { action_type: action, target_tile: fromTile || claim }
  }
  return { action_type: action, target_tile: fromTile || claim || 0 }
}
