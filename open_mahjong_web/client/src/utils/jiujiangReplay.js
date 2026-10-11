/** Never reuse another seat's hints, or an older hand's still-cached hints. */
export function jiujiangHintsAt(round, node, seat, physicalHand, physicalMelds) {
  if (!Number.isInteger(seat) || seat < 0 || seat > 3 || !Array.isArray(physicalHand) || !Array.isArray(physicalMelds)) return null
  const cache = [null, null, null, null]
  for (const tick of (round?.action_ticks || []).slice(0, Math.max(0, node))) {
    if (tick?.[0] !== 'jiujiang' || tick[1] !== 'hints' || !Number.isInteger(tick[2]) || tick[2] < 0 || tick[2] > 3) continue
    cache[tick[2]] = tick[3]
  }
  const hints = cache[seat]
  if (!hints || typeof hints !== 'object' || !Array.isArray(hints.source_hand_tiles) || !Array.isArray(hints.source_melds)) return null
  const same = (a, b) => {
    const left = [...a].sort(), right = [...b].sort()
    return left.length === right.length && left.every((value, index) => value === right[index])
  }
  if (!same(hints.source_melds, physicalMelds)) return null
  if (same(hints.source_hand_tiles, physicalHand)) return hints
  if (hints.source_hand_tiles.length === physicalHand.length + 1) {
    for (const [discard, waits] of Object.entries(hints.waiting_by_discard || {})) {
      const remaining = [...hints.source_hand_tiles]
      const index = remaining.indexOf(Number(discard))
      if (index < 0 || !Array.isArray(waits)) continue
      remaining.splice(index, 1)
      if (same(remaining, physicalHand)) return {
        ...hints, source_hand_tiles: [...physicalHand], waiting_tiles: [...waits], waiting_by_discard: {},
        waiting_details: hints.waiting_details_by_discard?.[discard] || {}, waiting_details_by_discard: {},
      }
    }
  }
  return null
}

/** Own physical hand plus publicly identifiable tiles; never hidden opponents. */
export function jiujiangKnownTiles(snapshot, toPhysical) {
  const viewer = snapshot.seats.find(item => item.seat_index === snapshot.viewer.seat_index)
  const known = [...(viewer?.hand_tiles || [])]
  if (viewer?.drawn_tile != null) known.push(viewer.drawn_tile)
  const tiles = known.map(toPhysical)
  for (const seat of snapshot.seats) {
    tiles.push(...(seat.discard_pile || []).map(toPhysical))
    for (const meld of seat.melds || []) {
      if (meld.concealed && seat.seat_index !== viewer?.seat_index
        && !meld.concealed_face_down?.some(hidden => !hidden)) continue
      const tile = toPhysical(meld.tile)
      tiles.push(...(meld.physical_tiles?.length ? meld.physical_tiles.map(toPhysical)
        : Array(meld.type === 'kong' ? 4 : 3).fill(tile)))
    }
  }
  return tiles
}

/** Stable and discard-preview rows share the server's ordinary-shape scores. */
export function jiujiangWaitDataAt(round, node, seat, hand, melds, knownTiles) {
  const hints = jiujiangHintsAt(round, node, seat, hand, melds)
  if (!hints) return null
  const details = (waits, scores) => [...new Set(Array.isArray(waits) ? waits : [])]
    .sort((a, b) => a - b).map(tile => {
      const score = scores?.[tile]
      const scored = Number.isInteger(score?.points) && score.points >= 0
      const kind = scored && score.points > 0 && ['dianhe','zimo'].includes(score.kind) ? score.kind : 'wuyi'
      return { tile, base_f: kind === 'dianhe' ? score.points : 0, selfdrawn_f: kind === 'zimo' ? score.points : 0,
        label: `${scored ? score.points : 0}分`, kind,
        remaining_count: Math.max(0, 4 - knownTiles.filter(value => value === tile).length) }
    })
  if (hand.length % 3 === 2) {
    const rows = Object.entries(hints.waiting_by_discard || {})
      .filter(([tile]) => hand.includes(Number(tile)))
      .map(([tile, waits]) => ({discard_tile: Number(tile), adds: details(waits, hints.waiting_details_by_discard?.[tile])}))
      .filter(row => row.adds.length)
    return rows.length ? {type: 'waits_all', details: rows} : null
  }
  const waits = details(hints.waiting_tiles, hints.waiting_details)
  return waits.length ? {type: 'waits', details: waits} : null
}

export function parseJiujiangFan(value) {
  const match = /^(.*)（(\d+)分）$/.exec(String(value || ''))
  return match ? {name: match[1], value: `${Number(match[2])}分`} : {name: String(value || ''), value: ''}
}

export function jiujiangPaymentsAt(round, node) {
  let rows = []
  for (const tick of (round?.action_ticks || []).slice(0, Math.max(0,node)))
    if (tick?.[0] === 'jiujiang' && tick[1] === 'payments' && Array.isArray(tick[2])) rows = tick[2]
  return rows
}
