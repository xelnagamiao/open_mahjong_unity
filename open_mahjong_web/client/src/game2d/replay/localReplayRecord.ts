import { convertedReplay } from '@/utils/recordConvert/convertedReplay.js'
import type { PublicGameRecord } from './recordReplay'
import { isDuplicateGameRecord } from '@/utils/duplicateWalls'
import { parseReplayJSON, stringifyReplayJSON } from '@/utils/browserRecordMigration'

const STORAGE_KEY = 'salasasa:local-replay-record'
export const LOCAL_REPLAY_ID = 'local-converted-record'
/** Unity `recordId` 正则只接受 1–16 位字母数字；与 2D 共用同一份 sessionStorage。 */
export const UNITY_LOCAL_REPLAY_ID = 'localconverted'

function normalizeRecord(value: PublicGameRecord | object) {
  const record = convertedReplay(value)
  const players = Object.values(record.tables).flatMap((t: any) => t.players.map((p: any) => ({ ...p, table_id: t.table_id })))
  return { game_id: UNITY_LOCAL_REPLAY_ID, cloud_saved: false, record, created_at: new Date().toISOString(),
    rule: record.rule, sub_rule: record.sub_rule, players, room_type: 'local', match_type: 'local' }
}

export function saveLocalReplayRecord(value: PublicGameRecord | object): string {
  const record = normalizeRecord(value)
  if (isDuplicateGameRecord(record)) throw new Error('复式牌谱不保存在本地，请在密钥解禁后通过数据站查看')
  try {
    sessionStorage.setItem(STORAGE_KEY, stringifyReplayJSON(record))
  } catch {
    throw new Error('牌谱过大，浏览器无法暂存用于回放')
  }
  return LOCAL_REPLAY_ID
}

export function loadLocalReplayRecord(gameId: string): PublicGameRecord | null {
  if (!isLocalReplayRecord(gameId)) return null
  const raw = sessionStorage.getItem(STORAGE_KEY)
  if (!raw) throw new Error('本地牌谱已失效，请返回转换工具重新转换')
  const record = parseReplayJSON(raw) as PublicGameRecord
  if (isDuplicateGameRecord(record)) throw new Error('复式牌谱请通过数据站查看')
  return record
}

export function isLocalReplayRecord(gameId: unknown): boolean {
  const id = String(gameId || '')
  return id === LOCAL_REPLAY_ID || id === UNITY_LOCAL_REPLAY_ID
}
