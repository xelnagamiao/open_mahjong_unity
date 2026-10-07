export const SUPPORTED_LOCALES = ['zh-CN', 'zh-TW', 'zh-HK', 'en', 'ja', 'fr']

export function normalizeLocale(value) {
  const locale = String(value || '').replace('_', '-').toLowerCase()
  if (locale === 'zh-hk' || locale === 'zh-mo' || locale.includes('-hk') || locale.includes('-mo')) return 'zh-HK'
  if (locale === 'zh-tw' || locale.includes('-tw') || locale.includes('hant')) return 'zh-TW'
  if (locale.startsWith('zh')) return 'zh-CN'
  if (locale.startsWith('ja')) return 'ja'
  if (locale.startsWith('en')) return 'en'
  if (locale.startsWith('fr')) return 'fr'
  return null
}

/** Explicit CTA selection wins over stored/browser language, only on game URLs. */
export function requestedGameLocale(path, search) {
  if (!/^\/2d(?:\/|$)/.test(path)) return null
  return normalizeLocale(new URLSearchParams(search).get('lang'))
}
