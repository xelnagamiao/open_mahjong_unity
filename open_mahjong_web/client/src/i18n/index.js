import { computed, ref, watch } from 'vue'
import { messages, textPatterns } from './messages'

export const SUPPORTED_LOCALES = ['zh-CN', 'zh-TW', 'zh-HK', 'en', 'ja']
export const LOCALE_STORAGE_KEY = 'salasasa.language'

function normalizeLocale(value) {
  const locale = String(value || '').replace('_', '-').toLowerCase()
  if (locale === 'zh-hk' || locale === 'zh-mo' || locale.includes('-hk') || locale.includes('-mo')) return 'zh-HK'
  if (locale === 'zh-tw' || locale.includes('-tw') || locale.includes('hant')) return 'zh-TW'
  if (locale.startsWith('zh')) return 'zh-CN'
  if (locale.startsWith('ja')) return 'ja'
  if (locale.startsWith('en')) return 'en'
  return null
}

function detectLocale() {
  try {
    const saved = normalizeLocale(window.localStorage.getItem(LOCALE_STORAGE_KEY))
    if (saved) return saved
  } catch {
    // Storage may be unavailable in privacy modes.
  }
  const candidates = typeof navigator === 'undefined'
    ? []
    : [...(navigator.languages || []), navigator.language]
  for (const candidate of candidates) {
    const locale = normalizeLocale(candidate)
    if (locale) return locale
  }
  return 'zh-CN'
}

export const locale = ref(typeof window === 'undefined' ? 'zh-CN' : detectLocale())

const ROUND_WINDS = ['东', '南', '西', '北']
const ROUND_NUMBERS = ['一', '二', '三', '四']

export function roundLabelKey(roundCounter, format = 'wind-seat', targetLocale = locale.value) {
  const number = Number(roundCounter)
  if (!Number.isFinite(number)) return ''
  const index = Math.max(0, Math.trunc(number) - 1)
  const prevailingWind = ROUND_WINDS[Math.floor(index / 4) % 4]
  const handIndex = index % 4
  return targetLocale === 'en' || format === 'round-number'
    ? `${prevailingWind}${ROUND_NUMBERS[handIndex]}局`
    : `${prevailingWind}风${ROUND_WINDS[handIndex]}`
}

export function setLocale(value) {
  const next = normalizeLocale(value) || 'zh-CN'
  locale.value = next
  if (typeof document !== 'undefined') document.documentElement.lang = next
  try {
    window.localStorage.setItem(LOCALE_STORAGE_KEY, next)
  } catch {
    // Keep the in-memory selection when storage is unavailable.
  }
}

export function tr(source, params = {}, targetLocale = locale.value) {
  if (source == null) return ''
  const raw = String(source)
  let translated = messages[targetLocale]?.[raw] ?? raw
  if (translated === raw && targetLocale !== 'zh-CN') {
    for (const pattern of textPatterns[targetLocale] || []) {
      const match = raw.match(pattern.match)
      if (match) {
        translated = pattern.replace(...match.slice(1))
        break
      }
    }
  }
  return translated.replace(/\{(\w+)\}/g, (_, key) => (
    Object.prototype.hasOwnProperty.call(params, key) ? String(params[key]) : `{${key}}`
  ))
}

export function useI18n() {
  return {
    locale,
    language: computed(() => locale.value),
    setLocale,
    t: tr,
  }
}

export function installDomLocalization() {
  if (typeof document === 'undefined' || typeof MutationObserver === 'undefined') return () => {}
  const originals = new WeakMap()
  let observer
  const isLocalizationDisabled = () => /^\/admin(?:\/|$)/.test(window.location.pathname)

  const translateNode = (node, refresh = false) => {
    if (isLocalizationDisabled()) return
    if (node.nodeType === Node.TEXT_NODE) {
      const parent = node.parentElement
      if (!parent || ['SCRIPT', 'STYLE', 'TEXTAREA'].includes(parent.tagName)) return
      if (parent.closest?.('[data-no-translate]')) return
      const current = refresh && originals.has(node) ? originals.get(node) : node.nodeValue
      const leading = current.match(/^\s*/)?.[0] || ''
      const trailing = current.match(/\s*$/)?.[0] || ''
      const content = current.trim()
      if (!content) return
      const translated = tr(content)
      if (translated !== content || originals.has(node)) {
        if (!originals.has(node)) originals.set(node, current)
        node.nodeValue = `${leading}${translated}${trailing}`
      }
      return
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return
    if (node.hasAttribute?.('data-no-translate')) return
    for (const attribute of ['aria-label', 'title', 'placeholder']) {
      if (!node.hasAttribute(attribute)) continue
      const key = `attr:${attribute}`
      const source = refresh && node[key] ? node[key] : node.getAttribute(attribute)
      const translated = tr(source)
      if (translated !== source || node[key]) {
        if (!node[key]) node[key] = source
        node.setAttribute(attribute, translated)
      }
    }
    for (const child of node.childNodes) translateNode(child, refresh)
  }

  const observe = () => {
    observer = new MutationObserver((mutations) => {
      observer.disconnect()
      for (const mutation of mutations) {
        if (mutation.type === 'characterData') {
          originals.delete(mutation.target)
          translateNode(mutation.target)
        } else {
          for (const node of mutation.addedNodes) translateNode(node)
        }
      }
      observe()
    })
    observer.observe(document.body, { childList: true, subtree: true, characterData: true })
  }

  translateNode(document.body)
  observe()
  const stop = watch(locale, () => {
    document.documentElement.lang = locale.value
    observer.disconnect()
    translateNode(document.body, true)
    observe()
  })
  document.documentElement.lang = locale.value
  return () => {
    stop()
    observer?.disconnect()
  }
}
