import type { PublicGameRecord, RecordRound, RecordTick } from './recordReplay'

const STORAGE_KEY = 'salasasa:local-replay-record'
export const LOCAL_REPLAY_ID = 'local-converted-record'

type SalasasaRecord = {
  game_title?: Record<string, unknown>
  game_round?: Record<string, RecordRound>
}

function scoreChange(tick: RecordTick): number[] | null {
  const action = String(tick?.[0] ?? '')
  let value: unknown
  if (['hu_self', 'hu_first', 'hu_second', 'hu_third'].includes(action)) value = tick[4]
  else if (action === 'hu_riichi') value = tick[6]
  else if (action === 'ryuukyoku' || action === 'liuju') value = tick[2]
  if (!Array.isArray(value) || value.length < 4) return null
  return value.slice(0, 4).map((item) => Number(item) || 0)
}

function finalScores(rounds: Record<string, RecordRound>): number[] {
  const scores = [0, 0, 0, 0]
  for (const round of Object.values(rounds)) {
    const seats = Array.isArray(round.seats) && round.seats.length === 4 ? round.seats : [0, 1, 2, 3]
    for (const tick of round.action_ticks || []) {
      const bySeat = scoreChange(tick)
      if (!bySeat) continue
      for (let original = 0; original < 4; original += 1) {
        scores[original] += bySeat[Number(seats[original]) || 0] || 0
      }
    }
  }
  return scores
}

function normalizeRecord(value: SalasasaRecord | PublicGameRecord): PublicGameRecord {
  if ('record' in value && value.record?.game_round) return value as PublicGameRecord

  const title = value.game_title || {}
  const rounds = value.game_round || {}
  if (!Object.keys(rounds).length) throw new Error('转换结果中没有可播放的小局')

  const scores = finalScores(rounds)
  const ranks = scores
    .map((score, index) => ({ score, index }))
    .sort((left, right) => right.score - left.score)
  const rankByIndex = Object.fromEntries(ranks.map((item, index) => [item.index, index + 1]))

  return {
    game_id: LOCAL_REPLAY_ID,
    created_at: new Date().toISOString(),
    rule: String(title.rule || 'guobiao'),
    sub_rule: title.sub_rule == null ? null : String(title.sub_rule),
    room_type: 'local',
    match_type: 'local',
    players: [0, 1, 2, 3].map((index) => ({
      user_id: Number(title[`p${index}_uid`]) || 900000000 + index,
      username: String(title[`p${index}_name`] || `玩家 ${index + 1}`),
      score: scores[index],
      rank: Number(rankByIndex[index]) || index + 1,
      original_player_index: index,
    })),
    record: {
      game_title: title,
      game_round: rounds,
    },
  }
}

export function saveLocalReplayRecord(value: SalasasaRecord | PublicGameRecord): string {
  const record = normalizeRecord(value)
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(record))
  } catch {
    throw new Error('牌谱过大，浏览器无法暂存用于 2D 回放')
  }
  return LOCAL_REPLAY_ID
}

export function loadLocalReplayRecord(gameId: string): PublicGameRecord | null {
  if (gameId !== LOCAL_REPLAY_ID) return null
  const raw = sessionStorage.getItem(STORAGE_KEY)
  if (!raw) throw new Error('本地牌谱已失效，请返回转换工具重新转换')
  return JSON.parse(raw) as PublicGameRecord
}

export function isLocalReplayRecord(gameId: unknown): boolean {
  return String(gameId || '') === LOCAL_REPLAY_ID
}
