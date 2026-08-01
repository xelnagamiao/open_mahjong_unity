/**
 * Split a revealed winning hand into concealed tiles and the separately
 * rendered winning tile. Depending on the server/replay version, the payload
 * can be either the pre-win concealed hand or the complete hand with one extra
 * winning tile.
 */
export function splitWinningTileFromRevealedHand(handTiles, winningTile, expectedConcealedCount) {
  const tiles = Array.isArray(handTiles) ? [...handTiles] : []
  const expectedCount = Math.max(0, Number(expectedConcealedCount) || 0)

  if (tiles.length > expectedCount) {
    let winningIndex = tiles.lastIndexOf(winningTile)
    if (winningIndex < 0 && tiles.length === expectedCount + 1) {
      winningIndex = tiles.length - 1
    }
    if (winningIndex >= 0) tiles.splice(winningIndex, 1)
  }

  return tiles
}

export function splitSettlementHand(handTiles, winningTile, meldCount, sortKey) {
  const expectedConcealedCount = Math.max(0, 13 - Math.max(0, Number(meldCount) || 0) * 3)
  return splitWinningTileFromRevealedHand(
    handTiles,
    winningTile,
    expectedConcealedCount,
  ).sort((left, right) => sortKey(left) - sortKey(right))
}

/**
 * Convert server combination masks into the settlement-only meld layout.
 *
 * Mask modes: 0 upright, 1 sideways (the claimed tile), 2 concealed,
 * 3 stacked added-kong tile, 4 empty. Concealed kongs stay fully hidden in
 * play, but settlement follows the common riichi layout: back, face, face,
 * back.
 */
export function buildSettlementMeldGroups(combinationMasks) {
  if (!Array.isArray(combinationMasks)) return []

  return combinationMasks.map((mask) => {
    if (!Array.isArray(mask)) return null

    const pairs = []
    for (let index = 0; index + 1 < mask.length; index += 2) {
      const mode = Number(mask[index])
      const tile = Number(mask[index + 1])
      if (mode !== 4 && tile > 10) pairs.push({ mode, tile })
    }
    if (!pairs.length) return null

    const isConcealedKong = pairs.length === 4 && pairs.every(({ mode }) => mode === 2)
    if (isConcealedKong) {
      return {
        type: 'concealed-kong',
        tiles: pairs.map(({ tile }, index) => ({
          tile,
          sideways: false,
          faceDown: index === 0 || index === pairs.length - 1,
          stackedTile: null,
        })),
      }
    }

    const stackedTile = pairs.find(({ mode }) => mode === 3)?.tile ?? null
    const basePairs = pairs.filter(({ mode }) => mode !== 3)
    const sidewaysIndex = basePairs.findIndex(({ mode }) => mode === 1)

    return {
      type: 'open',
      tiles: basePairs.map(({ mode, tile }, index) => ({
        tile,
        sideways: mode === 1,
        faceDown: mode === 2,
        stackedTile: stackedTile && index === sidewaysIndex ? stackedTile : null,
      })),
    }
  }).filter((group) => group?.tiles.length)
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
