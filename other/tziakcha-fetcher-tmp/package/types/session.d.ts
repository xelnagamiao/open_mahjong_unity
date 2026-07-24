import type { FetcherOptions, Session, SessionRounds } from "./types/shared";

declare function fetch(
  sessionId: string,
  options?: FetcherOptions
): Promise<Session>;

declare function fetchRounds(
  inputUrlOrId: string,
  options?: FetcherOptions
): Promise<SessionRounds>;

declare const sessionApi: {
  fetch: typeof fetch;
  fetchRounds: typeof fetchRounds;
};

export = sessionApi;
