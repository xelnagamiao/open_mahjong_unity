import fr from './locales/fr.js'
import de from './locales/de.js'
import ru from './locales/ru.js'
import { createFrenchPatterns } from './fr-patterns.js'
import { createGermanPatterns } from './de-patterns.js'
import { createRussianPatterns } from './ru-patterns.js'

const catalogs = { fr, de, ru }
export const extendedMessages = Object.fromEntries(Object.entries(catalogs).map(([locale, catalog]) => [locale, catalog.messages]))

const placeholders = /\{(?:[A-Za-z_][^{}\r\n]*|\d+(?::[^{}\r\n]+)?)\}/g
const escape = value => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

function templates(messages) {
  return Object.entries(messages)
    .filter(([key]) => /[\u3400-\u9fff]/u.test(key) && [...key.matchAll(placeholders)].length)
    .sort(([a], [b]) => b.replace(placeholders, '').length - a.replace(placeholders, '').length || b.length - a.length)
    .map(([key, text]) => {
      const matches = [...key.matchAll(placeholders)]
      let pattern = '^', offset = 0
      for (const match of matches) {
        pattern += escape(key.slice(offset, match.index))
        pattern += matches.length === 1 && /^\{[^}]+\}\s*(分钟|本场|位|名|级|段|人|场|局|副|番|符|点|分|张|枚|件|秒|页|条|圈|巡|底|台|倍|组|种|项|次)$/.test(key) ? '([+−\\-]?[\\d,.]+)' : key.includes('\n') ? '(.+?)' : '([^\\r\\n]+?)'
        offset = match.index + match[0].length
      }
      pattern += escape(key.slice(offset)) + '$'
      return {
        match: new RegExp(pattern, 's'),
        replace: (...values) => text.replace(placeholders, token => values[matches.findIndex(match => match[0] === token)]),
      }
    })
}

export function formatLocaleUnit(value, unit, locale, context = '') {
  context = String(context || '').split('/')[0]
  const units = catalogs[locale]?.units
  const forms = units?.[context ? `${context}:${unit}` : unit] || units?.[unit]
  if (!forms) return null
  const number = Math.abs(Number(String(value).replace('−', '-').replace(/,/g, '')))
  const category = Number.isFinite(number) ? new Intl.PluralRules(locale, { type: forms.ordinal ? 'ordinal' : 'cardinal' }).select(number) : 'other'
  return typeof forms === 'string' ? forms : forms[category] || forms.other
}

export function formatLocaleQuantity(value, unit, locale, context = '') {
  const word = formatLocaleUnit(value, unit, locale, context)
  return word == null ? null : word.includes('{n}') ? word.split('{n}').join(String(value)) : `${value} ${word}`
}

const contextTemplateCache = new WeakMap()
export function contextualMessage(source, locale, context) {
  context = String(context || '').split('/')[0]
  const messages = catalogs[locale]?.contexts?.[context]
  if (!messages) return null
  if (Object.hasOwn(messages, source)) return messages[source]
  const repeated = String(source).match(/^(.*?)\s*([*×])\s*(\d+)$/)
  if (repeated && Object.hasOwn(messages, repeated[1])) return `${messages[repeated[1]]}${repeated[2]}${repeated[3]}`
  if (!contextTemplateCache.has(messages)) contextTemplateCache.set(messages, templates(messages))
  for (const template of contextTemplateCache.get(messages)) {
    const match = String(source).match(template.match)
    if (match) return template.replace(...match.slice(1))
  }
  return null
}

const quantityPattern = locale => ({
  match: /^([+−\-]?[\d,.]+)\s*(分钟|本场|人|名|场|局|副|番|翻|符|点|分|张|枚|件|秒|级|段|页|条|底|台|倍|巡|圈|组|种|项|次)$/,
  replace: (value, unit) => formatLocaleQuantity(value, unit, locale) || `${value}${unit}`,
})

export const extendedPatterns = {}
const translate = locale => {
  let depth = 0
  return source => {
    const raw = String(source)
    const exact = extendedMessages[locale][raw] ?? extendedMessages[locale][raw.replace(/\s+/g, ' ').trim()]
    if (exact != null) return exact
    if (depth >= 8) return raw
    depth++
    try {
      for (const pattern of extendedPatterns[locale] || []) {
        const match = raw.match(pattern.match)
        if (match) return pattern.replace(...match.slice(1))
      }
      return raw
    } finally { depth-- }
  }
}
Object.assign(extendedPatterns, {
  fr: [quantityPattern('fr'), ...createFrenchPatterns(translate('fr')), ...templates(fr.messages)],
  de: [quantityPattern('de'), ...createGermanPatterns(translate('de')), ...templates(de.messages)],
  ru: [quantityPattern('ru'), ...createRussianPatterns(translate('ru')), ...templates(ru.messages)],
})
