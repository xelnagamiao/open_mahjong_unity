import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import * as Vue from 'vue'
import { compile } from '@vue/compiler-dom'
import { parse } from '@vue/compiler-sfc'
import { renderToString } from '@vue/server-renderer'
import { createMemoryHistory, createRouter } from 'vue-router'
import { fetchIosRelease, mobileDownloadRedirect, normalizeIosRelease } from '../src/utils/iosDownload.js'

const origin = 'https://salasasa.cn'
const downloadPath = `/api/client-updates/ios/ipa/${'a'.repeat(64)}`

test('generic mobile download follows iPhone, iPad and iPod devices', () => {
  for (const model of ['iPhone', 'iPad', 'iPod']) {
    const result = mobileDownloadRedirect({ path: '/mobile-download', query: { lang: 'en' }, hash: '#install' }, { userAgent: `Mozilla/5.0 (${model}; CPU OS 17_0 like Mac OS X)` })
    assert.deepEqual(result, { path: '/ios-download', query: { lang: 'en' }, hash: '#install', replace: true })
  }
})

test('iPad desktop identity routes to iOS but regular desktop and Android stay on APK', () => {
  const route = { path: '/mobile-download' }
  assert.equal(mobileDownloadRedirect(route, { userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X)', platform: 'MacIntel', maxTouchPoints: 5 }).path, '/ios-download')
  for (const device of [
    { platform: 'MacIntel', maxTouchPoints: 0 },
    { userAgent: 'Mozilla/5.0 (Windows NT 10.0)', platform: 'Win32', maxTouchPoints: 10 },
    { userAgent: 'Mozilla/5.0 (Linux; Android 14)', maxTouchPoints: 5 },
    undefined,
  ]) assert.equal(mobileDownloadRedirect(route, device), null)
})

test('explicit Android selection is respected on an iOS device', () => {
  const device = { userAgent: 'iPhone' }
  for (const platform of ['android', 'Android', ['ios', 'android']]) {
    assert.equal(mobileDownloadRedirect({ path: '/mobile-download', query: { platform } }, device), null)
  }
  assert.equal(mobileDownloadRedirect({ path: '/ios-download' }, device), null)
})

test('returning from explicit APK to generic download redirects on query-only navigation', async () => {
  const router = createRouter({ history: createMemoryHistory(), routes: [
    { path: '/mobile-download', component: {} }, { path: '/ios-download', component: {} },
  ] })
  router.beforeEach(to => mobileDownloadRedirect(to, { userAgent: 'iPhone' }) || true)
  await router.push('/mobile-download?platform=android')
  assert.equal(router.currentRoute.value.path, '/mobile-download')
  await router.push('/mobile-download')
  assert.equal(router.currentRoute.value.path, '/ios-download')
})

test('pending release never exposes a stale installation link', () => {
  assert.equal(normalizeIosRelease({ status: 'pending', installUrl: '/old.ipa' }, origin).installUrl, '')
})

test('available release resolves relative IPA and preserves delivery channel', () => {
  const release = normalizeIosRelease({ status: 'available', channel: 'sideload', downloadPath, versionName: '1.0.0', buildNumber: 2, releaseVersion: 34 }, origin)
  assert.equal(release.installUrl, `${origin}${downloadPath}`)
  assert.equal(release.versionName, '1.0.0')
  assert.equal(release.buildNumber, 2)
  assert.equal(release.releaseVersion, '34')
  const flight = normalizeIosRelease({ status: 'available', channel: 'testflight', installUrl: 'https://testflight.apple.com/join/example' }, origin)
  assert.equal(flight.channel, 'testflight')
})

test('download page displays the game release and only adds a distinct store build number', async () => {
  const { descriptor, errors } = parse(fs.readFileSync(new URL('../src/views/IosDownload.vue', import.meta.url), 'utf8'))
  assert.deepEqual(errors, [])
  const render = new Function('Vue', compile(descriptor.template.content, { mode: 'function', prefixIdentifiers: true }).code)(Vue)
  for (const buildNumber of [34, 101]) {
    const release = normalizeIosRelease({ status: 'available', channel: 'sideload', downloadPath,
      versionName: '0.5.89', releaseVersion: 34, buildNumber }, origin)
    const app = Vue.createSSRApp({ render, setup: () => ({ release, loading: false, error: '',
      channelHint: '', installLabel: '下载IPA测试包', loadRelease: () => {} }) })
    for (const name of ['el-button', 'router-link'])
      app.component(name, { setup: (_, { slots }) => () => Vue.h('span', slots.default?.()) })
    app.config.warnHandler = message => { throw new Error(message) }
    const html = await renderToString(app)
    assert.match(html, /发行版本：34/)
    assert.match(html, /应用版本：0\.5\.89/)
    if (buildNumber === 34) assert.doesNotMatch(html, /构建号：/)
    else assert.match(html, /构建号：101/)
  }
})

test('unavailable or unsafe installation metadata cannot become a download', () => {
  for (const installUrl of ['', 'javascript:alert(1)', 'data:text/plain,bad', 'http://example.com/a.ipa', 'https://user:password@example.com/a.ipa']) {
    assert.throws(() => normalizeIosRelease({ status: 'available', channel: 'testflight', installUrl }, origin))
  }
  assert.throws(() => normalizeIosRelease({ status: 'available', channel: 'sideload', downloadPath: '/old.ipa' }, origin))
  assert.throws(() => normalizeIosRelease({ status: 'error' }, origin))
})

test('failed version request reports an error instead of an available build', async () => {
  await assert.rejects(fetchIosRelease({ origin, fetchImpl: async () => ({ ok: false }) }))
})

test('fetches current metadata without browser caching', async () => {
  let request
  const release = await fetchIosRelease({ origin, fetchImpl: async (...args) => {
    request = args
    return { ok: true, json: async () => ({ status: 'pending' }) }
  } })
  assert.equal(request[0], '/api/client-updates/ios/version')
  assert.equal(request[1].cache, 'no-store')
  assert.equal(release.status, 'pending')
})
