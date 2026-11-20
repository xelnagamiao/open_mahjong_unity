import {
  openReplayDatabase,
  replayTransaction,
  saveReplayRows,
  loadReplay,
  requestResult,
} from './browserRecordDatabase.js'

const open = () => openReplayDatabase('om.recordCache')

export async function putLocalRecords(items) {
  const rows = (items || [])
    .filter((item) => item?.game_id && item.record != null)
    .map((item) => ({
      ...item,
      game_id: String(item.game_id),
      rank: item.rank != null ? Number(item.rank) : null,
      created_at: item.created_at || null,
      saved_at: Date.now(),
    }))
  if (rows.length) await replayTransaction(open, 'readwrite', (stores) => saveReplayRows(stores, rows))
}

export async function getLocalRecordIdSet(ids) {
  const list = (ids || []).map(String).filter(Boolean)
  const found = new Set()
  if (list.length)
    await replayTransaction(open, 'readonly', (stores) =>
      Promise.all(
        list.map(async (id) => {
          if ((await requestResult(stores.records.getKey(id))) || (await requestResult(stores.aliases.getKey(id))))
            found.add(id)
        }),
      ),
    )
  return found
}

export async function getLocalRecords(ids) {
  const list = (ids || []).map(String).filter(Boolean)
  if (!list.length) return []
  const rows = await replayTransaction(open, 'readonly', (stores) =>
    Promise.all(list.map((id) => loadReplay(stores, id))),
  )
  return rows.filter(Boolean)
}

export async function listCachedPlayers() {
  return replayTransaction(
    open,
    'readonly',
    (stores) =>
      new Promise((resolve, reject) => {
        const players = new Map(),
          req = stores.records.openCursor()
        req.onsuccess = () => {
          const cursor = req.result
          if (!cursor) {
            resolve([...players.values()].sort((a, b) => b.count - a.count || a.userId - b.userId))
            return
          }
          const seen = new Set()
          for (const table of Object.values(cursor.value.record.tables)) {
            const title = table.payload?.game_title
            if (!title) continue
            for (let i = 0; i < (Number(title.player_count) || 4); i++) {
              const uid = Number(title['p' + i + '_uid'])
              if (!Number.isSafeInteger(uid) || uid <= 0 || seen.has(uid)) continue
              seen.add(uid)
              const name = String(
                table.players?.find((p) => p.user_id === uid)?.username || title['p' + i + '_name'] || '',
              ).trim()
              const player = players.get(uid) || { userId: uid, username: name || String(uid), count: 0 }
              player.count++
              if (name) player.username = name
              players.set(uid, player)
            }
          }
          cursor.continue()
        }
        req.onerror = () => reject(req.error)
      }),
  )
}
