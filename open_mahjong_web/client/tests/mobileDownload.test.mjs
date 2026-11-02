import { test } from 'node:test'
import assert from 'node:assert/strict'
import { resolveMobileDownload, openMobileDownload } from '../src/utils/mobileDownload.js'

const release = (hash = 'a'.repeat(64), version = 31) => ({
  status: 'available', schemaVersion: 1, sha256: hash,
  versionName: `0.5.${version}.0`, versionCode: version, sizeBytes: 123,
  downloadPath: `/api/client-updates/android/apk/${hash}`,
})
const response = body => ({ ok: true, json: async () => body })

test('every click fetches fresh metadata and follows its hash, with no hard-coded release', async () => {
  const calls = []
  const values = [release(), release('b'.repeat(64), 32)]
  const fetchImpl = async (url, options) => {
    calls.push({ url, options })
    return response(values.shift())
  }
  const first = await resolveMobileDownload(fetchImpl)
  const second = await resolveMobileDownload(fetchImpl)
  assert.notEqual(first.path, second.path)
  assert.equal(second.filename, 'open_mahjong_unity-0.5.32.0-32.apk')
  assert.notEqual(calls[0].url, calls[1].url)
  for (const { url, options } of calls) {
    assert.match(url, /^\/api\/client-updates\/android\/latest\?request=/)
    assert.equal(options.cache, 'no-store')
    assert.equal(options.headers.Accept, 'application/json')
    assert.ok(options.signal instanceof AbortSignal)
  }
})

test('pending, invalid metadata and external/hash-mismatched download URLs fail closed', async () => {
  for (const value of [
    { status: 'pending' }, null, { ...release(), schemaVersion: 2 },
    { ...release(), sha256: 'old' }, { ...release(), versionCode: 0 },
    { ...release(), sizeBytes: -1 }, { ...release(), versionName: '\r\nunsafe' },
    { ...release(), downloadPath: 'https://example.com/old.apk' },
    { ...release(), downloadPath: '/android-game/open_mahjong_unity.apk' },
    { ...release(), downloadPath: `/api/client-updates/android/apk/${'c'.repeat(64)}` },
  ]) {
    await assert.rejects(resolveMobileDownload(async () => response(value)))
  }
  await assert.rejects(resolveMobileDownload(async () => ({ ok: false })), /获取安装包信息失败/)
  await assert.rejects(resolveMobileDownload(async () => { throw new DOMException('timeout', 'AbortError') }), /超时/)
})

test('download anchor uses the verified hash and versioned filename and is removed', async () => {
  const events = []
  const link = { click() { events.push('click') } }
  const documentRef = {
    createElement(tag) { assert.equal(tag, 'a'); return link },
    body: {
      appendChild(value) { assert.equal(value, link); events.push('append') },
      removeChild(value) { assert.equal(value, link); events.push('remove') },
    },
  }
  await openMobileDownload({ fetchImpl: async () => response(release()), documentRef })
  assert.equal(link.href, release().downloadPath)
  assert.equal(link.download, 'open_mahjong_unity-0.5.31.0-31.apk')
  assert.deepEqual(events, ['append', 'click', 'remove'])
})

test('failed metadata never triggers a fallback old-package download', async () => {
  let touched = false
  await assert.rejects(openMobileDownload({
    fetchImpl: async () => response({ status: 'pending' }),
    documentRef: { createElement() { touched = true } },
  }))
  assert.equal(touched, false)
})
