/** Minimal types for the scene module — mirrors what the backend sends. */

import type { WaitInfoData } from './WaitDisplay'

export interface MeldSnapshot {
  tile: number
  type: 'sequence' | 'triplet' | 'kong'
  concealed?: boolean
  chow_mode: number
  meld_from_rel: number
  claimed_from_drawn_discard?: boolean
  added_from_drawn_tile?: boolean
  concealed_from_drawn_tile?: boolean
}

export interface SeatSnapshot {
  seat_index: number
  score: number
  afk: boolean
  disconnected?: boolean
  hand_tile_count: number
  has_drawn_tile: boolean
  player_id: number | null
  username: string | null
  discard_pile: number[]
  discard_drawn_flags?: boolean[]
  melds: MeldSnapshot[]
  hand_tiles?: number[]
  drawn_tile?: number | null
}

export interface ViewerAction {
  kind: string
  tile?: number
  use_drawn_tile?: boolean
  ui64_value?: number
}

export interface ViewerSnapshot {
  seat_index: number
  pending?: string
  decision_timer_ms: number | null
  pending_start_timer_remaining_ms?: number | null
  available_actions: ViewerAction[]
  wait_data?: WaitInfoData
}

export interface GameState {
  round_counter: number
  stage_counter: number
  remaining_tile_count: number
  current_player?: number | null
  last_actor?: number | null
  last_event_kind?: string | null
  result_source_actor?: number | null
  ended?: boolean
  final_scores?: number[] | null
}

export interface ActiveSessionSnapshot {
  phase: 'active'
  session_id: number
  state: GameState
  seats: SeatSnapshot[]
  viewer: ViewerSnapshot
  result_event?: {
    kind: string
    stage_counter: number
    actor_seat: number
    timestamp_ms: number
    tile?: number
    win?: {
      win_fan: number
      win_base_point: number
      win_fan_codes: string[]
      win_fans?: string[]
    }
    revealed_hand_tiles?: number[]
    scores?: number[]
  } | null
  reveal_all_hands?: boolean
}
