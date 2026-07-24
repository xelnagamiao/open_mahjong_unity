import type { AnalyzeOptions, AnalyzeResult } from "../shared";

declare function analyze(
  record: { id?: string; step: Record<string, unknown> },
  options?: AnalyzeOptions
): AnalyzeResult;

export = analyze;
