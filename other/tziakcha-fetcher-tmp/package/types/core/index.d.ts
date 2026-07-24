declare const coreApi: {
  config: typeof import("./config");
  tiles: {
    decodeWallHex(wallHex: string): number[];
    groupHandToGbString(tileIds: number[]): string;
    tileIdToBase(tileId: number): number;
    tileIdToGbTile(tileId: number): string;
  };
  network: {
    DEFAULT_BASE_URL: string;
    assertOk(response: { ok: boolean; status: number }, endpoint: string): void;
    buildUrl(path: string, options?: { baseUrl?: string }): string;
    getFetch(options?: Record<string, unknown>): Function;
    mergeHeaders(
      options?: { headers?: Record<string, string> },
      headers?: Record<string, string>
    ): Record<string, string>;
  };
};

export = coreApi;
