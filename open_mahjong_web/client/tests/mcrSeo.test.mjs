import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync, writeFileSync, mkdtempSync, existsSync, mkdirSync } from 'node:fs'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import { execFileSync } from 'node:child_process'
import { MCR_PAGES, mcrPageForPath } from '../src/seo/mcr-pages.js'
import { SUPPORTED_LOCALES, requestedGameLocale } from '../src/i18n/locale-utils.js'

const client = resolve(dirname(fileURLToPath(import.meta.url)), '..')
// Keep local validation output in the repository's ignored workspace.
const workspace = resolve(client, '../../.om_workspace')
mkdirSync(workspace, { recursive: true })
const output = mkdtempSync(resolve(workspace, 'mcr-seo-test-'))
writeFileSync(resolve(output, 'index.html'), readFileSync(resolve(client, 'index.html'), 'utf8'))
execFileSync(process.execPath, ['scripts/prerender-seo.mjs', output], { cwd: client })

for (const page of MCR_PAGES) {
  test(`${page.locale}: static HTTP document has content, metadata and a usable CTA without JS`, () => {
    const html = readFileSync(resolve(output, page.path.slice(1), 'index.html'), 'utf8')
    const canonical = `https://salasasa.cn${page.path}`
    assert.ok(html.includes(`<html lang="${page.locale}">`))
    assert.equal((html.match(/<h1>/g) || []).length, 1)
    assert.ok(html.includes(page.h1))
    assert.ok(html.includes(page.description))
    assert.equal((html.match(/rel="canonical"/g) || []).length, 1)
    assert.ok(html.includes(`rel="canonical" href="${canonical}"`))
    assert.ok(html.includes(`property="og:url" content="${canonical}"`))
    assert.ok(html.includes(`property="og:locale" content="${page.ogLocale}"`))
    assert.ok(html.includes(`property="og:image" content="https://salasasa.cn/seo/mcr-${page.locale}.png"`))
    const gameLocale = page.locale === 'fr' ? 'fr' : 'en'
    assert.equal(page.gameLocale, gameLocale)
    assert.equal(page.gamePath, `/2d?lang=${gameLocale}`)
    assert.equal(html.split(`href="/2d?lang=${gameLocale}"`).length - 1, 2)
    if (page.locale === 'fr') assert.ok(!html.includes('href="/2d?lang=en"'))
    assert.doesNotMatch(html, /type="module"|<div id="app"><\/div>|noscript/)
    for (const other of MCR_PAGES) {
      assert.ok(html.includes(`hreflang="${other.locale}" href="https://salasasa.cn${other.path}"`))
    }
    assert.ok(html.includes('hreflang="x-default" href="https://salasasa.cn/en/mcr"'))
    const data = JSON.parse(html.match(/<script type="application\/ld\+json">(.*?)<\/script>/s)[1])
    assert.equal(data['@graph'][0].inLanguage, page.locale)
    assert.equal(data['@graph'][0].url, canonical)
    assert.equal(data['@graph'][1].offers.price, '0')
    assert.deepEqual(data['@graph'][1].inLanguage, SUPPORTED_LOCALES)
    assert.ok(data['@graph'][1].inLanguage.includes('fr'))
    assert.ok(!('aggregateRating' in data['@graph'][1]))
    assert.ok(!('review' in data['@graph'][1]))
  })
}

test('MCR descriptions, instructions and FAQ reflect French game support', () => {
  const french = mcrPageForPath('/fr/mcr')
  assert.match(french.description, /jeu en français/)
  assert.match(french.gameNote, /s'ouvre en français/)
  assert.match(french.steps[0], /en français/)
  const frenchAnswer = french.faq.find((item) => item.question.includes('disponible en français')).answer
  assert.match(frenchAnswer, /^Oui\./)
  assert.match(frenchAnswer, /en français/)
  assert.doesNotMatch(french.description + french.gameNote + french.steps[0], /en anglais/)
  assert.doesNotMatch(frenchAnswer, /Le bouton ouvre le jeu en anglais/)
  const englishAnswer = mcrPageForPath('/en/mcr').faq.find((item) => item.question.includes('languages')).answer
  assert.match(englishAnswer, /French/)
})

test('sitemap lists both languages reciprocally, excludes drafts and private pages', () => {
  const xml = readFileSync(resolve(output, 'sitemap.xml'), 'utf8')
  for (const page of MCR_PAGES) {
    assert.ok(xml.includes(`<loc>https://salasasa.cn${page.path}</loc>`))
    assert.equal((xml.match(new RegExp(`hreflang="${page.locale}"`, 'g')) || []).length, 2)
  }
  for (const path of ['/de/mcr', '/nl/mcr', '/es/mcr', '/it/mcr', '/login', '/register', '/account', '/2d/game']) {
    assert.ok(!xml.includes(`<loc>https://salasasa.cn${path}</loc>`))
  }
  assert.doesNotMatch(xml, /<lastmod>/)
  assert.ok(xml.includes('<loc>https://salasasa.cn/library/zhongyong</loc>'))
  assert.ok(!existsSync(resolve(output, 'de/mcr/index.html')))
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
