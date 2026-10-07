import { computed, ref, watch } from 'vue'
import { messages, textPatterns } from './messages'
import { SUPPORTED_LOCALES, normalizeLocale, requestedGameLocale } from './locale-utils.js'
import { mcrPageForPath } from '../seo/mcr-pages.js'

export { SUPPORTED_LOCALES }
export const LOCALE_STORAGE_KEY = 'salasasa.language'

function detectLocale() {
  const requested = requestedGameLocale(window.location.pathname, window.location.search)
  if (requested) return requested
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

function syncDocumentLanguage() {
  document.documentElement.lang = mcrPageForPath(window.location.pathname)?.locale || locale.value
}

const ROUND_WINDS = ['东', '南', '西', '北']
const ROUND_NUMBERS = ['一', '二', '三', '四']

export function roundLabelKey(roundCounter, format = 'wind-seat', targetLocale = locale.value) {
  const number = Number(roundCounter)
  if (!Number.isFinite(number)) return ''
  const index = Math.max(0, Math.trunc(number) - 1)
  const prevailingWind = ROUND_WINDS[Math.floor(index / 4) % 4]
  const handIndex = index % 4
  return targetLocale === 'en' || targetLocale === 'fr' || format === 'round-number'
    ? `${prevailingWind}${ROUND_NUMBERS[handIndex]}局`
    : `${prevailingWind}风${ROUND_WINDS[handIndex]}`
}

export function setLocale(value) {
  const next = normalizeLocale(value) || 'zh-CN'
  locale.value = next
  if (typeof document !== 'undefined') syncDocumentLanguage()
  try {
    window.localStorage.setItem(LOCALE_STORAGE_KEY, next)
  } catch {
    // Keep the in-memory selection when storage is unavailable.
  }
}

export function tr(source, params = {}, targetLocale = locale.value) {
  if (source == null) return ''
  const raw = String(source)
  let translated = messages[targetLocale]?.[raw] ?? messages[targetLocale]?.[raw.replace(/\s+/g, ' ').trim()] ?? raw
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
    if (node.closest?.('[data-no-translate]')) return
    for (const attribute of ['aria-label', 'title', 'placeholder', 'alt']) {
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
        } else if (mutation.type === 'attributes') {
          delete mutation.target[`attr:${mutation.attributeName}`]
          translateNode(mutation.target)
        } else {
          for (const node of mutation.addedNodes) translateNode(node)
        }
      }
      observe()
    })
    observer.observe(document.body, {
      childList: true, subtree: true, characterData: true,
      attributes: true, attributeFilter: ['aria-label', 'title', 'placeholder', 'alt'],
    })
  }

  translateNode(document.body)
  observe()
  const stop = watch(locale, () => {
    syncDocumentLanguage()
    observer.disconnect()
    translateNode(document.body, true)
    observe()
  })
  syncDocumentLanguage()
  return () => {
    stop()
    observer?.disconnect()
  }
}
