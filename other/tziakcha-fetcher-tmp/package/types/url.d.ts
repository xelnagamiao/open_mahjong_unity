declare function parseTziakchaSessionId(input: unknown): string | null;

declare const urlApi: {
  parseTziakchaSessionId: typeof parseTziakchaSessionId;
};

export = urlApi;
