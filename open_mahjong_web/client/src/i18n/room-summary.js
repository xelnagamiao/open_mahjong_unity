import { tr, locale } from './index.js'

// Room summaries contain only rule/configuration labels, never player input.
// Translate their fields separately so a template cannot swallow an untranslated
// rule name, switch value, or winning threshold as an opaque parameter.
export function localizeRoomSummary(source, language) {
  const code = language || locale.value
  if (code.startsWith('zh')) return String(source)
  const translate = value => tr(value, {}, language)
  return String(source).split(' · ').map(part => {
    const rounds = part.match(/^(\d+)\s*圈$/)
    if (rounds) {
      const count = Number(rounds[1])
      if (code === 'en') return `${count} ${count === 1 ? 'round' : 'rounds'}`
      return tr('{count} 圈', { count }, language)
    }
    const colon = part.indexOf('：')
    if (colon >= 0) return `${translate(part.slice(0, colon))}: ${translate(part.slice(colon + 1))}`
    return translate(part)
  }).join(' · ')
}
