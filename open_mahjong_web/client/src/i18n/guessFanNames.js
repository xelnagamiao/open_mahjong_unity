import { locale } from './index.js'
import { translateFanName } from './fanNames.js'

// Compare/reveal results retain their original rule labels and protocol data.
export function guessFanName(source, rules, targetLocale = locale.value) {
  const rule = Array.isArray(rules) ? rules[0] : String(rules || '').split('/')[0]
  const context = ['riichi', '立直', '日麻'].includes(rule) ? 'riichi' : 'guobiao'
  return translateFanName(source, targetLocale, context)
}
