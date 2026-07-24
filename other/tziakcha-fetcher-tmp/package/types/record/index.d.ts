import type {
  FetcherOptions,
  RecordData,
  RoundWinInfo,
  StepData,
  WinFanItem
} from "../shared";

declare function decompress(input: string): Promise<string>;
declare function fetch(
  recordId: string,
  options?: FetcherOptions
): Promise<RecordData>;
declare function fetchStep(
  recordId: string,
  options?: FetcherOptions
): Promise<StepData>;
declare function parseWinFanItems(raw: unknown): WinFanItem[];
declare function extractWins(session: { records?: Array<{ id: string; index: number; step?: StepData }>; players?: Array<{ name?: string; n?: string }> }): RoundWinInfo[];
declare function decodeAction(action: [number, number, number]): {
  playerIndex: number;
  type: number;
  typeName: string;
  data: number;
  time: number;
  detail: Record<string, unknown>;
};
declare function simulate(record: { id?: string; step: StepData }): {
  steps: Array<Record<string, unknown>>;
  state: Record<string, unknown>;
  resultFlags: Record<string, unknown>;
  roundWind: string;
};

declare const recordApi: {
  decodeAction: typeof decodeAction;
  decompress: typeof decompress;
  extractWins: typeof extractWins;
  fetch: typeof fetch;
  fetchStep: typeof fetchStep;
  parseWinFanItems: typeof parseWinFanItems;
  simulate: typeof simulate;
};

export = recordApi;
