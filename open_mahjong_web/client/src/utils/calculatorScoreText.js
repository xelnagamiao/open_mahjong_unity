export const preciseFan = value => Number(Number(value).toFixed(5)).toString()
export function calculatorScoreText(score) {
  if (score.rule === 'riichi') return score.canWin ? `${score.points} 点 · ${score.yakuman ? `${score.yakuman > 1 ? score.yakuman + ' 倍' : ''}役满` : `${score.fan} 番 ${score.fu} 符`}` : '无役，不能和牌'
  if (score.rule === 'qingque') return `${preciseFan(score.fan)} 番 · 底分 ${score.points}`
  return `${score.fan} 番`
}
