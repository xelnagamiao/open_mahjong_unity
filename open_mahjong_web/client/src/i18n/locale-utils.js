export const SUPPORTED_LOCALES = ['zh-CN', 'zh-TW', 'zh-HK', 'en', 'ja', 'fr', 'de', 'ru', 'es']

export function normalizeLocale(value) {
  const raw = String(value || '').trim().replace(/_/g, '-').toLowerCase()
  if (!raw) return null
  const parts = raw.split('-').filter(Boolean)
  const primary = parts[0]
  if (primary === 'zh') {
    if (parts.includes('hans')) return 'zh-CN'
    if (parts.includes('hk') || parts.includes('mo')) return 'zh-HK'
    if (parts.includes('tw') || parts.includes('hant')) return 'zh-TW'
    return 'zh-CN'
  }
  if (primary === 'ja') return 'ja'
  if (primary === 'en') return 'en'
  if (primary === 'fr') return 'fr'
  if (primary === 'de') return 'de'
  if (primary === 'ru') return 'ru'
  if (primary === 'es') return 'es'
  return null
}

export function detectBrowserLocale(nav = typeof navigator === 'undefined' ? null : navigator, fallback = 'zh-CN') {
  if (!nav) return fallback
  const candidates = [
    ...(Array.isArray(nav.languages) ? nav.languages : []),
    nav.language,
  ]
  for (const candidate of candidates) {
    const locale = normalizeLocale(candidate)
    if (locale) return locale
  }
  return fallback
}

/** Explicit CTA selection wins over stored/browser language, only on game URLs. */
export function requestedGameLocale(path, search) {
  if (!/^\/2d(?:\/|$)/.test(path)) return null
  return normalizeLocale(new URLSearchParams(search).get('lang'))
}
