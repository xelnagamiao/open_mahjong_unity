import { locale, tr } from '../i18n/index.js'
import { formatLocaleQuantity, formatLocaleUnit } from '../i18n/extended-locales.js'
import { formatFanCount } from '../i18n/fanNames.js'

export const preciseFan = value => Number(Number(value).toFixed(5)).toString()
export const scorePointUnit = value => formatLocaleUnit(value, '点', locale.value) || tr('点')
export function riichiValueText(score) {
  if (!['fr', 'de', 'ru'].includes(locale.value)) return score.yakuman ? (score.yakuman > 1 ? `${score.yakuman} 倍役满` : '役满') : `${score.fan} 番 ${score.fu} 符`
  if (score.yakuman) return score.yakuman > 1 ? `${score.yakuman} × ${tr('役满')}` : tr('役满')
  return `${formatFanCount(score.fan, locale.value, 'riichi')} ${formatLocaleQuantity(score.fu, '符', locale.value)}`
}
export function calculatorScoreText(score, context = score.rule || 'guobiao') {
  if (['fr', 'de', 'ru'].includes(locale.value)) {
    if (score.rule === 'riichi') return score.canWin ? `${formatLocaleQuantity(score.points, '点', locale.value)} · ${riichiValueText(score)}` : tr('无役，不能和牌')
    if (score.rule === 'qingque') return `${formatFanCount(preciseFan(score.fan), locale.value, context)} · ${tr('底分')} ${score.points}`
    return formatFanCount(score.fan, locale.value, context)
  }
  if (score.rule === 'riichi') return score.canWin ? `${score.points} 点 · ${score.yakuman ? `${score.yakuman > 1 ? score.yakuman + ' 倍' : ''}役满` : `${score.fan} 番 ${score.fu} 符`}` : '无役，不能和牌'
  if (score.rule === 'qingque') return `${preciseFan(score.fan)} 番 · 底分 ${score.points}`
  return `${score.fan} 番`
}
