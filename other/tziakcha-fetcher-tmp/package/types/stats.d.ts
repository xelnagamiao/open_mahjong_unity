import type { SessionSummary } from "./types/shared";

declare function summarizeSession(session: {
  sessionId: string;
  players?: Array<{ name?: string; id?: string | number }>;
  records?: Array<Record<string, unknown>>;
}): SessionSummary;

declare const statsApi: {
  summarizeSession: typeof summarizeSession;
};

export = statsApi;
