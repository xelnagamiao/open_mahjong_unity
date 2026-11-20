import { isDuplicateGameRecord } from './duplicateWalls.js'
import {
  openReplayDatabase,
  replayTransaction,
  saveReplayRows,
  loadReplay,
  requestResult,
} from './browserRecordDatabase.js'

export const LOCAL_GAME_RECORD_MAX = 200
export const LOCAL_GAME_ID_PREFIX = 'L'
const open = () => openReplayDatabase('om.localGameRecords', toLocalRecordInfo)

export function toLocalRecordInfo(detail) {
  if (!detail?.game_id) return null
  const table = detail.record?.tables?.[detail.selected_table_id]
  return {
    game_id: String(detail.game_id),
    cloud_saved: detail.cloud_saved === true,
    rule: detail.rule || '',
    sub_rule: detail.sub_rule ?? null,
    match_type: detail.match_type || '',
    created_at: detail.created_at || '',
    players: detail.players || [],
    room_type: table?.payload?.game_title?.room_type ?? detail.room_type ?? null,
    is_favorite: !!detail.is_favorite,
    ...(detail.match_summary ? { match_summary: detail.match_summary } : {}),
  }
}

export function isLocalOnlyGameId(gameId) {
  return String(gameId || '').startsWith(LOCAL_GAME_ID_PREFIX)
}

export async function saveLocalGameRecord(detail) {
  if (!detail?.record || !detail.game_id || isDuplicateGameRecord(detail)) return
  try {
    await replayTransaction(open, 'readwrite', async (stores) => {
      const [row] = await saveReplayRows(stores, [{ ...detail, perspective: false, saved_at: Date.now() }])
      const cards = (await requestResult(stores.meta.get('index'))) || []
      const next = [toLocalRecordInfo(row), ...cards.filter((card) => card.game_id !== row.game_id)]
      for (const old of next.splice(LOCAL_GAME_RECORD_MAX)) {
        const ids = await requestResult(stores.aliases.index('replay_id').getAllKeys(old.game_id))
        for (const id of ids) stores.aliases.delete(id)
        stores.records.delete(old.game_id)
      }
      stores.meta.put(next, 'index')
    })
  } catch (error) {
    console.warn('保存本地牌谱失败', error)
  }
}

export async function listLocalGameRecords() {
  try {
    return (await replayTransaction(open, 'readonly', (stores) => requestResult(stores.meta.get('index')))) || []
  } catch (error) {
    console.warn('读取本地牌谱列表失败', error)
    return []
  }
}

export async function getLocalGameRecord(gameId) {
  if (!gameId) return null
  try {
    return await replayTransaction(open, 'readonly', (stores) => loadReplay(stores, String(gameId)))
  } catch (error) {
    console.warn('读取本地牌谱失败', error)
    return null
  }
}
