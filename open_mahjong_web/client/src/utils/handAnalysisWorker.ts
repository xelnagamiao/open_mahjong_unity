import { analyzeHand, type HandDraft } from './handAnalysis.ts'
import type { PailiOptions } from './pailiCalculator.ts'
import type { CalculatorRule } from './calculatorRules.ts'
self.onmessage = ({ data }: MessageEvent<{ id: number; draft: HandDraft; options?: PailiOptions; rule?: CalculatorRule }>) => {
  try { self.postMessage({ id: data.id, result: analyzeHand(data.draft, data.options, data.rule) }) }
  catch (error) { self.postMessage({ id: data.id, error: error instanceof Error ? error.message : String(error) }) }
}
