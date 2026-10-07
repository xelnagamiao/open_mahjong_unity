/** Replay uses the physical birds and substitutions stored by the server. */
export function hongzhongInfoAt(round, node) {
  let info = null
  for (const tick of (round?.action_ticks || []).slice(0, Math.max(0, node))) {
    if (tick?.[0] === 'hongzhong' && tick[1] === 'birds') info = tick[2]
  }
  return info && typeof info === 'object' && !Array.isArray(info) ? info : null
}

export function hongzhongKongEventsAt(round, node) {
  return (round?.action_ticks || []).slice(0, Math.max(0, node))
    .filter(tick => tick?.[0] === 'hongzhong' && ['kong_score', 'kong_refund'].includes(tick[1])
      && Array.isArray(tick[2]) && tick[2].length === 4 && tick[2].every(Number.isInteger))
    .map(tick => ({ kind: tick[1] === 'kong_refund' ? '流局退杠' : ({ direct: '明杠', added: '加杠', concealed: '暗杠' })[tick[3]] || '杠分', changes: [...tick[2]] }))
}

/** Never reuse another seat's hints, or an older hand's still-cached hints. */
export function hongzhongHintsAt(round, node, seat, physicalHand, physicalMelds) {
  if (!Number.isInteger(seat) || seat < 0 || seat > 3 || !Array.isArray(physicalHand) || !Array.isArray(physicalMelds)) return null
  const cache = [null, null, null, null]
  for (const tick of (round?.action_ticks || []).slice(0, Math.max(0, node))) {
    if (tick?.[0] !== 'hongzhong' || tick[1] !== 'hints' || !Number.isInteger(tick[2]) || tick[2] < 0 || tick[2] > 3) continue
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
export function hongzhongKnownTiles(snapshot, toPhysical) {
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
export function hongzhongWaitDataAt(round, node, seat, hand, melds, knownTiles) {
  const hints = hongzhongHintsAt(round, node, seat, hand, melds)
  if (!hints) return null
  const details = (waits, scores) => [...new Set(Array.isArray(waits) ? waits : [])]
    .sort((a, b) => a - b).map(tile => {
      const score = scores?.[tile]
      const scored = Number.isInteger(score?.fan) && score.fan >= 0 && score.fan <= 4
        && score.base_score === 2 ** score.fan
      const blocked = hints.win_blocked === true
      return { tile, base_f: 0, selfdrawn_f: blocked ? 0 : scored ? score.fan : 0,
        label: blocked ? '未满足' : scored ? `${score.fan}番\n自摸${score.base_score}分` : '仅自摸',
        kind: blocked ? 'wuyi' : 'zimo',
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

export function parseHongzhongFan(value) {
  const fields = String(value || '').split('|')
  if (fields.length !== 4 || fields[0] !== 'HZ' || !/^\d+$/.test(fields[2])) return null
  return { name: fields[3], value: `${Number(fields[2])}番` }
}

export function hongzhongTileName(tile) {
  const number = Number(tile)
  if (number === 45) return '红中'
  const suit = Math.floor(number / 10)
  const rank = number % 10
  return suit >= 1 && suit <= 3 && rank >= 1 && rank <= 9 ? `${rank}${({ 1: '万', 2: '筒', 3: '条' })[suit]}` : String(tile)
}

/** Current witnesses are [hand index, physical tile, logical tile]. */
export function hongzhongSubstitutionName(item) {
  if (Array.isArray(item)) {
    // Legacy replay explanations stored just [physical tile, logical tile].
    if (item.length !== 2 && item.length !== 3) return ''
    return item.slice(-2).map(hongzhongTileName).join('→')
  }
  if (item && typeof item === 'object') {
    return `${hongzhongTileName(item.physical_tile ?? item.physical ?? 45)}→${hongzhongTileName(item.logical_tile ?? item.logical ?? item.tile)}`
  }
  return item == null ? '' : `红中→${hongzhongTileName(item)}`
}
