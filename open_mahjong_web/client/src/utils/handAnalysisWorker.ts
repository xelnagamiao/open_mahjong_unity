import { analyzeHand, type HandDraft } from './handAnalysis.ts'
import type { PailiOptions } from './pailiCalculator.ts'
self.onmessage = ({ data }: MessageEvent<{ id: number; draft: HandDraft; options?: PailiOptions }>) => {
  try { self.postMessage({ id: data.id, result: analyzeHand(data.draft, data.options) }) }
  catch (error) { self.postMessage({ id: data.id, error: error instanceof Error ? error.message : String(error) }) }
}
