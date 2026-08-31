const DB_NAME = 'om.recordCache'
const STORE_NAME = 'records'
const DB_VERSION = 1

function openDb() {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === 'undefined') {
      reject(new Error('IndexedDB is unavailable'))
      return
    }
    const request = indexedDB.open(DB_NAME, DB_VERSION)
    request.onupgradeneeded = () => {
      const db = request.result
      if (!db.objectStoreNames.contains(STORE_NAME)) {
        db.createObjectStore(STORE_NAME, { keyPath: 'game_id' })
      }
    }
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error ?? new Error('Failed to open IndexedDB'))
  })
}

function runStore(mode, fn) {
  return openDb().then((db) => new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, mode)
    const store = tx.objectStore(STORE_NAME)
    let result
    try {
      result = fn(store)
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

/** @param {Array<{ game_id: string, record: any, rank?: number|null }>} items */
export async function putLocalRecords(items) {
  const rows = (items || []).filter((item) => item?.game_id && item.record != null)
  if (!rows.length) return
  const savedAt = Date.now()
  await runStore('readwrite', (store) => {
    for (const item of rows) {
      store.put({
        game_id: String(item.game_id),
        record: item.record,
        rank: item.rank != null ? Number(item.rank) : null,
        created_at: item.created_at || null,
        saved_at: savedAt,
      })
    }
  })
}

/** @param {string[]} ids @returns {Promise<Set<string>>} */
export async function getLocalRecordIdSet(ids) {
  const list = (ids || []).map(String).filter(Boolean)
  if (!list.length) return new Set()
  return runStore('readonly', (store) => {
    const found = new Set()
    for (const id of list) {
      const req = store.get(id)
      req.onsuccess = () => {
        if (req.result) found.add(id)
      }
    }
    return found
  })
}

/** @param {string[]} ids */
export async function getLocalRecords(ids) {
  const list = (ids || []).map(String).filter(Boolean)
  if (!list.length) return []
  const byId = await runStore('readonly', (store) => {
    const map = new Map()
    for (const id of list) {
      const req = store.get(id)
      req.onsuccess = () => {
        if (req.result) map.set(id, req.result)
      }
    }
    return map
  })
  return list.map((id) => byId.get(id)).filter(Boolean)
}

function collectPlayersFromTitle(title, map) {
  if (!title || typeof title !== 'object') return
  for (let i = 0; i < 4; i += 1) {
    const uid = Number(title[`p${i}_uid`])
    if (!Number.isFinite(uid) || uid <= 0) continue
    const name = String(title[`p${i}_name`] || '').trim()
    const prev = map.get(uid) || { userId: uid, username: name || String(uid), count: 0 }
    prev.count += 1
    if (name) prev.username = name
    map.set(uid, prev)
  }
}

/** 扫描本机已缓存牌谱，列出出现过的玩家（按入局次数降序）。 */
export async function listCachedPlayers() {
  const db = await openDb()
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, 'readonly')
    const store = tx.objectStore(STORE_NAME)
    const map = new Map()
    const req = store.openCursor()
    req.onsuccess = () => {
      const cursor = req.result
      if (!cursor) return
      collectPlayersFromTitle(cursor.value?.record?.game_title, map)
      cursor.continue()
    }
    req.onerror = () => {
      db.close()
      reject(req.error ?? new Error('Failed to scan cached records'))
    }
    tx.oncomplete = () => {
      db.close()
      resolve([...map.values()].sort((a, b) => b.count - a.count || a.userId - b.userId))
    }
    tx.onerror = () => {
      db.close()
      reject(tx.error ?? new Error('IndexedDB transaction failed'))
    }
  })
}
