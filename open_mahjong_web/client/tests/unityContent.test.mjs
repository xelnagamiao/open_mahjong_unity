import test from 'node:test'
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { detectUnityTextureProfile, prepareUnityContent } from '../src/utils/unityContent.js'
import { sha256Hex } from '../src/utils/seedVerify.js'

const baseUrl = 'https://salasasa.test/unity-game/StreamingAssets/WebGLContent/'
const entries = new Map()
function bundle(dir, name, chars) {
  const data = new TextEncoder().encode(chars)
  const hash = createHash('sha256').update(data).digest('hex')
  const file = dir + '/' + name + '-' + hash + '.bundle'
  entries.set(baseUrl + file, data)
  return { name, file, bytes: data.length }
}
const shared = bundle('Shared', 'images', 'shared images')
const dxt = bundle('DXT', 'textures', 'dxt only')
const astc = bundle('ASTC', 'textures', 'astc only')
const manifest = { version: 1, linear: true, dxt: [shared, dxt], astc: [shared, shared, astc] }
const fetchData = async url => new Response(entries.get(url))
// Each cache scenario opts into its own store; network tests always start cold.
const options = { baseUrl, cacheStorage: null, indexedDB: null, detectProfile: () => 'ASTC', fetchImpl: fetchData }

test('temporary context follows Unity linear/DXT/ASTC selection and always releases the context', () => {
  for (const linear of [false, true]) for (const a of [false, true]) for (const d of [false, true]) for (const s of [false, true]) {
    let releases = 0
    const extensions = {
      WEBGL_compressed_texture_astc: a, WEBGL_compressed_texture_s3tc: d, WEBGL_compressed_texture_s3tc_srgb: s,
      WEBGL_lose_context: { loseContext: () => releases++ }
    }
    const canvas = { getContext(name, attributes) {
      assert.equal(name, 'webgl2')
      assert.equal(attributes.powerPreference, 'high-performance')
      return { getExtension: name => extensions[name] }
    } }
    assert.equal(detectUnityTextureProfile(linear, () => canvas), a && (!d || (linear && !s)) ? 'ASTC' : 'DXT')
    assert.equal(releases, 1)
    assert.equal(canvas.width * canvas.height, 1)
  }
  assert.throws(() => detectUnityTextureProfile(true, () => ({ getContext: () => null })), /WebGL 2/)
})

test('pure-JS fallback matches native SHA for UTF-8 and binary subarrays across padding boundaries', async () => {
  const native = data => createHash('sha256').update(data).digest('hex')
  assert.equal(sha256Hex('牌谱😀'), native('牌谱😀'))
  const source = Uint8Array.from({length:65540}, (_,i) => (i*33+0xb1) & 255)
  for (const n of [0,1,55,56,57,63,64,65,119,120,127,128,65536]) {
    const sub = source.subarray(3,3+n)
    assert.equal(sha256Hex(sub),native(sub))
  }
  const binary = source.subarray(15,82)
  const entry = { name:'media',file:'Shared/media-'+native(binary)+'.bundle',bytes:binary.length }
  const original = Object.getOwnPropertyDescriptor(globalThis,'crypto')
  Object.defineProperty(globalThis,'crypto',{ configurable:true,value:{} })
  try {
    const res = await prepareUnityContent({ version:1,linear:true,astc:[entry],dxt:[entry] }, {
      ...options,fetchImpl:async () => new Response(binary)
    })
    assert.deepEqual(res.take(entry.file),binary)
  } finally {
    if (original) Object.defineProperty(globalThis,'crypto',original)
    else delete globalThis.crypto
  }
})

test('fetches and verifies only the selected set once; transfers ownership without cloning the payload', async () => {
  const calls = []
  const stages = []
  const downloads = []
  const result = await prepareUnityContent(manifest, {
    ...options,
    detectProfile(linear) { assert.equal(linear, true); return 'ASTC' },
    async fetchImpl(url) {
      calls.push(url)
      const data = entries.get(url)
      return new Response(new ReadableStream({ start(controller) {
        controller.enqueue(data.subarray(0, 2)); controller.enqueue(data.subarray(2)); controller.close()
      } }))
    },
    onProgress: p => stages.push(p),
    onDownloadProgress: p => downloads.push(p)
  })
  assert.deepEqual(calls, [baseUrl+shared.file, baseUrl+astc.file])
  const total = shared.bytes+astc.bytes
  assert.deepEqual(stages, [0, 2, shared.bytes, shared.bytes+2, total].map(loaded => ({ loaded, total, cached:0 })))
  assert.deepEqual(downloads,[2, shared.bytes, shared.bytes+2, total].map(loaded => ({loaded,total})))
  assert.equal(result.profile, 'ASTC')
  assert.deepEqual(result.take(shared.file), entries.get(baseUrl+shared.file))
  assert.equal(result.take(baseUrl+shared.file), null)
  assert.deepEqual(result.take(baseUrl+astc.file), entries.get(baseUrl+astc.file))
  assert.equal(result.take(dxt.file), null)
  result.dispose()
})

test('length, SHA-256, HTTP, and manifest mismatches reject instead of trying another profile', async () => {
  for (const [response, pattern] of [
    [new Response(new Uint8Array(shared.bytes+1)), /长度/],
    [new Response(new Uint8Array(shared.bytes-1)), /长度/],
    [new Response(new Uint8Array(shared.bytes)), /SHA-256/],
    [new Response(null, {status:404}), /404/]
  ]) {
    const urls = []
    await assert.rejects(prepareUnityContent(manifest, { ...options,
      async fetchImpl(url) { urls.push(url); return response.clone() }
    }), pattern)
    const url = baseUrl+shared.file
    assert.deepEqual(urls, pattern.source === '404' ? [url] : [url,url])
  }
  for (const wrong of [dxt, { ...shared, name: 'textures' }, { ...shared, file: '../'+shared.file },
    { ...shared, bytes: 0 }, { ...shared, bytes: shared.bytes+1 }]) {
    await assert.rejects(prepareUnityContent({ ...manifest, astc: [shared,wrong] }, {
      ...options, fetchImpl() { assert.fail('invalid manifest must not cause a fetch') }
    }), /清单/)
  }
})

function cacheFixture() {
  const stored = new Map()
  const names = new Set()
  const cache = {
    keys: async () => [...stored.keys()].map(url => new Request(url)),
    match: async url => stored.has(url) ? new Response(stored.get(url)) : undefined,
    delete: async key => stored.delete(typeof key === 'string' ? key : key.url),
    put: async (url, response) => stored.set(url,new Uint8Array(await response.arrayBuffer()))
  }
  return { stored, cache, names, cacheStorage:{ async open(name) { names.add(name); return cache } } }
}

test('repeat launch does zero fetches; new manifest fetches only the changed file and prunes only our stale hashes', async () => {
  const { stored, names, cacheStorage } = cacheFixture()
  const requests = []
  const progress = []
  const opts = { ...options, cacheStorage, onProgress: p => progress.push(p),
    fetchImpl: async (url,init) => { requests.push([url,init.cache]); return fetchData(url) } }
  const read = async (m, extra) => (await prepareUnityContent(m, { ...opts,...extra })).dispose()
  await read(manifest)
  assert.deepEqual(requests, [shared,astc].map(b => [baseUrl+b.file, 'force-cache']))
  requests.length = 0
  await read(manifest)
  assert.deepEqual(requests, [])
  assert.equal(progress.at(-1).cached,progress.at(-1).total)
  await read(manifest,{ detectProfile: () => 'DXT' })
  assert.deepEqual(requests,[[baseUrl+dxt.file,'force-cache']])
  const change = bundle('Shared','images','updated shared images')
  const next = { ...manifest, dxt:[change,dxt], astc:[change,astc] }
  const external = 'https://elsewhere.test/'+shared.file
  stored.set(external,new Uint8Array(10))
  requests.length = 0
  await read(next)
  assert.deepEqual(requests,[[baseUrl+change.file,'force-cache']])
  assert.equal(progress.at(-1).cached,astc.bytes)
  assert.equal(stored.has(baseUrl+shared.file),false)
  assert.equal(stored.has(baseUrl+dxt.file),true)
  assert.equal(stored.has(external),true)
  assert.deepEqual([...names],['open-mahjong-webgl-content-v1'])
})

test('corrupt cached bytes are reloaded just once; corrupt replacement is never stored or returned',async () => {
  const { cacheStorage, stored } = cacheFixture()
  const key = baseUrl+shared.file
  stored.set(key,new Uint8Array(shared.bytes))
  stored.set(baseUrl+astc.file,entries.get(baseUrl+astc.file))
  let requests=[]
  const opts = { ...options, cacheStorage, fetchImpl:async (url,init) => {
    requests.push([url,init.cache]); return fetchData(url)
  } }
  const result = await prepareUnityContent(manifest,opts)
  assert.deepEqual(requests,[[key,'reload']])
  assert.deepEqual(result.take(key),entries.get(key)); result.dispose()
  stored.set(key,new Uint8Array(shared.bytes));requests=[]
  await assert.rejects(prepareUnityContent(manifest,{ ...opts, fetchImpl:async (url,init) => {
    requests.push([url,init.cache]);return new Response(new Uint8Array(shared.bytes))
  } }),/SHA-256/)
  assert.deepEqual(requests,[[key,'reload']])
  assert.equal(stored.has(key),false)
})

test('download captions count only missing or corrupt packages, even when later entries are cached', async () => {
  const { stored, cacheStorage } = cacheFixture()
  stored.set(baseUrl+astc.file, entries.get(baseUrl+astc.file))
  const calls = [], downloads = [], all = []
  const run = async () => (await prepareUnityContent(manifest, {
    ...options, cacheStorage,
    onProgress: p => all.push(p),
    onDownloadProgress: p => downloads.push(p),
    async fetchImpl(url, init) { calls.push([url,init.cache]); return fetchData(url) }
  })).dispose()
  for (const mode of ['force-cache', 'reload']) {
    if (mode === 'reload') stored.set(baseUrl+shared.file, new Uint8Array(shared.bytes))
    await run()
    assert.deepEqual(calls, [[baseUrl+shared.file,mode]])
    assert.deepEqual(downloads, [{loaded:shared.bytes,total:shared.bytes}])
    assert.deepEqual(all.at(-1), {loaded:astc.bytes+shared.bytes,total:astc.bytes+shared.bytes,cached:astc.bytes})
    calls.length = downloads.length = all.length = 0
  }
  await run()
  assert.deepEqual(calls, [])
  assert.deepEqual(downloads, [])
  assert.equal(all.at(-1).loaded, all.at(-1).cached)
})

test('a same-origin HTTP cache hit already visible to ResourceTiming is not shown as a download', async t => {
  const timing = new Map()
  t.mock.method(performance, 'getEntriesByName', url => timing.get(url) || [])
  const captions = [], requests = []
  const result = await prepareUnityContent(manifest, {
    ...options,
    cacheStorage:null, indexedDB:null,
    onDownloadProgress: p => captions.push(p),
    async fetchImpl(url, init) {
      requests.push([url,init.cache])
      if (url === baseUrl+shared.file) {
        timing.set(url, [{ initiatorType:'fetch',startTime:performance.now(),transferSize:0,encodedBodySize:shared.bytes }])
      }
      return fetchData(url)
    }
  })
  assert.deepEqual(captions,[{loaded:astc.bytes,total:astc.bytes}])
  assert.deepEqual(requests,[shared,astc].map(f=>[baseUrl+f.file,'force-cache']))
  assert.deepEqual(result.take(shared.file),entries.get(baseUrl+shared.file))
  result.dispose()
})

test('late confirmation of an HTTP cache hit clears the provisional missing-file total', async t => {
  let stamp, complete = false
  t.mock.method(performance, 'getEntriesByName', () => complete ? [
    { initiatorType:'fetch',startTime:stamp,responseStatus:304 }
  ] : [])
  const captions = []
  const result = await prepareUnityContent({...manifest,astc:[shared]},{
    ...options, cacheStorage:null, indexedDB:null,
    onDownloadProgress: p => { captions.push(p);complete = true },
    fetchImpl: async url => { stamp = performance.now();return fetchData(url) }
  })
  assert.deepEqual(captions,[{loaded:shared.bytes,total:shared.bytes},{loaded:0,total:0}])
  result.dispose()
})

test('unavailable or full Cache Storage still uses HTTP force-cache and verifies binary payloads',async () => {
  const { cacheStorage, cache,stored } = cacheFixture()
  let attempts=0
  cache.put=async () => { attempts++;throw new DOMException('quota', 'QuotaExceededError') }
  for (const store of [cacheStorage,{ open() { throw new Error('disabled') } }]) {
    const modes=[]
    const res = await prepareUnityContent(manifest,{ ...options,cacheStorage:store,fetchImpl:(url, {cache:mode}) => {
      modes.push(mode);return fetchData(url)
    } })
    assert.deepEqual(res.take(shared.file),entries.get(baseUrl+shared.file))
    assert.deepEqual(modes,['force-cache','force-cache']);res.dispose()
  }
  assert.equal(attempts,1)
  assert.equal(stored.size,0)
})

test('abort interrupts an open stream, stops the queue and releases retained bundles', async () => {
  const abort = new AbortController()
  let cancels=0
  let requests=0
  const stream = new ReadableStream({
    start(c) { c.enqueue(new Uint8Array([1])) },
    cancel() { cancels++ }
  })
  await assert.rejects(prepareUnityContent(manifest, { ...options, signal:abort.signal,
    async fetchImpl(url, { signal }) {
      requests++; assert.equal(signal,abort.signal); return new Response(stream)
    },
    onProgress({ loaded }) { if (loaded > 0) abort.abort() }
  }), {name:'AbortError'})
  assert.equal(requests,1)
  assert.equal(cancels,1)
  assert.equal(stream.locked,false)

  const release = new AbortController()
  const source = await prepareUnityContent(manifest, { ...options, signal:release.signal })
  release.abort()
  assert.equal(source.take(shared.file),null)
  const other = await prepareUnityContent(manifest, options)
  other.dispose()
  assert.equal(other.take(astc.file),null)
})

function idbFixture() {
  const records = new Map(), opens = []
  let initialized = false, closes = 0
  const factory = { open(name, version) {
    opens.push([name,version])
    const req = { result:{
      createObjectStore(store) { assert.equal(store,'bundles'); initialized=true },
      close() { closes++ },
      transaction(store) {
        assert.equal(store,'bundles')
        const tx = { objectStore:() => ({
          getAllKeys:() => ({result:[...records.keys()]}),
          get:url => ({result:structuredClone(records.get(url))}),
          put:(bytes,url) => { assert.ok(bytes instanceof Uint8Array);records.set(url,structuredClone(bytes));return {result:url} },
          delete:url => { records.delete(url);return {result:undefined} }
        }) }
        queueMicrotask(() => tx.oncomplete())
        return tx
      }
    } }
    queueMicrotask(() => { if (!initialized) req.onupgradeneeded();req.onsuccess() })
    return req
  } }
  return {factory,records,opens,get closes() {return closes}}
}

test('HTTP fallback uses only the dedicated origin IDB; warm launch does not even call fetch', async () => {
  const db = idbFixture()
  const net = []
  const fallback = { ...options,cacheStorage:null,indexedDB:db.factory,
    async fetchImpl(url,options) { net.push([url,options.cache]);return fetchData(url) } }
  const run = async m => (await prepareUnityContent(m,fallback)).dispose()
  await run(manifest)
  assert.deepEqual(net,[shared,astc].map(f => [baseUrl+f.file,'force-cache']))
  net.length=0
  await run(manifest)
  assert.deepEqual(net,[])
  const change = bundle('Shared','images','changed on HTTP')
  await run({ ...manifest,astc:[change,astc],dxt:[change,dxt] })
  assert.deepEqual(net,[[baseUrl+change.file,'force-cache']])
  assert.equal(db.records.has(baseUrl+shared.file),false)
  db.records.set(baseUrl+astc.file,new Uint8Array(astc.bytes))
  net.length=0
  await run({ ...manifest,astc:[change,astc],dxt:[change,dxt] })
  assert.deepEqual(net,[[baseUrl+astc.file,'reload']])
  assert.equal(db.closes,4)
  assert.ok(db.opens.every(([name,version]) => name === 'open-mahjong-webgl-content-v1' && version === 1))
})

test('Cache Storage takes precedence even on quota errors, and IDB denial does not disable HTTP fallback',async () => {
  const {cacheStorage,cache} = cacheFixture()
  const idb = {open() { throw new Error('disabled') }}
  let idbAttempts=0
  const monitored = {open() { idbAttempts++;return idb.open() }}
  cache.put=async () => { throw Error('quota') }
  const launch=async storage => (await prepareUnityContent(manifest,{...options,cacheStorage:storage,indexedDB:monitored})).dispose()
  await launch(cacheStorage)
  assert.equal(idbAttempts,0)
  await launch({open() { throw Error('denied') }})
  assert.equal(idbAttempts,1)
})
