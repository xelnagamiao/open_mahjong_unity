import { languageVersions, pageAlternates, pageStructuredData } from './pages.js'
import { escapeHtml as e, jsonLdText } from './mcr-render.js'
import { messages } from '../i18n/messages.js'

export function renderPageContent(page, content) {
  if (!content?.html) throw new Error('Original page content is required: ' + page.path)
  const t = source => e(messages[page.locale]?.[source] || source)
  const prefix = page.path.split('/')[1]
  const languages = languageVersions(page).map(other => `<a href="${e(other.path)}" lang="${other.locale}" hreflang="${other.locale}"${other.path === page.path ? ' aria-current="page"' : ''}>${e(other.label)}</a>`).join('')
  return `<div id="top" class="mcr-page content-page" data-no-translate data-page-language="${page.locale}">
    <header class="mcr-header"><a class="mcr-brand" href="/"><img src="/logo.svg" width="34" height="34" alt="" />Salasasa</a><nav aria-label="${t('语言')}">${languages}</nav></header>
    <main id="content-main">
      <nav class="mcr-context-nav" aria-label="Salasasa"><div class="mcr-links"><a href="/">${t('首页')}</a><a href="/${prefix}/mahjong-rules">${t('规则书')}</a><a href="/${prefix}/guide">${t('使用说明')}</a></div></nav>
      ${content.html}
    </main>
    <footer class="mcr-footer"><span>Salasasa · open_mahjong_unity</span><a href="/">${t('首页')}</a></footer>
  </div>`
}

export function renderPageDocument(page, css, domain, content) {
  const url = domain + page.path
  const image = page.imagePath ? domain + page.imagePath : null
  return `<!DOCTYPE html>
<html lang="${page.locale}">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>${e(page.title)}</title>
  <meta name="description" content="${e(page.description)}" />
  <meta name="robots" content="index,follow" />
  <link rel="canonical" href="${url}" />
  ${pageAlternates(page, domain).map(alt => `<link rel="alternate" hreflang="${alt.language}" href="${alt.href}" />`).join('\n  ')}
  <link rel="icon" type="image/svg+xml" href="/logo.svg" />
  <meta property="og:site_name" content="Salasasa" />
  <meta property="og:type" content="website" />
  <meta property="og:title" content="${e(page.title)}" />
  <meta property="og:description" content="${e(page.description)}" />
  <meta property="og:url" content="${url}" />
  <meta property="og:locale" content="${page.ogLocale}" />
  ${languageVersions(page).filter(other => other.locale !== page.locale).map(other => `<meta property="og:locale:alternate" content="${other.ogLocale}" />`).join('\n  ')}
  ${image ? `<meta property="og:image" content="${e(image)}" /><meta property="og:image:alt" content="${e(page.imageAlt)}" /><meta name="twitter:image" content="${e(image)}" />` : ''}
  <meta name="twitter:card" content="${image ? 'summary_large_image' : 'summary'}" />
  <meta name="twitter:title" content="${e(page.title)}" />
  <meta name="twitter:description" content="${e(page.description)}" />
  <script type="application/ld+json">${jsonLdText(pageStructuredData(page, domain))}</script>
  <style>${css}\n${content.css || ''}</style>
</head>
<body>${renderPageContent(page, content)}</body>
</html>
`
}
