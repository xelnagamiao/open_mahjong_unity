/**
 * 构建后为公开 URL 生成独立 TDK；MCR 落地页输出完整静态正文，
 * 让不执行 JS 的搜索引擎（尤其百度）也能读到每页的 title/description。
 *
 * 运行时机：npm run build 时在 vite build 之后自动执行。
 */
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { SITE, PRERENDER_PATHS, seoEntryFor, noindexEntryFor } from '../src/seo.js'
import { mcrPageForPath } from '../src/seo/mcr-pages.js'
import { renderMcrDocument } from '../src/seo/mcr-render.js'
import { renderSitemap } from '../src/seo/sitemap.js'

const clientDir = dirname(dirname(fileURLToPath(import.meta.url)))
// An explicit output path allows isolated validation without touching live game packages.
const distDir = process.argv[2] ? resolve(clientDir, process.argv[2]) : join(clientDir, 'dist')
const indexPath = join(distDir, 'index.html')

let template
try {
  template = readFileSync(indexPath, 'utf-8')
} catch {
  console.error('[prerender-seo] 未找到 dist/index.html，请先执行 vite build')
  process.exit(1)
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/** 替换 head 中的某个标签；tag 为 null 时移除该标签；标签不存在时在 </head> 前插入。 */
function replaceTag(html, pattern, tag) {
  if (!pattern.test(html)) return tag ? html.replace('</head>', `    ${tag}\n  </head>`) : html
  return html.replace(pattern, tag == null ? '' : tag)
}

function renderPage(url) {
  const landing = mcrPageForPath(url)
  if (landing) {
    return renderMcrDocument(landing, readFileSync(join(clientDir, 'src/seo/mcr-landing.css'), 'utf-8'), SITE.domain)
  }
  const seo = seoEntryFor(url) || {}
  const noindex = noindexEntryFor(url)
  const title = seo.title || noindex?.title || SITE.name
  const description = noindex?.description ?? seo.description ?? ''
  const keywords = noindex ? '' : seo.keywords || ''
  const robots = noindex ? 'noindex,nofollow' : 'index,follow'
  const canonical = `${SITE.domain}${url}`

  let html = template
  html = replaceTag(html, /<title>[\s\S]*?<\/title>/, `<title>${escapeHtml(title)}</title>`)
  html = replaceTag(html, /<meta name="description"[^>]*\/>/, description
    ? `<meta name="description" content="${escapeHtml(description)}" />`
    : null)
  html = replaceTag(html, /<meta name="keywords"[^>]*\/>/, keywords
    ? `<meta name="keywords" content="${escapeHtml(keywords)}" />`
    : null)
  html = replaceTag(html, /<meta name="robots"[^>]*\/>/, `<meta name="robots" content="${robots}" />`)
  html = replaceTag(html, /<link rel="canonical"[^>]*\/>/, `<link rel="canonical" href="${canonical}" />`)
  html = replaceTag(html, /<meta property="og:title"[^>]*\/>/, `<meta property="og:title" content="${escapeHtml(title)}" />`)
  html = replaceTag(html, /<meta property="og:description"[^>]*\/>/, description
    ? `<meta property="og:description" content="${escapeHtml(description)}" />`
    : null)
  html = replaceTag(html, /<meta property="og:url"[^>]*\/>/, `<meta property="og:url" content="${canonical}" />`)
  return html
}

let count = 0
for (const url of PRERENDER_PATHS) {
  const html = renderPage(url)
  const outPath = url === '/' ? indexPath : join(distDir, url.slice(1), 'index.html')
  mkdirSync(dirname(outPath), { recursive: true })
  writeFileSync(outPath, html, 'utf-8')
  count += 1
}

writeFileSync(join(distDir, 'sitemap.xml'), renderSitemap(), 'utf-8')
console.log(`[prerender-seo] 已为 ${count} 个 URL 生成 SEO 页面及 sitemap（MCR 含完整正文）`)
