import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync, writeFileSync, mkdtempSync, existsSync, mkdirSync } from 'node:fs'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import { execFileSync } from 'node:child_process'
import { createServer } from 'node:http'
import { once } from 'node:events'
import { mcrSeoPlugin } from '../scripts/vite-mcr-seo.mjs'
import { MCR_PAGES, mcrPageForPath } from '../src/seo/mcr-pages.js'
import { STATIC_PAGES, staticPageForPath, pageAlternates } from '../src/seo/pages.js'
import { escapeHtml } from '../src/seo/mcr-render.js'
import { requestedGameLocale } from '../src/i18n/locale-utils.js'

import { originalHomeIntroduction, originalRule, translateOriginal } from '../scripts/original-seo-content.mjs'
import { parse as parseHtml } from '@vue/compiler-dom'

const client = resolve(dirname(fileURLToPath(import.meta.url)), '..')
// Keep local validation output in the repository's ignored workspace.
const workspace = resolve(client, '../../.om_workspace')
mkdirSync(workspace, { recursive: true })
const output = mkdtempSync(resolve(workspace, 'mcr-seo-test-'))
writeFileSync(resolve(output, 'index.html'), readFileSync(resolve(client, 'index.html'), 'utf8'))
// Verify the content-only documents independently of a developer's analytics token.
execFileSync(process.execPath, ['scripts/prerender-seo.mjs', output], {
  cwd: client,
  env: { ...process.env, VITE_CLOUDFLARE_ANALYTICS_TOKEN: '' },
})

for (const page of MCR_PAGES) {
  test(page.locale + ': original project introduction and game link remain crawlable without JS', () => {
    const html = readFileSync(resolve(output, page.path.slice(1), 'index.html'), 'utf8')
    assert.ok(html.includes(escapeHtml(translateOriginal(originalHomeIntroduction(), page.locale))))
    assert.ok(html.includes('href="/2d?lang=' + page.locale + '"'))
    assert.ok(html.includes('href="/game-unity"'))
    assert.doesNotMatch(html.match(/<body>[\s\S]*?<\/body>/)[0], /mcr-faq|mcr-features|No download required|Your first game/)
    const data = JSON.parse(html.match(/<script type="application\/ld\+json">(.*?)<\/script>/s)[1])
    assert.equal(data.inLanguage, page.locale)
    assert.equal(data.url, 'https://salasasa.cn' + page.path)
  })
}

test('all independent pages use source references instead of separately authored prose', () => {
  for (const page of STATIC_PAGES) {
    assert.ok(['home','library','guide','rulebook'].includes(page.source))
    for (const key of ['intro','faq','features','sections','steps','gameNote']) assert.equal(page[key], undefined)
  }
})

test('sitemap lists actual translations reciprocally and excludes drafts and private pages', () => {
  const xml = readFileSync(resolve(output, 'sitemap.xml'), 'utf8')
  for (const page of STATIC_PAGES) {
    assert.ok(xml.includes(`<loc>https://salasasa.cn${page.path}</loc>`))
    const block = xml.split('<url>').find((item) => item.includes(`<loc>https://salasasa.cn${page.path}</loc>`))
    const alternates = pageAlternates(page, 'https://salasasa.cn')
    assert.equal((block.match(/<xhtml:link/g) || []).length, alternates.length)
    for (const alternate of alternates) {
      assert.ok(block.includes(`hreflang="${alternate.language}" href="${alternate.href}"`))
      const target = staticPageForPath(new URL(alternate.href).pathname)
      assert.equal(target.family, page.family)
      assert.ok(pageAlternates(target, 'https://salasasa.cn').some((alt) => alt.href === `https://salasasa.cn${page.path}`))
    }
  }
  for (const path of ['/de/mcr', '/nl/mcr', '/es/mcr', '/it/mcr', '/login', '/register', '/account', '/2d/game']) {
    assert.ok(!xml.includes(`<loc>https://salasasa.cn${path}</loc>`))
  }
  assert.doesNotMatch(xml, /<lastmod>/)
  assert.ok(xml.includes('<loc>https://salasasa.cn/library/zhongyong</loc>'))
  assert.ok(!existsSync(resolve(output, 'de/mcr/index.html')))
})

for (const page of STATIC_PAGES) {
  test(`${page.path}: complete, language-stable, connected static content`, () => {
    const html = readFileSync(resolve(output, page.path.slice(1), 'index.html'), 'utf8')
    assert.ok(html.includes(`<html lang="${page.locale}">`))
    assert.ok(html.includes(`<title>${escapeHtml(page.title)}</title>`))
    assert.equal((html.match(/<h1(?:\s|>)/g) || []).length, 1)
    assert.equal((html.match(/rel="canonical"/g) || []).length, 1)
    assert.ok(html.includes(`rel="canonical" href="https://salasasa.cn${page.path}"`))
    assert.doesNotMatch(html, /<script(?! type="application\/ld\+json")|<div id="app">|localStorage|navigator\.language/)
    const main = html.match(/<main[^>]*>([\s\S]*?)<\/main>/)[1]
    if (['en', 'fr'].includes(page.locale)) {
      const missing = []
      const walk = (node, protectedText = false) => {
        if (node.type === 1) protectedText ||= ['pre','code'].includes(node.tag) || node.props.some(p => p.type === 6 && p.name === 'data-no-translate')
        if (node.type === 2 && !protectedText && /[\u3400-\u9fff]/u.test(node.content)) missing.push(node.content)
        for (const child of node.children || []) walk(child, protectedText)
      }
      walk(parseHtml(main))
      assert.deepEqual(missing, [], 'original contributor names stay unchanged; translated prose must not fall back to Chinese')
    }
    const ids = new Set([...html.matchAll(/\bid="([^"]+)"/g)].map((match) => match[1]))
    for (const [, href] of html.matchAll(/\bhref="([^"]+)"/g)) {
      if (href.startsWith('#')) assert.ok(ids.has(href.slice(1)), `missing anchor ${href}`)
      if (/^\/(en|fr|zh)\//.test(href)) assert.ok(staticPageForPath(href), `unpublished internal link ${href}`)
      if (/^\/rulebooks\//.test(href)) assert.ok(existsSync(resolve(client, 'public', href.slice(1))), `missing reference ${href}`)
    }
    for (const alt of pageAlternates(page, 'https://salasasa.cn')) assert.ok(html.includes(`hreflang="${alt.language}" href="${alt.href}"`))
  })
}

test('rules pages contain the existing library descriptions and rulebooks', () => {
  for (const page of STATIC_PAGES.filter(p => p.source === 'library')) {
    const rule = originalRule(page.ruleKey)
    const html = readFileSync(resolve(output, page.path.slice(1), 'index.html'), 'utf8')
    assert.ok(html.includes(escapeHtml(translateOriginal(rule.description, page.locale))))
    for (const doc of rule.resources) assert.ok(html.includes('href="' + escapeHtml(doc.url) + '"'))
  }
})

test('localized guides preserve all 15 original sections, credits and account regulations', () => {
  for (const lang of ['en','fr','zh']) {
    const html = readFileSync(resolve(output, lang + '/guide/index.html'), 'utf8')
    const ids = [...html.matchAll(/<section id="([^"]+)"/g)].map(match => match[1])
    assert.deepEqual(ids, ['intro','features','start','meld-protect','tactical-call','seed','guess-fan','events','addrule','rank-credit','contribute','sponsor','thanks','links','regulations'])
    for (const original of ['open_mahjong_unity','Xelnaga','FFMJ','hashlib','Salasasa Studio']) assert.ok(html.includes(original))
  }
})

test('SEO generation preserves the existing SPA body without adding visible content', () => {
  const template = readFileSync(resolve(client, 'index.html'), 'utf8')
  const originalBody = template.match(/<body>[\s\S]*?<\/body>/)[0]
  for (const path of ['index.html', '2d/index.html', 'calc/chinese/index.html', 'guide/index.html']) {
    const html = readFileSync(resolve(output, path), 'utf8')
    assert.equal(html.match(/<body>[\s\S]*?<\/body>/)[0], originalBody)
    assert.ok(!html.includes('data-home-intro'))
  }
})

test('existing home, tools and noindex shells retain their route metadata', () => {
  const home = readFileSync(resolve(output, 'index.html'), 'utf8')
  assert.ok(home.includes('Salasasa国标麻将对战平台-萨拉飒飒'))
  const calc = readFileSync(resolve(output, 'calc/chinese/index.html'), 'utf8')
  assert.ok(calc.includes('国标麻将算番计算器'))
  const login = readFileSync(resolve(output, 'login/index.html'), 'utf8')
  assert.ok(login.includes('noindex,nofollow'))
  assert.ok(!login.includes('hreflang='))
})

test('game entry accepts explicit supported language only on 2D routes', () => {
  assert.equal(requestedGameLocale('/2d', '?lang=en'), 'en')
  assert.equal(requestedGameLocale('/2d/game', '?lang=en-US'), 'en')
  assert.equal(requestedGameLocale('/2d', '?lang=fr'), 'fr')
  assert.equal(requestedGameLocale('/2d', '?lang=fr-FR'), 'fr')
  assert.equal(requestedGameLocale('/2d', '?lang=invalid'), null)
  assert.equal(requestedGameLocale('/login', '?lang=en'), null)
  assert.equal(requestedGameLocale('/2different', '?lang=en'), null)
  assert.equal(mcrPageForPath('/fr/mcr/').locale, 'fr')
  assert.equal(mcrPageForPath('/de/mcr'), null)
})

test('dev and production preview serve real documents, redirects and 404s over HTTP', async (t) => {
  for (const preview of [false, true]) {
    let middleware
    const server = { config: { root: client, build: { outDir: output } }, middlewares: { use(handler) { middleware = handler } } }
    const plugin = mcrSeoPlugin()
    if (preview) plugin.configurePreviewServer(server)
    else plugin.configureServer(server)
    const http = createServer((req, res) => middleware(req, res, () => { res.statusCode = 204; res.end() }))
    http.listen(0, '127.0.0.1')
    await once(http, 'listening')
    t.after(() => http.close())
    const base = `http://127.0.0.1:${http.address().port}`
    for (const page of STATIC_PAGES) {
      const response = await fetch(base + page.path, { headers: { 'Accept-Language': page.locale === 'fr' ? 'zh-CN' : 'fr' } })
      assert.equal(response.status, 200)
      assert.match(response.headers.get('content-type'), /^text\/html/)
      assert.ok((await response.text()).includes(`<html lang="${page.locale}">`), 'URL language must override request preferences')
      for (const suffix of ['/', '/index.html']) {
        const redirect = await fetch(base + page.path + suffix + '?source=test', { redirect: 'manual' })
        assert.equal(redirect.status, 301)
        assert.equal(redirect.headers.get('location'), page.path + '?source=test')
      }
    }
    for (const path of ['/de/mcr', '/fr/riichi', '/en/missing', '/en/mcr/unknown', '/fr/mahjong/taiwanese']) {
      const response = await fetch(base + path)
      assert.equal(response.status, 404, `${preview ? 'preview' : 'dev'}: ${path}`)
      assert.doesNotMatch(await response.text(), /id="app"/)
    }
    const head = await fetch(base + '/en/guide', { method: 'HEAD' })
    assert.equal(head.status, 200)
    assert.equal(await head.text(), '')
    assert.equal((await fetch(base + '/2d?lang=fr')).status, 204, 'game routes pass to the app')
    assert.equal((await fetch(base + '/api/test')).status, 204, 'API routes pass through')
    assert.equal((await fetch(base + '/en/mcr', { method: 'POST' })).status, 204)
    http.closeAllConnections()
    http.close()
  }
})
