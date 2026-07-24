declare const tilesApi: {
  decodeWallHex(wallHex: string): number[];
  groupHandToGbString(tileIds: number[]): string;
  tileIdToBase(tileId: number): number;
  tileIdToGbTile(tileId: number): string;
};

export = tilesApi;
