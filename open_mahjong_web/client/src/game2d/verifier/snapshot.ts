import type { ActiveSessionSnapshot, MeldSnapshot, SeatSnapshot } from '@/game2d/game/scene/types'
import { salasasaTileToMmcr } from '@/game2d/salasasa/gameAdapter'

export interface VerifierPlayerDump {
  index: number
  user_id: number
  username: string
  hand_tiles?: number[]
  has_draw_slot?: boolean
  discard_tiles?: number[]
  combination_tiles?: string[]
  combination_mask?: number[][]
  huapai_list?: number[]
  score?: number
  tag_list?: string[]
}

export interface VerifierDump {
  current_player_index?: number
  current_round?: number
  server_action_tick?: number
  tiles_remaining?: number
  tiles_list?: number[]
  players?: VerifierPlayerDump[]
  game_status?: string
}

function parseMeld(target: string, mask: number[] | undefined): MeldSnapshot | null {
  if (!target) return null
  const prefix = target[0]
  const rawTile = Number(target.slice(1))
  const horizontalPairIndex = mask?.findIndex((value, index) => index % 2 === 0 && value === 1) ?? -1
  const horizontalTilePosition = horizontalPairIndex >= 0 ? Math.floor(horizontalPairIndex / 2) : 0
  const meldFromRel = ([1, 2, 3][horizontalTilePosition] ?? 1) as number
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

function playerToSeat(player: VerifierPlayerDump): SeatSnapshot {
  const targets = player.combination_tiles ?? []
  const masks = player.combination_mask ?? []
  const melds = targets
    .map((target, index) => parseMeld(target, masks[index]))
    .filter((meld): meld is MeldSnapshot => meld !== null)
  const hand = player.hand_tiles ?? []
  const hasDrawnTile = player.has_draw_slot ?? (hand.length % 3 === 2)
  const drawnRaw = hasDrawnTile ? hand[hand.length - 1] : null
  const body = hasDrawnTile ? hand.slice(0, -1) : hand
  return {
    seat_index: player.index,
    score: Number(player.score) || 0,
    afk: player.tag_list?.includes('offline') ?? false,
    disconnected: player.tag_list?.includes('offline') ?? false,
    hand_tile_count: hasDrawnTile ? Math.max(0, body.length) : hand.length,
    has_drawn_tile: hasDrawnTile,
    player_id: player.user_id,
    username: player.username,
    discard_pile: (player.discard_tiles ?? []).map(salasasaTileToMmcr),
    melds,
    flower_tiles: (player.huapai_list ?? []).map(salasasaTileToMmcr),
    hand_tiles: body.map(salasasaTileToMmcr),
    drawn_tile: drawnRaw ? salasasaTileToMmcr(drawnRaw) : null,
  }
}

export function dumpToSnapshot(dump: VerifierDump | undefined, viewerSeat: number): ActiveSessionSnapshot | null {
  if (!dump?.players?.length) return null
  const seats = [...dump.players]
    .sort((left, right) => left.index - right.index)
    .map(playerToSeat)
  while (seats.length < 4) {
    const index = seats.length
    seats.push({
      seat_index: index,
      score: 0,
      afk: false,
      hand_tile_count: 0,
      has_drawn_tile: false,
      player_id: null,
      username: `P${index}`,
      discard_pile: [],
      melds: [],
      flower_tiles: [],
      hand_tiles: [],
      drawn_tile: null,
    })
  }
  const remaining = Array.isArray(dump.tiles_list)
    ? dump.tiles_list.length
    : Number(dump.tiles_remaining) || 0
  return {
    phase: 'active',
    session_id: 1,
    reveal_all_hands: true,
    state: {
      round_counter: Number(dump.current_round) || 1,
      stage_counter: Math.max(1, Number(dump.server_action_tick) || 1),
      remaining_tile_count: remaining,
      current_player: dump.current_player_index ?? 0,
      ended: dump.game_status === 'END',
    },
    seats,
    viewer: {
      seat_index: ((viewerSeat % 4) + 4) % 4,
      decision_timer_ms: null,
      available_actions: [],
    },
  }
}
