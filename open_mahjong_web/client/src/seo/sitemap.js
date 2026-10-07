import { SITE, PRERENDER_PATHS, noindexEntryFor } from '../seo.js'
import { mcrPageForPath, mcrAlternates } from './mcr-pages.js'
import { escapeHtml } from './mcr-render.js'

/** No guessed lastmod dates; public URLs and HTML share one source of truth. */
export function renderSitemap() {
  const urls = [...new Set(PRERENDER_PATHS)].filter((path) => !noindexEntryFor(path))
  return `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">
${urls.map((path) => {
    const alternates = mcrPageForPath(path)
      ? mcrAlternates(SITE.domain).map((alt) => `    <xhtml:link rel="alternate" hreflang="${alt.language}" href="${escapeHtml(alt.href)}" />`).join('\n')
      : ''
    return `  <url>\n    <loc>${escapeHtml(SITE.domain + path)}</loc>${alternates ? '\n' + alternates : ''}\n  </url>`
  }).join('\n')}
</urlset>
`
}
