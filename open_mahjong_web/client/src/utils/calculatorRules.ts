import type { PailiOptions } from './pailiCalculator.ts'

export type CalculatorRule = 'guobiao' | 'qingque' | 'riichi'
export const CALCULATOR_RULES = ['guobiao', 'qingque', 'riichi'] as const
export const RULE_LABELS = { guobiao: '国标', qingque: '青雀', riichi: '日麻' }
export const ruleFromPath = (path: string): CalculatorRule => path.endsWith('/qingque') ? 'qingque' : path.endsWith('/riichi') ? 'riichi' : 'guobiao'
export const calculatorPath = (rule: CalculatorRule) => `/calc/${rule === 'guobiao' ? 'chinese' : rule}`
export const pailiPath = (rule: CalculatorRule) => rule === 'guobiao' ? '/paili' : `/paili/${rule}`

export function ruleOptions(rule: CalculatorRule = 'guobiao'): Required<PailiOptions> {
  return {
    mcrSevenPairs: rule !== 'riichi', riichiSevenPairs: rule === 'riichi',
    thirteenOrphans: true, unrelatedTiles: rule === 'guobiao', combinationDragon: rule === 'guobiao',
  }
}

export function analysisOptions(rule: CalculatorRule, options?: PailiOptions): Required<PailiOptions> {
  const selected = { ...ruleOptions(rule), ...options }
  if (rule !== 'guobiao') { selected.unrelatedTiles = false; selected.combinationDragon = false }
  if (rule === 'riichi') selected.mcrSevenPairs = false
  return selected
}
