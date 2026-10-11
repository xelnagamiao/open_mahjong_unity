import test from 'node:test'
import assert from 'node:assert/strict'
import vm from 'node:vm'
import { cloudflareAnalyticsMarkup } from '../scripts/cloudflare-analytics.mjs'

const token = '0'.repeat(32) // Isolated test fixture; never used to make a network request.
test('empty configuration emits no beacon; rejects malformed tokens', () => {
  assert.equal(cloudflareAnalyticsMarkup(''), '')
  assert.equal(cloudflareAnalyticsMarkup(undefined), '')
  assert.throws(() => cloudflareAnalyticsMarkup('not-a-site-token'))
  assert.throws(() => cloudflareAnalyticsMarkup('</script><script>alert(1)</script>'))
})
test('one deferred beacon on production hosts, none on local previews or direct admin entries', () => {
  const markup = cloudflareAnalyticsMarkup(token)
  const js = markup.slice(markup.indexOf('>') + 1, markup.lastIndexOf('</script>'))
  for (const [hostname, pathname, expected] of [
    ['salasasa.cn', '/', 1], ['salasasa.cn', '/fr/mcr', 1], ['www.salasasa.cn', '/2d', 1],
    ['localhost', '/', 0], ['127.0.0.1', '/en/mcr', 0], ['preview.example.com', '/', 0],
    ['evil-salasasa.cn', '/', 0], ['salasasa.cn', '/admin/users', 0], ['salasasa.cn', '/event-admin', 0],
  ]) {
    const scripts = []
    const document = { querySelector: () => scripts[0] || null, createElement: () => ({ setAttribute(k, v) { this[k] = v } }), body: { appendChild: s => scripts.push(s) } }
    const context = vm.createContext({ document, location: { hostname, pathname } })
    vm.runInContext(js, context)
    vm.runInContext(js, context)
    assert.equal(scripts.length, expected, hostname + pathname)
    if (expected) {
      assert.equal(scripts[0].src, 'https://static.cloudflareinsights.com/beacon.min.js')
      assert.equal(scripts[0].defer, true)
      assert.equal(JSON.parse(scripts[0]['data-cf-beacon']).token, token)
    }
  }
})
