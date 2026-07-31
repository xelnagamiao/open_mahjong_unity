/**
 * Split a settlement hand into concealed tiles and the separately rendered
 * winning tile. A normal ron payload contains only the pre-win concealed hand;
 * tsumo (and some penalty payloads) contain one extra winning tile.
 */
export function splitSettlementHand(handTiles, winningTile, meldCount, sortKey) {
  const tiles = Array.isArray(handTiles) ? [...handTiles] : []
  const expectedConcealedCount = Math.max(0, 13 - Math.max(0, Number(meldCount) || 0) * 3)

  if (tiles.length > expectedConcealedCount) {
    let winningIndex = tiles.lastIndexOf(winningTile)
    if (winningIndex < 0 && tiles.length === expectedConcealedCount + 1) {
      winningIndex = tiles.length - 1
    }
    if (winningIndex >= 0) tiles.splice(winningIndex, 1)
  }

  return tiles.sort((left, right) => sortKey(left) - sortKey(right))
}

/** Salasasa ids: 11-19万, 21-29筒, 31-39索, 41-47字, 51-58花. */
export function salasasaSettlementSortKey(tile) {
  const normalized = Number(tile) >= 100 ? Number(tile) % 100 : Number(tile)
  const suit = Math.floor(normalized / 10)
  const rank = normalized % 10
  const suitOrder = { 1: 0, 2: 1, 3: 2, 4: 3, 5: 4 }
  return (suitOrder[suit] ?? 9) * 16 + rank
}

/** MMCR ids use bit suits; honors must follow Man, Pin and Sou. */
export function mmcrSettlementSortKey(tile) {
  const value = Number(tile)
  const suit = value & 0xe0
  const rank = value & 0x0f
  const suitOrder = { 0x40: 0, 0x60: 1, 0xc0: 2, 0xa0: 3, 0xe0: 4 }
  return (suitOrder[suit] ?? 9) * 16 + rank
}
