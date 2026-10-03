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
  return same(hints.source_hand_tiles, physicalHand) && same(hints.source_melds, physicalMelds) ? hints : null
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
