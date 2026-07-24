import type { FetcherOptions, RecordData, StepData } from "../types/shared";

declare function decompressZlibBase64(input: string): Promise<string>;
declare function decodeRecordStep(
  recordId: string,
  raw: { script?: string; step?: StepData; [key: string]: unknown },
  decompress: typeof decompressZlibBase64,
  options?: FetcherOptions
): Promise<StepData>;
declare function fetchTziakchaRecord(
  recordId: string,
  options?: FetcherOptions
): Promise<RecordData>;
declare function fetchTziakchaRecordStep(
  recordId: string,
  options?: FetcherOptions
): Promise<StepData>;

declare const recordFetchApi: {
  decompressZlibBase64: typeof decompressZlibBase64;
  decodeRecordStep: typeof decodeRecordStep;
  fetchTziakchaRecord: typeof fetchTziakchaRecord;
  fetchTziakchaRecordStep: typeof fetchTziakchaRecordStep;
};

export = recordFetchApi;
