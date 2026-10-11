import { SITE } from '../seo.js'
import { staticPageForPath, languageVersions, pageAlternates, pageStructuredData } from './pages.js'
import { localizedRouteCopy } from './site-copy.js'
import { jsonLdText } from './mcr-render.js'

function setMeta(attribute, name, content) {
  let element = document.head.querySelector(`meta[${attribute}="${name}"]`)
  if (!content) { element?.remove(); return }
  if (!element) {
    element = document.createElement('meta')
    element.setAttribute(attribute, name)
    document.head.appendChild(element)
  }
  element.setAttribute('content', content)
}

/** Keep social metadata in sync during SPA navigation, including cleanup on exit. */
export function applyRouteHead(route, locale, translate) {
  const page = staticPageForPath(route.path)
  const copy = localizedRouteCopy(route.path, locale)
  const title = page?.title || copy?.title || (route.path.startsWith('/admin') ? route.meta.title : translate(route.meta.title || SITE.name))
  const description = page?.description || copy?.description || translate(route.meta.description || '')
  const canonicalUrl = SITE.domain + (page?.path || route.path)
  document.title = title
  document.documentElement.lang = page?.locale || locale
  setMeta('name', 'description', description)
  setMeta('name', 'keywords', page ? null : route.meta.keywords)
  setMeta('name', 'robots', route.meta.noindex ? 'noindex,nofollow' : 'index,follow')

  let canonical = document.head.querySelector('link[rel="canonical"]')
  if (!canonical) {
    canonical = document.createElement('link')
    canonical.setAttribute('rel', 'canonical')
    document.head.appendChild(canonical)
  }
  canonical.setAttribute('href', canonicalUrl)
  setMeta('property', 'og:site_name', SITE.name)
  setMeta('property', 'og:type', 'website')
  setMeta('property', 'og:title', title)
  setMeta('property', 'og:description', description)
  setMeta('property', 'og:url', canonicalUrl)
  const ogLocales = { en: 'en_US', fr: 'fr_FR', ja: 'ja_JP', 'zh-CN': 'zh_CN', 'zh-TW': 'zh_TW', 'zh-HK': 'zh_HK' }
  setMeta('property', 'og:locale', page?.ogLocale || ogLocales[locale] || 'en_US')

  document.head.querySelectorAll('[data-mcr-seo]').forEach((element) => element.remove())
  if (page) {
    for (const alternate of pageAlternates(page, SITE.domain)) {
      const link = document.createElement('link')
      link.setAttribute('rel', 'alternate')
      link.setAttribute('hreflang', alternate.language)
      link.setAttribute('href', alternate.href)
      link.setAttribute('data-mcr-seo', 'alternate')
      document.head.appendChild(link)
    }
    for (const other of languageVersions(page).filter((entry) => entry.locale !== page.locale)) {
      const meta = document.createElement('meta')
      meta.setAttribute('property', 'og:locale:alternate')
      meta.setAttribute('content', other.ogLocale)
      meta.setAttribute('data-mcr-seo', 'og-alternate')
      document.head.appendChild(meta)
    }
    const script = document.createElement('script')
    script.type = 'application/ld+json'
    script.setAttribute('data-mcr-seo', 'structured-data')
    script.textContent = jsonLdText(pageStructuredData(page, SITE.domain))
    document.head.appendChild(script)
  }

  const image = page?.imagePath ? SITE.domain + page.imagePath : null
  setMeta('property', 'og:image', image)
  setMeta('property', 'og:image:width', image ? '1200' : null)
  setMeta('property', 'og:image:height', image ? '630' : null)
  setMeta('property', 'og:image:alt', page?.imageAlt)
  setMeta('name', 'twitter:card', page ? (image ? 'summary_large_image' : 'summary') : null)
  setMeta('name', 'twitter:title', page ? title : null)
  setMeta('name', 'twitter:description', page ? description : null)
  setMeta('name', 'twitter:image', image)
  setMeta('name', 'twitter:image:alt', page?.imageAlt)
}
