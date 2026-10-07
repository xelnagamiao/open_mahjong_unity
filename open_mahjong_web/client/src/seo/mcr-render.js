import { MCR_PAGES, mcrAlternates, mcrStructuredData } from './mcr-pages.js'

export function escapeHtml(value) {
  return String(value).replace(/&/g, '&amp;').replace(/</g, '&lt;')
    .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;')
}

export function jsonLdText(value) {
  return JSON.stringify(value).replace(/</g, '\\u003c')
}

/** Shared by the static document and the SPA route. All text is repository-owned. */
export function renderMcrContent(page) {
  const e = escapeHtml
  const cta = `<a class="mcr-button" href="${e(page.gamePath)}">${e(page.cta)} <span aria-hidden="true">↗</span></a>`
  const languages = MCR_PAGES.map((other) => `<a href="${e(other.path)}" lang="${other.locale}" hreflang="${other.locale}"${other.locale === page.locale ? ' aria-current="page"' : ''}>${e(other.label)}</a>`).join('')
  return `<div class="mcr-page" data-no-translate data-page-language="${page.locale}">
    <a class="mcr-skip" href="#mcr-main">${page.locale === 'fr' ? 'Aller au contenu' : 'Skip to content'}</a>
    <header class="mcr-header">
      <a class="mcr-brand" href="${e(page.path)}"><img src="/logo.svg" width="34" height="34" alt="" />Salasasa<span>MCR</span></a>
      <nav aria-label="${e(page.languageLabel)}">${languages}</nav>
    </header>
    <main id="mcr-main">
      <section class="mcr-hero">
        <p class="mcr-eyebrow">${e(page.eyebrow)}</p>
        <h1>${e(page.h1)}</h1>
        <p class="mcr-intro">${e(page.intro)}</p>
        ${cta}<p class="mcr-game-note">${e(page.gameNote)}</p>
      </section>
      <section class="mcr-section" aria-labelledby="mcr-features">
        <h2 id="mcr-features">${e(page.featuresTitle)}</h2>
        <div class="mcr-features">${page.features.map((feature, index) => `<article><span class="mcr-number" aria-hidden="true">0${index + 1}</span><h3>${e(feature.title)}</h3><p>${e(feature.text)}</p></article>`).join('')}</div>
      </section>
      <section class="mcr-section mcr-rules" aria-labelledby="mcr-rules-title">
        <h2 id="mcr-rules-title">${e(page.rulesTitle)}</h2><p>${e(page.rulesText)}</p><p>${e(page.rulesNote)}</p>
        <div class="mcr-links"><a href="/library/guobiao">${e(page.platformRules)}</a><a href="${e(page.communityUrl)}">${e(page.communityRules)}</a></div>
      </section>
      <section class="mcr-section" aria-labelledby="mcr-start-title">
        <h2 id="mcr-start-title">${e(page.startTitle)}</h2>
        <ol class="mcr-steps">${page.steps.map((step) => `<li>${e(step)}</li>`).join('')}</ol>
        ${cta}
      </section>
      <section class="mcr-section mcr-faq" aria-labelledby="mcr-faq-title">
        <h2 id="mcr-faq-title">${e(page.faqTitle)}</h2>
        ${page.faq.map((item) => `<article><h3>${e(item.question)}</h3><p>${e(item.answer)}</p></article>`).join('')}
      </section>
      <section class="mcr-section mcr-source" aria-labelledby="mcr-source-title">
        <h2 id="mcr-source-title">${e(page.sourceTitle)}</h2><p>${e(page.sourceText)}</p>
        <a href="https://github.com/xelnagamiao/open_mahjong_unity">${e(page.sourceLink)} ↗</a>
      </section>
    </main>
    <footer class="mcr-footer"><span>Salasasa · open_mahjong_unity</span><a href="/">${e(page.mainSite)}</a></footer>
  </div>`
}

/** A complete HTML document: no JavaScript, game bundle or network API needed. */
export function renderMcrDocument(page, css, domain) {
  const e = escapeHtml
  const url = domain + page.path
  const image = `${domain}/seo/mcr-${page.locale}.png`
  return `<!DOCTYPE html>
<html lang="${page.locale}">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>${e(page.title)}</title>
  <meta name="description" content="${e(page.description)}" />
  <meta name="robots" content="index,follow" />
  <link rel="canonical" href="${url}" />
  ${mcrAlternates(domain).map((alt) => `<link rel="alternate" hreflang="${alt.language}" href="${alt.href}" />`).join('\n  ')}
  <link rel="icon" type="image/svg+xml" href="/logo.svg" />
  <meta property="og:site_name" content="Salasasa" />
  <meta property="og:type" content="website" />
  <meta property="og:title" content="${e(page.title)}" />
  <meta property="og:description" content="${e(page.description)}" />
  <meta property="og:url" content="${url}" />
  <meta property="og:locale" content="${page.ogLocale}" />
  ${MCR_PAGES.filter((other) => other.locale !== page.locale).map((other) => `<meta property="og:locale:alternate" content="${other.ogLocale}" />`).join('\n  ')}
  <meta property="og:image" content="${image}" />
  <meta property="og:image:width" content="1200" />
  <meta property="og:image:height" content="630" />
  <meta property="og:image:alt" content="${e(page.imageAlt)}" />
  <meta name="twitter:card" content="summary_large_image" />
  <meta name="twitter:title" content="${e(page.title)}" />
  <meta name="twitter:description" content="${e(page.description)}" />
  <meta name="twitter:image" content="${image}" />
  <meta name="twitter:image:alt" content="${e(page.imageAlt)}" />
  <script type="application/ld+json">${jsonLdText(mcrStructuredData(page, domain))}</script>
  <style>${css}</style>
</head>
<body>${renderMcrContent(page)}</body>
</html>
`
}
