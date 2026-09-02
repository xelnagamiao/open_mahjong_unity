const DB_NAME = 'om.localGameRecords'
const DB_VERSION = 1
const RECORD_STORE = 'records'
const META_STORE = 'meta'
const INDEX_KEY = 'index'
export const LOCAL_GAME_RECORD_MAX = 200
export const LOCAL_GAME_ID_PREFIX = 'L'

function openDb() {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === 'undefined') {
      reject(new Error('IndexedDB is unavailable'))
      return
    }
    const request = indexedDB.open(DB_NAME, DB_VERSION)
    request.onupgradeneeded = () => {
      const db = request.result
      if (!db.objectStoreNames.contains(RECORD_STORE)) {
        db.createObjectStore(RECORD_STORE, { keyPath: 'game_id' })
      }
      if (!db.objectStoreNames.contains(META_STORE)) {
        db.createObjectStore(META_STORE)
      }
    }
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error ?? new Error('Failed to open IndexedDB'))
  })
}

function runTx(mode, fn) {
  return openDb().then((db) => new Promise((resolve, reject) => {
    const tx = db.transaction([RECORD_STORE, META_STORE], mode)
    let result
    try {
      result = fn(tx.objectStore(RECORD_STORE), tx.objectStore(META_STORE))
    } catch (err) {
      db.close()
      reject(err)
      return
    }
    tx.oncomplete = () => {
      db.close()
      resolve(result)
    }
    tx.onerror = () => {
      db.close()
      reject(tx.error ?? new Error('IndexedDB transaction failed'))
    }
    tx.onabort = () => {
      db.close()
      reject(tx.error ?? new Error('IndexedDB transaction aborted'))
    }
  }))
}

function requestToPromise(request) {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
  })
}

function roomTypeOf(detail) {
  const title = detail?.record?.game_title
  if (title && typeof title === 'object' && title.room_type != null) {
    return String(title.room_type)
  }
  return detail?.room_type != null ? String(detail.room_type) : null
}

export function toLocalRecordInfo(detail) {
  if (!detail || !detail.game_id) return null
  return {
    game_id: String(detail.game_id),
    rule: detail.rule || '',
    sub_rule: detail.sub_rule ?? null,
    match_type: detail.match_type || '',
    created_at: detail.created_at || '',
    players: Array.isArray(detail.players) ? detail.players : [],
    room_type: roomTypeOf(detail),
    is_favorite: false,
  }
}

export function isLocalOnlyGameId(gameId) {
  return String(gameId || '').startsWith(LOCAL_GAME_ID_PREFIX)
}

/** @param {object} detail 服务端 game_end_info.record_detail */
export async function saveLocalGameRecord(detail) {
  if (!detail?.record || !detail.game_id) return
  const row = {
    ...detail,
    game_id: String(detail.game_id),
    perspective: false,
    saved_at: Date.now(),
  }
  const info = toLocalRecordInfo(row)
  try {
    const db = await openDb()
    await new Promise((resolve, reject) => {
      const tx = db.transaction([RECORD_STORE, META_STORE], 'readwrite')
      const records = tx.objectStore(RECORD_STORE)
      const meta = tx.objectStore(META_STORE)
      records.put(row)
      const indexReq = meta.get(INDEX_KEY)
      indexReq.onsuccess = () => {
        const list = Array.isArray(indexReq.result) ? indexReq.result.slice() : []
        const next = list.filter((item) => item && item.game_id !== row.game_id)
        next.unshift(info)
        const dropped = next.splice(LOCAL_GAME_RECORD_MAX)
        for (const old of dropped) {
          if (old?.game_id) records.delete(String(old.game_id))
        }
        meta.put(next, INDEX_KEY)
      }
      tx.oncomplete = () => {
        db.close()
        resolve()
      }
      tx.onerror = () => {
        db.close()
        reject(tx.error ?? new Error('IndexedDB transaction failed'))
      }
      tx.onabort = () => {
        db.close()
        reject(tx.error ?? new Error('IndexedDB transaction aborted'))
      }
    })
  } catch (error) {
    console.warn('保存本地牌谱失败', error)
  }
}

export async function listLocalGameRecords() {
  try {
    const list = await runTx('readonly', (_records, meta) => requestToPromise(meta.get(INDEX_KEY)))
    return (Array.isArray(list) ? list : []).filter((item) => item?.game_id)
  } catch (error) {
    console.warn('读取本地牌谱列表失败', error)
    return []
  }
}

export async function getLocalGameRecord(gameId) {
  const id = String(gameId || '')
  if (!id) return null
  try {
    const row = await runTx('readonly', (records) => requestToPromise(records.get(id)))
    if (!row?.record) return null
    return row
  } catch (error) {
    console.warn('读取本地牌谱失败', error)
    return null
  }
}
