/** User-facing labels only; the saved event/payment objects remain unchanged. */
export function hangzhouActionLabel(tick) {
  if (tick?.[0] !== 'hangzhou') return null
  if (tick[1] === 'tail_burn') return '杠后暗弃'
  if (tick[1] === 'hints') return '听牌提示'
  if (tick[1] === 'win_source' && tick[3] === 'ten_winds') return '十风和'
  if (tick[1] === 'state' && tick[2]?.phase === 'waiting_hangzhou_ten_winds') return '十风和牌选择'
  return '杭州状态'
}

export function hangzhouPaymentReason(info, reason) {
  return info?.ledger?.source === 'ten_winds' && reason === '自摸' ? '十风' : reason
}

export function parseHangzhouFan(label) {
  const fields = String(label || '').split('|')
  if (fields.length < 4 || fields[0] !== 'HZ') return null
  return { name: fields[3], value: `${fields[2]}番` }
}
export function hangzhouInfoAt(round, node) {
  let info = round?.hangzhou || null
  for (const tick of (round?.action_ticks || []).slice(0, Math.max(0, node))) {
    if (tick[0] === 'hangzhou' && tick[1] === 'state' && tick[2] && typeof tick[2] === 'object') info = tick[2]
    if (tick[0] === 'hangzhou' && tick[1] === 'win_source' && tick[5] && typeof tick[5] === 'object') info = tick[5]
  }
  return info
}

function sameTiles(a, b) {
  if (!Array.isArray(a) || !Array.isArray(b) || a.length !== b.length) return false
  const sorted = [...b].sort()
  return [...a].sort().every((value, index) => value === sorted[index])
}
const physicalTile = tile => Number.isInteger(tile) && ((tile >= 11 && tile <= 39 && tile % 10 >= 1 && tile % 10 <= 9) || (tile >= 41 && tile <= 47))

/** Ordinary draws stop with twenty physical tiles still in the wall. */
export function hangzhouNormalDrawableWallIndices(tiles) {
  const remaining = []
  tiles.forEach((tile, index) => { if (!tile.consumed) remaining.push(index) })
  return new Set(remaining.slice(0, Math.max(0, remaining.length - 20)))
}

/** Count only the chosen viewer's own hand and public tiles, never concealed opponents or burned tiles. */
export function hangzhouKnownTiles(snapshot, toPhysical) {
  const viewer = snapshot.seats.find(seat => seat.seat_index === snapshot.viewer.seat_index)
  const known = [...(viewer?.hand_tiles || [])]
  if (viewer?.drawn_tile != null) known.push(viewer.drawn_tile)
  const physical = known.map(toPhysical)
  for (const seat of snapshot.seats) {
    physical.push(...seat.discard_pile.map(toPhysical))
    for (const meld of seat.melds) {
      if (meld.concealed && seat.seat_index !== viewer?.seat_index) continue
      const tile = toPhysical(meld.tile)
      physical.push(...(meld.physical_tiles?.length ? meld.physical_tiles.map(toPhysical)
        : meld.type === 'sequence' ? [tile - 1, tile, tile + 1]
          : Array(meld.type === 'kong' ? 4 : 3).fill(tile)))
    }
  }
  return physical.filter(physicalTile)
}

/** Replay the server's seat-private structural hints. Old records without hints remain compatible. */
export function hangzhouWaitDataAt(round, node, seat, hand, melds, knownTiles) {
  let hints = null
  for (const tick of (round?.action_ticks || []).slice(0, Math.max(0, node))) {
    if (tick[0] === 'hangzhou' && tick[1] === 'hints' && Number(tick[2]) === seat) hints = tick[3]
  }
  if (!hints || !Array.isArray(hints.source_hand_tiles) || !sameTiles(hints.source_melds || [], melds)) return null
  const details = waits => [...new Set(Array.isArray(waits) ? waits : [])].filter(physicalTile).sort((a, b) => a - b).map(tile => ({
    tile, base_f: 0,
    // The shared wait renderer uses this eligibility flag to display "仅自摸", not a fan count.
    selfdrawn_f: 1,
    remaining_count: Math.max(0, 4 - knownTiles.filter(value => value === tile).length),
  }))
  if (sameTiles(hints.source_hand_tiles, hand)) {
    if (hand.length % 3 === 2) {
      const rows = Object.entries(hints.waiting_by_discard || {}).filter(([tile]) => hand.includes(Number(tile)))
        .map(([tile, waits]) => ({ discard_tile: Number(tile), adds: details(waits) })).filter(row => row.adds.length)
      return rows.length ? { type: 'waits_all', details: rows } : null
    }
    const waits = details(hints.waiting_tiles)
    return waits.length ? { type: 'waits', details: waits } : null
  }
  // A cut may render before the next hints event; derive only an explicitly authorized row.
  if (hints.source_hand_tiles.length === hand.length + 1) {
    for (const [tile, waits] of Object.entries(hints.waiting_by_discard || {})) {
      const source = [...hints.source_hand_tiles]
      const index = source.indexOf(Number(tile))
      if (index < 0) continue
      source.splice(index, 1)
      if (sameTiles(source, hand)) {
        const row = details(waits)
        return row.length ? { type: 'waits', details: row } : null
      }
    }
  }
  return null
}
