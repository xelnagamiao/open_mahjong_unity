const defaultBase = '/unity-game/StreamingAssets/WebGLContent/'
const bundlePath = /^(Shared|DXT|ASTC)\/([a-z0-9_]+)-([a-f0-9]{64})\.bundle$/
const cacheName = 'open-mahjong-webgl-content-v1'
class InvalidBundle extends Error {}

export function detectUnityTextureProfile(linear, makeCanvas = () => document.createElement('canvas')) {
  const canvas = makeCanvas()
  canvas.width = canvas.height = 1
  // Match the game's webGLPowerPreference=2 on machines with two GPUs.
  const gl = canvas.getContext('webgl2', { powerPreference: 'high-performance' })
  if (!gl) throw new Error('WebGL 2 不可用')
  try {
    const astc = gl.getExtension('WEBGL_compressed_texture_astc')
    const dxt = gl.getExtension('WEBGL_compressed_texture_s3tc')
    const srgb = !linear || gl.getExtension('WEBGL_compressed_texture_s3tc_srgb')
    return astc && !(dxt && srgb) ? 'ASTC' : 'DXT'
  } finally {
    gl.getExtension('WEBGL_lose_context')?.loseContext()
  }
}

function selectedFiles(manifest, profile, base) {
  const list = manifest[profile.toLowerCase()]
  if (!Array.isArray(list) || !list.length) throw new Error('WebGL 资源清单为空')
  const files = new Map()
  for (const item of list) {
    const parts = bundlePath.exec(item?.file)
    if (!parts || (parts[1] !== 'Shared' && parts[1] !== profile) || item.name !== parts[2]
        || !Number.isSafeInteger(item.bytes) || item.bytes <= 0 || item.bytes > 0x7fffffff) {
      throw new Error('WebGL 资源清单无效')
    }
    const url = new URL(item.file, base).href
    const prior = files.get(item.name)
    if (prior && (prior.url !== url || prior.size !== item.bytes)) throw new Error('WebGL 资源清单冲突')
    files.set(item.name, { url, size: item.bytes, hash: parts[3], file: item.file })
  }
  return [...files.values()]
}

async function readBundle(response, file, signal, progress) {
  const failSize = () => new InvalidBundle('WebGL 资源长度不符：' + file.file)
  const reader = response.body?.getReader()
  if (!reader) {
    const data = new Uint8Array(await response.arrayBuffer())
    signal?.throwIfAborted()
    if (data.length !== file.size) throw failSize()
    progress(data.length)
    return data
  }
  const cancel = () => { reader.cancel().catch(() => {}) }
  signal?.addEventListener('abort', cancel, { once: true })
  try {
    const data = new Uint8Array(file.size)
    let offset = 0
    for (;;) {
      signal?.throwIfAborted()
      const { value, done } = await reader.read()
      signal?.throwIfAborted()
      if (done) break
      if (value.byteLength > data.length - offset) throw failSize()
      data.set(value, offset)
      offset += value.byteLength
      progress(offset)
    }
    if (offset !== data.length) throw failSize()
    return data
  } catch (error) {
    cancel()
    throw error
  } finally {
    signal?.removeEventListener('abort', cancel)
    reader.releaseLock()
  }
}

async function verifiedBytes(response, file, signal, progress) {
  let bytes
  if (response instanceof Uint8Array) {
    bytes = response // IndexedDB already returned an independent structured clone.
    if (bytes.length !== file.size) throw new InvalidBundle('WebGL 资源长度不符：' + file.file)
  } else {
    if (!response.ok) {
      await response.body?.cancel()
      throw new Error('WebGL 资源 HTTP ' + response.status + '：' + file.file)
    }
    bytes = await readBundle(response, file, signal, progress)
  }
  signal?.throwIfAborted()
  let hash
  if (globalThis.crypto?.subtle) {
    const digest = await crypto.subtle.digest('SHA-256', bytes)
    hash = Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, '0')).join('')
  } else {
    const { sha256Hex } = await import('./seedVerify.js')
    hash = sha256Hex(bytes)
  }
  signal?.throwIfAborted()
  if (hash !== file.hash) throw new InvalidBundle('WebGL 资源 SHA-256 不符：' + file.file)
  return bytes
}

function cacheResponse(bytes) {
  // Avoid cloning/teeing the entire response (including the large scene bundle).
  let offset = 0
  return new Response(new ReadableStream({
    pull(controller) {
      const chunk = bytes.subarray(offset, offset + 256 * 1024)
      controller.enqueue(chunk)
      offset += chunk.length
      if (offset === bytes.length) controller.close()
    }
  }))
}

function openBundleDatabase(factory, signal) {
  if (!factory) return null
  return new Promise(resolve => {
    signal?.throwIfAborted()
    const req = factory.open(cacheName, 1)
    let abandoned = false
    const stop = () => {
      abandoned = true
      signal?.removeEventListener('abort', stop)
      resolve(null)
    }
    signal?.addEventListener('abort', stop, { once:true })
    req.onblocked = req.onerror = stop
    req.onupgradeneeded = () => {
      if (abandoned) { req.transaction.abort(); return }
      req.result.createObjectStore('bundles')
    }
    req.onsuccess = () => {
      signal?.removeEventListener('abort', stop)
      const db = req.result
      if (abandoned) { db.close(); return }
      db.onversionchange = () => db.close()
      function transaction(mode, operation) {
        return new Promise((done, fail) => {
          signal?.throwIfAborted()
          const tx = db.transaction('bundles',mode)
          const abort = () => { try { tx.abort() } catch { /* Already committed. */ } }
          const detach = () => signal?.removeEventListener('abort', abort)
          const result = operation(tx.objectStore('bundles'))
          signal?.addEventListener('abort',abort,{ once:true })
          tx.oncomplete = () => { detach(); done(result.result) }
          tx.onabort = () => { detach(); fail(signal?.reason || tx.error || new Error('IndexedDB transaction aborted')) }
        })
      }
      resolve({
        keys: () => transaction('readonly', store => store.getAllKeys()),
        match: url => transaction('readonly', store => store.get(url)),
        put: (url, data) => transaction('readwrite', store => store.put(data,url)),
        delete: url => transaction('readwrite', store => store.delete(url)),
        close: () => db.close()
      })
    }
  })
}

async function openBundleCache(cacheStorage, indexedDB, signal) {
  try {
    const cache = await (cacheStorage === undefined ? globalThis.caches : cacheStorage)?.open(cacheName)
    if (cache) return {
      keys: async () => (await cache.keys()).map(key => key.url),
      match: url => cache.match(url),
      put: (url,data) => cache.put(url,cacheResponse(data)),
      delete: url => cache.delete(url)
    }
  } catch { /* Bare HTTP and private mode can deny Cache Storage. */ }
  signal?.throwIfAborted()
  try { return await openBundleDatabase(indexedDB === undefined ? globalThis.indexedDB : indexedDB,signal) }
  catch { return null }
}

// Fetch itself does not disclose HTTP cache hits. Use positive same-origin timing
// evidence when already available; never probe, bypass the cache or guess by speed.
function httpCacheHit(url, since) {
  return globalThis.performance?.getEntriesByName?.(url, 'resource').some(entry =>
    entry.initiatorType === 'fetch' && entry.startTime >= since &&
    (entry.deliveryType === 'cache' || entry.responseStatus === 304 ||
      (entry.transferSize === 0 && entry.encodedBodySize > 0)))
}

export async function prepareUnityContent(manifest, {
  baseUrl = defaultBase, signal, onProgress, onDownloadProgress, fetchImpl = fetch, detectProfile = detectUnityTextureProfile,
  cacheStorage, indexedDB
} = {}) {
  signal?.throwIfAborted()
  if (manifest?.version !== 1 || typeof manifest.linear !== 'boolean') throw new Error('WebGL 资源清单版本无效')
  const base = new URL(baseUrl.toString().replace(/\/?$/, '/'), globalThis.location?.href)
  if (globalThis.location?.origin && base.origin !== globalThis.location.origin) throw new Error('WebGL 资源必须来自当前站点')
  const profile = detectProfile(manifest.linear)
  if (profile !== 'DXT' && profile !== 'ASTC') throw new Error('WebGL 纹理格式无效')
  const files = selectedFiles(manifest, profile, base)
  const retained = new Set([...files, ...selectedFiles(manifest, profile === 'DXT' ? 'ASTC' : 'DXT', base)].map(f => f.url))
  const total = files.reduce((sum, f) => sum + f.size, 0)
  const buffers = new Map()
  function dispose() {
    buffers.clear()
    signal?.removeEventListener('abort', dispose)
  }
  const cache = await openBundleCache(cacheStorage, indexedDB, signal)
  try {
    signal?.throwIfAborted()
    let oldKeys = []
    try { if (cache) oldKeys = await cache.keys() } catch { /* Pruning is best-effort. */ }
    let cached = 0, canWrite = !!cache
    const pending = []
    onProgress?.({ loaded:0, total, cached })
    for (const file of files) {
      signal?.throwIfAborted()
      let data, corrupt = false
      let saved
      try { saved = await cache?.match(file.url) } catch { /* Use the HTTP cache. */ }
      if (saved) {
        try {
          data = await verifiedBytes(saved, file, signal, () => {})
        } catch {
          signal?.throwIfAborted()
          corrupt = true
          try { await cache.delete(file.url) } catch { /* Never return corrupt bytes. */ }
        }
      }
      if (data) {
        buffers.set(file.url, data)
        cached += data.length
        onProgress?.({ loaded: cached, total, cached })
      } else {
        pending.push({ file, corrupt })
      }
    }
    let loaded = cached, downloaded = 0, downloadTotal = total - cached
    for (const { file, corrupt } of pending) {
      let data, high = 0, fromHttpCache = false, shown = false
      const download = async mode => {
        signal?.throwIfAborted()
        const since = globalThis.performance?.now()
        const response = await fetchImpl(file.url, { signal, cache: mode, mode: 'same-origin' })
        fromHttpCache = onDownloadProgress && httpCacheHit(file.url, since)
        const bytes = await verifiedBytes(response, file, signal, offset => {
          if (offset <= high) return
          high = offset
          onProgress?.({ loaded: loaded + offset, total, cached })
          if (!fromHttpCache && onDownloadProgress) {
            shown = true
            onDownloadProgress({ loaded: downloaded + offset, total: downloadTotal })
          }
        })
        fromHttpCache ||= onDownloadProgress && httpCacheHit(file.url, since)
        return bytes
      }
      try {
        data = await download(corrupt ? 'reload' : 'force-cache')
      } catch (error) {
        if (corrupt || !(error instanceof InvalidBundle)) throw error
        data = await download('reload')
      }
      if (fromHttpCache) {
        downloadTotal -= file.size
        if (shown || downloaded) onDownloadProgress({ loaded: downloaded, total: downloadTotal })
      } else {
        downloaded += file.size
        if (!shown) onDownloadProgress?.({ loaded: downloaded, total: downloadTotal })
      }
      if (canWrite) {
        try { await cache.put(file.url, data) } catch { canWrite = false }
      }
      signal?.throwIfAborted()
      buffers.set(file.url, data)
      loaded += data.length
    }
    // Only prune our own old hashed bundles, after the entire new set verifies.
    for (const key of oldKeys) {
      signal?.throwIfAborted()
      if (typeof key === 'string' && key.startsWith(base.href) && bundlePath.test(key.slice(base.href.length)) && !retained.has(key)) {
        try { await cache.delete(key) } catch { /* Pruning is best-effort. */ }
      }
    }
    signal?.throwIfAborted()
    signal?.addEventListener('abort', dispose, { once: true })
    return {
      profile, dispose,
      take(url) {
        const key = new URL(url, base).href
        const data = buffers.get(key)
        buffers.delete(key)
        if (!buffers.size) dispose()
        return data ?? null
      }
    }
  } catch (error) {
    dispose()
    throw error
  } finally {
    cache?.close?.()
  }
}
