import type { WaitInfoData } from '../game/scene/WaitDisplay'

/* Types shared between the React pages and the backend WebSocket / HTTP API. */

export interface PlayerProfile {
  player_id: number
  username: string
}

export interface SessionInfo {
  session_id: number
  token: string
  created_at_ms: number
  expires_at_ms: number
}

export interface AuthSession {
  player: PlayerProfile
  session: SessionInfo
}

// ── Pending (waiting room) ──────────────────────────────────────────

export interface PendingSessionSummary {
  session_id: number
  occupied_seat_count: number
  ready_seat_count: number
  primary_timer_ms: number
  secondary_timer_ms: number
  auxiliary_timer_ms: number
  round_count: number
  recorded: boolean
  debug_mode?: boolean
  public_session: boolean
  can_join: boolean
  can_start: boolean
  names: string[]
}

export interface ActiveSessionSummary {
  session_id: number
  primary_timer_ms: number
  secondary_timer_ms: number
  auxiliary_timer_ms: number
  round_count: number
  round_counter: number
  recorded: boolean
  debug_mode?: boolean
  ended?: boolean
  public_session: boolean
  names: string[]
}

export interface PendingSeatSnapshot {
  seat_index: number
  ready: boolean
  player_id: number | null
  username: string | null
}

export interface PendingSnapshot {
  phase: 'pending'
  summary: PendingSessionSummary
  seats: PendingSeatSnapshot[]
}

// ── Active game ─────────────────────────────────────────────────────

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

export interface ViewerAction {
  kind: string
  tile?: number
  use_drawn_tile?: boolean
  ui64_value?: number
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

export interface ViewerSnapshot {
  seat_index: number
  pending: string
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
  result_event?: GameEventSnapshot | null
  reveal_all_hands?: boolean
}

export type SessionSnapshot = PendingSnapshot | ActiveSessionSnapshot

// ── game.event ──────────────────────────────────────────────────────

export interface GameEventWinData {
  win_fan: number
  win_base_point: number
  win_fan_codes: string[]
  win_fans?: string[]
}

export interface GameEventSnapshot {
  kind: string
  stage_counter: number
  actor_seat: number
  timestamp_ms: number
  drawn_tiles?: number[]
  tile?: number
  use_drawn_tile?: boolean
  forced?: boolean
  draw_from_back?: boolean
  ui64_value?: number
  win?: GameEventWinData
  revealed_hand_tiles?: number[]
  scores?: number[]
}

export interface CompactSeatStatus {
  seat_index: number
  score: number
  afk: boolean
  disconnected?: boolean
  hand_tile_count: number
  has_drawn_tile: boolean
  username?: string | null
}

export interface GameEventPayload {
  category: string
  event: GameEventSnapshot
  state: GameState
  viewer: ViewerSnapshot
  seat_status: CompactSeatStatus[]
  reveal_all_hands?: boolean
}

export interface PassAckPayload {
  stage_counter: number
}

export interface SessionSnapshotPayload {
  session: SessionSnapshot
}

// ── Lobby ───────────────────────────────────────────────────────────

export interface LobbyListPayload {
  sessions: PendingSessionSummary[]
  active_sessions?: ActiveSessionSummary[]
  player?: PlayerProfile
}

// ── Replay ──────────────────────────────────────────────────────────

export interface ReplayInfo {
  session_identifier: string
  timestamp_ns: string
  timestamp_ms: number
  round_count: number
  player_names: string[]
}

export interface ReplayListPayload {
  replays: ReplayInfo[]
}

export interface ReplayListQueryPayload {
  page?: number
  page_size?: number
  session_query?: string
  player_query?: string
  exact_session_match?: boolean
  started_after_ms?: number
  started_before_ms?: number
}

export interface ReplayListPagePayload {
  replays: ReplayInfo[]
  total_count: number
  page: number
  page_size: number
  page_count: number
  unique_player_count: number
  latest_timestamp_ms: number | null
  session_query?: string
  player_query?: string
  exact_session_match: boolean
}

export interface ReplayGameConfig {
  primary_timer_ms: number
  secondary_timer_ms: number
  auxiliary_timer_ms: number
  round_count: number
  seat_shuffle_period: number
  recorded: boolean
}

export interface ReplayRecordHeader {
  session_identifier: string
  round_number: number
  game_config: ReplayGameConfig
}

export interface ReplayRoundStartSnapshot {
  seat_shuffle_seed?: string | number | null
  wall_seeds: Array<string | number>
  player_ids: number[]
}

export interface ReplayInitialSeat {
  player_id: number | null
  player_name: string | null
  afk_counter: number
  score: number
  disconnected: boolean
}

export interface ReplayRecordEvent {
  kind: string
  stage_counter: number
  actor_seat: number
  timestamp_ms: number
  round_turn?: number
  round_total_turn?: number
  drawn_tiles?: number[]
  tile?: number
  use_drawn_tile?: boolean
  forced?: boolean
  draw_from_back?: boolean
  ui64_value?: number
  win_type_bits?: number
  result_source_actor?: number
  win_data?: GameEventWinData
  revealed_hand_tiles?: number[]
  final_scores?: number[]
}

export interface ReplayRoundResult {
  completed: boolean
  terminal_kind: string
  drawn_game: boolean
  turn: number
  total_turn: number
  time_ms: number
  meld_count: number[]
  winner_seat?: number | null
  from_seat?: number | null
  win_type_bits: number
  win_tile?: number | null
  fan: number
  fan_results: string[]
  fan_names: string[]
}

export interface ReplayRoundRecord {
  version: number
  header: ReplayRecordHeader
  round_start_snapshot: ReplayRoundStartSnapshot
  initial_seats: ReplayInitialSeat[]
  round_result?: ReplayRoundResult
  transition_queue: ReplayRecordEvent[]
  event_queue: ReplayRecordEvent[]
}

export interface ReplaySessionPayload {
  session_identifier: string
  round_count: number
  player_names: string[]
  round_records: ReplayRoundRecord[]
}

// ── WebSocket ───────────────────────────────────────────────────────

export interface WsEnvelope<TPayload = unknown> {
  version?: number
  type: string
  payload: TPayload
  requestId?: string | null
}

export function buildGameInput(
  kind: string,
  stage_counter: number,
  opts?: { tile?: number; use_drawn_tile?: boolean; ui64_value?: number },
): Record<string, unknown> {
  return { kind, stage_counter, ...opts }
}