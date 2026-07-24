export type ConnectionStatus = 'idle' | 'connecting' | 'online' | 'offline'

export interface SalasasaLoginInfo {
  user_id: number
  username: string
  userkey?: string
  is_tourist?: boolean
}

export interface SalasasaRankData {
  guobiao_rank: string
  guobiao_score: number
  is_sponsor?: boolean
  is_mcrpl_qualified?: boolean
}

export interface SalasasaResponse {
  type: string
  success?: boolean
  message?: string
  message_info?: {
    title?: string
    content?: string
  }
  client_ts?: number
  login_info?: SalasasaLoginInfo
  rank_data?: SalasasaRankData
  queue_status?: Record<string, { waiting: number; playing: number }>
  game_info?: SalasasaGameInfo
  ask_hand_action_info?: SalasasaAskHandInfo
  ask_other_action_info?: SalasasaAskOtherInfo
  do_action_info?: SalasasaDoActionInfo
  show_result_info?: SalasasaResultInfo
  game_end_info?: SalasasaGameEndInfo
  ready_status_info?: SalasasaReadyStatusInfo
  [key: string]: unknown
}

export interface SalasasaPlayerInfo {
  user_id: number
  username: string
  hand_tiles_count: number
  hand_tiles?: number[] | null
  discard_tiles?: number[]
  combination_tiles?: string[]
  combination_mask?: number[][]
  huapai_list?: number[]
  remaining_time?: number
  player_index: number
  original_player_index?: number
  score: number
  guobiao_rank?: string
  guobiao_score?: number
  has_draw_slot?: boolean
  voice_used?: number
  tag_list?: string[]
}

export interface SalasasaGameInfo {
  room_id: number
  gamestate_id: string
  current_player_index: number
  action_tick: number
  max_round: number
  tile_count: number
  current_round: number
  step_time: number
  round_time: number
  room_type: string
  room_rule: string
  sub_rule?: string
  tips?: boolean
  hepai_limit?: number
  dealer_index?: number
  players_info: SalasasaPlayerInfo[]
}

export interface SalasasaAskHandInfo {
  action_list: string[]
  remaining_time: number
  player_index: number
  remain_tiles: number
  forced_cut_tiles?: number[]
  action_tick: number
  dealer_index?: number
  opening_buhua_complete?: boolean
}

export interface SalasasaAskOtherInfo {
  action_list: string[]
  remaining_time: number
  cut_tile: number
  action_tick: number
  player_index?: number
  is_tactical_recheck?: boolean
}

export interface SalasasaDoActionInfo {
  action_list: string[]
  action_player: number
  action_tick: number
  cut_from_player?: number
  cut_tile?: number
  cut_tiles?: number[]
  cut_tile_index?: number
  cut_class?: boolean
  deal_tile?: number
  deal_tiles?: number[]
  buhua_tile?: number
  combination_target?: string
  combination_mask?: number[]
  is_claim?: boolean
  silent?: boolean
  is_mo_gang?: boolean
  is_mo_buhua?: boolean
}

export interface SalasasaResultInfo {
  hepai_player_index?: number
  player_to_score?: Record<string, number>
  hu_score?: number
  hu_fan?: string[]
  hu_class?: string
  hepai_player_hand?: number[]
  hepai_player_huapai?: number[]
  hepai_player_combination_mask?: number[][]
  action_tick?: number
  score_changes?: Record<string, number>
  next_status?: string
}

export interface SalasasaReadyStatusInfo {
  player_to_ready: Record<string, boolean>
}

export interface SalasasaGameEndInfo {
  player_final_data: Record<string, {
    username?: string
    rank?: number
    score?: number
    pt?: number
    rank_before?: string
    rank_after?: string
  }>
}

export type StoredCredentials =
  | { mode: 'token'; token: string }
  | { mode: 'password'; username: string; password: string }

export interface PublicLeaderboardEntry {
  rank_position: number
  user_id: number
  username: string
  guobiao_rank: string
  guobiao_score: number
}

export interface PublicPlayerInfo {
  user_id: number
  username: string
  guobiao_rank?: string
  guobiao_score?: number
  [key: string]: unknown
}
