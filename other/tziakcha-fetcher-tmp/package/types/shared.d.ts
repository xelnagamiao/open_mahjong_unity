export type JsonObject = Record<string, unknown>;

export interface FetcherOptions {
  baseUrl?: string;
  fetch?: (input: string, init?: Record<string, unknown>) => Promise<{
    ok: boolean;
    status: number;
    json(): Promise<unknown>;
  }>;
  headers?: Record<string, string>;
  decompressZlibBase64?: (input: string) => Promise<string>;
}

export interface SessionPlayer {
  name: string;
  id?: string | number;
}

export interface SessionRecordRef {
  id: string;
  index: number;
}

export interface Session {
  sessionId: string;
  players: SessionPlayer[];
  records: SessionRecordRef[];
  periods: number | null;
  isFinished: boolean;
  raw: JsonObject;
}

export interface StepData {
  [key: string]: unknown;
}

export interface RecordData {
  id: string;
  belongs?: string;
  script: "<Decoded>";
  step: StepData;
  raw: JsonObject;
}

export interface SessionRound extends SessionRecordRef {
  step: StepData;
}

export interface SessionRounds extends Omit<Session, "records"> {
  records: SessionRound[];
}

export interface WinFanItem {
  fanIndex: number;
  fanName: string;
  count: number;
  unitFan: number;
  totalFan: number;
}

export interface RoundWinner {
  playerName: string;
  playerIndex: number;
  totalFan: number;
  fanItems: WinFanItem[];
  winTile: number | null;
  winTileName: string | null;
}

export interface RoundDiscarder {
  playerName: string;
  playerIndex: number;
}

export interface RoundWinInfo {
  roundNo: number;
  recordId: string;
  winners: RoundWinner[];
  discarders: RoundDiscarder[];
  selfDraw: boolean;
  winTile: number | null;
  winTileName: string | null;
}

export interface SummaryPlayerStats {
  playerIndex: number;
  playerName: string;
  playerId?: string | number;
  rounds: number;
  wins: number;
  tsumoWins: number;
  ronWins: number;
  dealIns: number;
  tsumoAgainst: number;
  totalFan: number;
  fanCounts: Record<string, number>;
}

export interface SessionSummary {
  sessionId: string;
  totalRounds: number;
  finishedRounds: number;
  drawRounds: number;
  players: SummaryPlayerStats[];
  fanCounts: Record<string, number>;
}

export interface AnalyzeProblem {
  code: string;
  message: string;
  [key: string]: unknown;
}

export interface AnalyzeOptions {
  throwOnProblem?: boolean;
  onProblem?: (problem: AnalyzeProblem) => void;
  players?: Array<{ name?: string; n?: string }>;
  sessionPlayers?: Array<{ name?: string; n?: string }>;
  fanCalculator?: (input: {
    hand: string;
    winTile: number | null;
    winnerSeat: number;
    roundWind: string;
    seatWind: string;
    selfDraw: boolean;
    envFlags: string;
    packs: number[][];
    flowers: number[];
  }) => unknown;
}

export interface AnalyzeSeatInfo {
  seat: number;
  playerIndex: number;
  playerName: string | null;
}

export interface AnalyzeResult {
  recordId: string;
  winner: AnalyzeSeatInfo;
  discarder: AnalyzeSeatInfo | null;
  selfDraw: boolean;
  winTile: number | null;
  roundWind: string;
  seatWind: string;
  envFlags: {
    roundWind: string;
    seatWind: string;
    selfDraw: boolean;
    lastCopy: boolean;
    seaLast: boolean;
    robbingKong: boolean;
  };
  envFlagString: string;
  formattedHand: string;
  gbHandTilesString: string;
  handStringForGb: string;
  scriptedWin: {
    totalFan: number;
    fanDetails: WinFanItem[];
  };
  calculatedFan: unknown;
  problems: AnalyzeProblem[];
  simulated: Record<string, unknown>;
}
