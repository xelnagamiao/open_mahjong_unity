import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'
import { createServer } from 'vite'
import vue from '@vitejs/plugin-vue'
import { createSSRApp, h } from 'vue'
import { renderToString } from '@vue/server-renderer'
import { parse, compileStyle } from '@vue/compiler-sfc'
import { parse as parseHtml } from '@vue/compiler-dom'
import { LIBRARY_RULES } from '../src/constants/libraryRules.js'
import { messages, textPatterns } from '../src/i18n/messages.js'
import { escapeHtml as e } from '../src/seo/mcr-render.js'

const client = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const read = name => readFileSync(resolve(client, name), 'utf8')

// Use the same dictionary as the original pages. No landing-page-specific prose.
export function translateOriginal(source, locale) {
  const raw = String(source)
  const normalized = raw.replace(/\s+/g, ' ').trim()
  let result = messages[locale]?.[raw] ?? messages[locale]?.[normalized]
  if (result != null) return result
  for (const pattern of textPatterns[locale] || []) {
    const match = normalized.match(pattern.match)
    if (match) return pattern.replace(...match.slice(1))
  }
  return raw
}

export function originalHomeIntroduction() {
  const source = read('src/views/Home.vue')
  const match = source.match(/class="platform-card-main"[\s\S]*?<p>([\s\S]*?)<\/p>/)
  if (!match || /<[^>]+>/.test(match[1])) throw new Error('Home introduction must remain plain original text')
  return match[1].replace(/\s+/g, ' ').trim()
}

export function originalRule(key) {
  key = key === 'taiwanese' ? 'taiwan' : key
  const rule = LIBRARY_RULES.find(item => item.key === key)
  if (!rule) throw new Error(`Unknown original library entry: ${key}`)
  return rule
}

function localizeHtml(html, locale) {
  const edits = []
  function walk(node, protectedText = false) {
    if (node.type === 1) protectedText ||= ['pre', 'code', 'script', 'style'].includes(node.tag) || node.props.some(p => p.type === 6 && p.name === 'data-no-translate')
    if (!protectedText && node.type === 2 && node.content.trim()) {
      const source = node.content.trim()
      const translated = translateOriginal(source, locale)
      if (translated !== source) edits.push({ start: node.loc.start.offset, end: node.loc.end.offset, text: e(node.content.match(/^\s*/)[0] + translated + node.content.match(/\s*$/)[0]) })
    }
    if (!protectedText && node.type === 1) for (const prop of node.props) {
      if (prop.type !== 6 || !prop.value || !['aria-label','title','alt','placeholder'].includes(prop.name)) continue
      const translated = translateOriginal(prop.value.content, locale)
      if (translated !== prop.value.content) edits.push({ start: prop.value.loc.start.offset, end: prop.value.loc.end.offset, text: `"${e(translated)}"` })
    }
    for (const child of node.children || []) walk(child, protectedText)
  }
  walk(parseHtml(html))
  for (const edit of edits.sort((a,b) => b.start-a.start)) html = html.slice(0,edit.start) + edit.text + html.slice(edit.end)
  // The static copy has the complete regulations below, so its existing button
  // links to that section instead of requiring a JavaScript-only dialog.
  html = html.replace(/<button([^>]*class="guide-text-button"[^>]*)>/g, '<a$1 href="#regulations">').replace(/(<a[^>]*class="guide-text-button"[^>]*>[^<]*)<\/button>/g, '$1</a>')
  return html.replace(/href="\/2d"/g, `href="/2d?lang=${locale}"`)
}

async function guideContent(locale) {
  // Middleware mode performs build-time SSR without starting a listening server.
  const server = await createServer({
    root: client, configFile: false, plugins: [vue()], logLevel: 'error',
    optimizeDeps: { noDiscovery: true, include: [] },
    resolve: { alias: { '@': resolve(client, 'src') } },
    server: { middlewareMode: true, hmr: false, watch: null },
  })
  try {
    const { default: Guide } = await server.ssrLoadModule('/src/views/UsageGuide.vue')
    const i18n = await server.ssrLoadModule('/src/i18n/index.js')
    i18n.locale.value = locale
    const app = createSSRApp(Guide)
    app.component('RouterLink', { props: ['to'], setup: (props, { slots, attrs }) => () => h('a', { ...attrs, href: props.to }, slots.default?.()) })
    app.component('ElDialog', { setup: () => () => null })
    app.component('ElButton', { setup: (_, { slots, attrs }) => () => h('button', attrs, slots.default?.()) })
    const html = localizeHtml(await renderToString(app), locale)
    const { descriptor } = parse(read('src/views/UsageGuide.vue'))
    const css = descriptor.styles.map(style => compileStyle({ source: style.content, filename: 'UsageGuide.vue', id: Guide.__scopeId, scoped: style.scoped }).code).join('\n')
    return { html, css: read('src/style.css') + '\n' + css }
  } finally {
    await server.close()
  }
}

/** All visible strings come from Home.vue, UsageGuide.vue or the library. */
export async function originalPageContent(page) {
  const t = source => e(translateOriginal(source, page.locale))
  if (page.source === 'guide') return guideContent(page.locale)
  if (page.source === 'home') return {
    html: `<section class="mcr-hero"><h1>${t('国标麻将')}</h1><p class="mcr-intro">${t(originalHomeIntroduction())}</p><div class="mcr-links"><a href="/game-unity">${t('进入平台')}</a><a class="mcr-button" href="/2d?lang=${page.locale}">${t('进入2D模式')}</a></div></section>`, css: '',
  }
  const resourceList = rule => `<div class="mcr-rule-cards">${rule.resources.map(doc => `<article><h3><a href="${e(doc.url)}">${t(doc.title)}</a></h3>${doc.desc ? `<p>${t(doc.desc)}</p>` : ''}</article>`).join('')}</div>`
  if (page.source === 'library') {
    const rule = originalRule(page.ruleKey)
    const platform = page.family === 'play-riichi'
      ? `<p>${t(originalHomeIntroduction())}</p><a class="mcr-button" href="/game-unity">${t('进入平台')}</a>` : ''
    return { html: `<section class="mcr-hero"><h1>${t(rule.label)}</h1><p class="mcr-intro">${t(rule.description)}</p>${platform}</section><section class="mcr-section"><h2>${t('规则书')}</h2>${resourceList(rule)}</section>`, css: '' }
  }
  const keys = ['guobiao','riichi','taiwanese','sichuan','changsha']
  const sections = keys.map(key => {
    const rule = originalRule(key)
    return `<section class="mcr-section"><h2>${t(rule.label)}</h2><p>${t(rule.description)}</p>${resourceList(rule)}</section>`
  }).join('')
  return { html: `<section class="mcr-hero"><h1>${t('规则书')}</h1><p class="mcr-intro">${t('查阅各类麻将规则的说明书、牌例或文档')}</p></section>${sections}`, css: '' }
}
