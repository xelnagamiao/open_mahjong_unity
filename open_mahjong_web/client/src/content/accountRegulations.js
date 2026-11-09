import zh from './accountRegulations.txt?raw'
import en from './accountRegulations.en.txt?raw'
import fr from './accountRegulations.fr.txt?raw'
import de from './accountRegulations.de.txt?raw'
import ru from './accountRegulations.ru.txt?raw'
import ja from './accountRegulations.ja.txt?raw'
import { messages } from '../i18n/messages.js'

export function accountRegulationsFor(locale) {
  if (locale === 'zh-TW' || locale === 'zh-HK') return messages[locale][zh]
  return ({ en, fr, ja, de, ru })[locale] || zh
}

export function accountRegulationsBody(locale) {
  const text = accountRegulationsFor(locale)
  return text.slice(text.indexOf('\n') + 1).trim()
}
