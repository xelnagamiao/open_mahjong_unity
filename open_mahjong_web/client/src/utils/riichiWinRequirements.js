import option from '../../../shared/riichiWinRequirements.json' with { type: 'json' }

export const riichiWinLines = option.values.map((value, index) => ({ value, label: option.choices[index] }))
export const riichiWinLineHelp = option.help
export const riichiWinLineLabel = value => riichiWinLines.find(line => line.value === value)?.label || '不限'

export function riichiWinRequirements(form) {
  if (!Number.isInteger(form.riichi_hepai_limit) || form.riichi_hepai_limit < 1 || form.riichi_hepai_limit > 64) {
    throw new Error('起和番须为 1–64 的整数（不含宝牌）')
  }
  if (!option.values.includes(form.minimum_win_score)) throw new Error('请选择有效的起和线')
  return { hepai_limit: form.riichi_hepai_limit, detailed_config: { minimum_win_score: form.minimum_win_score } }
}
