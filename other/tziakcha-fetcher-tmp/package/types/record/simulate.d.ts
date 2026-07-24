import type { StepData } from "../types/shared";

declare function simulateTziakchaRecord(record: { id?: string; step: StepData }): {
  recordId?: string;
  initialHands: number[][];
  steps: Array<Record<string, unknown>>;
  state: Record<string, unknown>;
  resultFlags: {
    winnerMask: number;
    discarderMask: number;
  };
  roundWind: string;
};

declare const simulateApi: {
  simulateTziakchaRecord: typeof simulateTziakchaRecord;
};

export = simulateApi;
