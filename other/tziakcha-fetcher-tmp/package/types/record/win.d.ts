import type { RoundWinInfo, WinFanItem } from "../types/shared";

declare const FAN_NAMES: string[];
declare const SEAT_PLAYER_ORDERS: number[][];

declare function parseTziakchaWinFanItems(raw: unknown): WinFanItem[];
declare function extractTziakchaRoundWinInfos(session: {
  sessionId?: string;
  players?: Array<{ name?: string; n?: string }>;
  records?: Array<{ id: string; index: number; step?: Record<string, unknown> }>;
}): RoundWinInfo[];

declare const winApi: {
  FAN_NAMES: typeof FAN_NAMES;
  SEAT_PLAYER_ORDERS: typeof SEAT_PLAYER_ORDERS;
  extractTziakchaRoundWinInfos: typeof extractTziakchaRoundWinInfos;
  parseTziakchaWinFanItems: typeof parseTziakchaWinFanItems;
};

export = winApi;
