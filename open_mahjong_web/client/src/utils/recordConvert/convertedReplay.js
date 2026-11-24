import { mergeReplayRows, stringifyReplayJSON } from '../browserRecordMigration.js'
import { sha256Hex } from '../seedVerify.js'
import { expandStoredReplay } from '../../../../shared/replayStorage.mjs'

function scoreChange(tick) {
  const action = String(tick?.[0] ?? '')
  let value
  if (['hu_self', 'hu_first', 'hu_second', 'hu_third'].includes(action)) value = tick[4]
  else if (action === 'hu_riichi') value = tick[6]
  else if (action === 'ryuukyoku' || action === 'liuju') value = tick[2]
  if (!Array.isArray(value) || value.length < 4) return null
  return value.slice(0, 4).map((item) => Number(item) || 0)
}

function finalScores(rounds) {
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

// Standalone external imports have no cloud ID. Their content gives them a stable local ID.
export function convertedReplay(value) {
  const record = value.record || value
  if (record.format === 'open_mahjong.match_record') return expandStoredReplay(record)
  const title = record.game_title || {}
  const rounds = record.game_round || {}
  if (!Object.keys(rounds).length) throw new Error('转换结果中没有可播放的小局')
  const scores = finalScores(rounds)
  const ranks = scores
    .map((score, index) => ({ score, index }))
    .sort((left, right) => right.score - left.score)
  const rankByIndex = Object.fromEntries(ranks.map((item, index) => [item.index, index + 1]))

  const players = [0, 1, 2, 3].map((index) => ({
      user_id: Number(title[`p${index}_uid`]) || 900000000 + index,
      username: String(title[`p${index}_name`] || `玩家 ${index + 1}`),
      score: scores[index],
      rank: Number(rankByIndex[index]) || index + 1,
      original_player_index: index,
    }))
  const game_id = 'L' + sha256Hex(stringifyReplayJSON(record)).slice(0,15)
  return mergeReplayRows([{ game_id, record, players: value.players || players }]).row.record
}
