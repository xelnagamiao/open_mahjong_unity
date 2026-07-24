import type { AnalyzeOptions, AnalyzeResult } from "../shared";

declare function analyze(
  record: { id?: string; step: Record<string, unknown> },
  options?: AnalyzeOptions
): AnalyzeResult;

declare const nodeApi: {
  analyze: typeof analyze;
};

export = nodeApi;
