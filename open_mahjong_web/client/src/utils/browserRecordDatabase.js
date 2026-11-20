import {
  attachArchivedTable,
  legacyRowIdentity,
  mergeReplayRows,
  replayKey,
  selectCachedReplay,
  standardBody,
} from './browserRecordMigration.js'

function requireData(ok, message) {
  if (!ok) throw new Error('牌谱迁移异常（' + message + '）')
}

function scan(store, visit) {
  return new Promise((resolve, reject) => {
    const scan = store.openCursor()
    scan.onsuccess = () => {
      const cursor = scan.result
      if (!cursor) return resolve()
      try {
        visit(cursor.value)
        cursor.continue()
      } catch (error) {
        reject(error)
      }
    }
    scan.onerror = () => reject(scan.error)
  })
}

async function migrateLegacyStores(db, tx, records, aliases, meta, makeIndexEntry) {
  if (!db.objectStoreNames.contains('records')) return
  const old = tx.objectStore('records')
  const legacy = new Map(),
    published = new Map()
  await scan(old, (row) => {
    const { id, sourceKeys } = legacyRowIdentity(row)
    legacy.set(row.game_id, id)
    for (const source of sourceKeys) {
      requireData(!published.has(source) || published.get(source) === id, '桌编号冲突')
      published.set(source, id)
    }
  })
  const groups = new Map()
  for (const [key, id] of legacy) {
    const primary = id || published.get(key)
    requireData(primary && (!published.has(key) || published.get(key) === primary), '复式来源无法核验')
    if (!groups.has(primary)) groups.set(primary, [])
    groups.get(primary).push(key)
    legacy.set(key, primary)
  }
  const info = new Map(),
    claimed = new Set()
  for (const [id, keys] of groups) {
    const rows = await Promise.all(keys.map((key) => requestResult(old.get(key))))
    const result = mergeReplayRows(rows.map((row) => attachArchivedTable(row, rows)))
    records.add(result.row)
    for (const alias of result.aliases) {
      requireData(!claimed.has(alias.game_id) && !groups.has(alias.game_id), '旧编号冲突')
      claimed.add(alias.game_id)
      aliases.add(alias)
    }
    if (makeIndexEntry) info.set(id, makeIndexEntry(result.row))
  }
  if (makeIndexEntry) {
    const index = (await requestResult(meta.get('index'))) || []
    requireData(Array.isArray(index), '索引格式错误')
    const next = new Map()
    for (const card of index) {
      const id = legacy.get(card.game_id)
      requireData(id, '索引指向缺失牌谱')
      const previous = next.get(id)
      if (!previous) next.set(id, { ...card, ...info.get(id), is_favorite: !!card.is_favorite })
      else {
        previous.is_favorite ||= !!card.is_favorite
        if (card.note && previous.note !== card.note) (previous.table_notes ??= {})[card.game_id] = card.note
      }
    }
    for (const [id, card] of info) if (!next.has(id)) next.set(id, card)
    meta.put([...next.values()], 'index')
  }
  db.deleteObjectStore('records')
}

// The version number advances only if every body, locator and index entry commits.
export function openReplayDatabase(name, makeIndexEntry) {
  return new Promise((resolve, reject) => {
    const open = indexedDB.open(name, 2)
    let cause
    open.onupgradeneeded = () => {
      const db = open.result,
        tx = open.transaction
      const records = db.createObjectStore('replays', { keyPath: 'game_id' })
      const aliases = db.createObjectStore('aliases', { keyPath: 'game_id' })
      aliases.createIndex('replay_id', 'replay_id')
      const meta = db.objectStoreNames.contains('meta') ? tx.objectStore('meta') : db.createObjectStore('meta')
      migrateLegacyStores(db, tx, records, aliases, meta, makeIndexEntry).catch((error) => {
        cause = error
        try {
          tx.abort()
        } catch {}
      })
    }
    open.onsuccess = () => {
      open.result.onversionchange = () => open.result.close()
      resolve(open.result)
    }
    open.onerror = () => reject(cause || open.error)
  })
}

const requestResult = (req) =>
  new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })

export async function replayTransaction(open, mode, work) {
  const db = await open()
  try {
    const tx = db.transaction(['replays', 'aliases', 'meta'], mode)
    const finished = new Promise((resolve, reject) => {
      tx.oncomplete = resolve
      tx.onabort = tx.onerror = () => reject(tx.error || new Error('牌谱事务已中止'))
    })
    const stores = {
      records: tx.objectStore('replays'),
      aliases: tx.objectStore('aliases'),
      meta: tx.objectStore('meta'),
    }
    try {
      const [value] = await Promise.all([work(stores), finished])
      return value
    } catch (error) {
      try {
        tx.abort()
      } catch {}
      throw error
    }
  } finally {
    db.close()
  }
}

export async function saveReplayRows(stores, rows) {
  const groups = new Map(),
    saved = []
  for (const row of rows) {
    const id = replayKey(row)
    if (!groups.has(id)) groups.set(id, [])
    groups.get(id).push(row)
  }
  for (const [id, group] of groups) {
    requireData(!(await requestResult(stores.aliases.getKey(id))), '主编号已属于另一桌')
    const before = await requestResult(stores.records.get(id))
    const denied = new Set()
    for (const entry of group) {
      const incoming = standardBody(entry)
      if (incoming)
        for (const [k, t] of Object.entries(incoming.tables)) {
          if (t.reason === 'access_denied') denied.add(k)
        }
    }
    const result = mergeReplayRows(before && !denied.size ? [before, ...group] : group)
    if (denied.size) {
      const previousAliases = await requestResult(stores.aliases.index('replay_id').getAll(id))
      for (const alias of previousAliases) if (denied.has(alias.table_id)) stores.aliases.delete(alias.game_id)
    }
    for (const alias of result.aliases) {
      requireData(!(await requestResult(stores.records.getKey(alias.game_id))), '桌编号已属于另一场')
      const old = await requestResult(stores.aliases.get(alias.game_id))
      requireData(!old || (old.replay_id === id && old.table_id === alias.table_id), '桌编号冲突')
      stores.aliases.put({ ...old, ...alias })
    }
    stores.records.put(result.row)
    saved.push(result.row)
  }
  return saved
}

export async function loadReplay(stores, id) {
  const alias = await requestResult(stores.aliases.get(id))
  const row = await requestResult(stores.records.get(alias?.replay_id || id))
  return selectCachedReplay(row, alias)
}

export { requestResult }
