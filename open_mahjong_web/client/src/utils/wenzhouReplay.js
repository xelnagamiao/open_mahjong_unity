export function parseWenzhouFan(label) {
  const fields = String(label || '').split('|')
  return fields.length >= 4 && fields[0] === 'WZ' ? { name: fields[3], value: fields[2].replace(/^x/, '×') } : null
}

function object(value) {
  if (typeof value === 'string') {
    try { value = JSON.parse(value) } catch { return null }
  }
  return value && typeof value === 'object' && !Array.isArray(value) ? value : null
}

/** Uses saved metadata only. Never flips a new indicator or substitutes a tile face. */
export function wenzhouInfoAt(round, node) {
  let info = object(round?.wenzhou)
  for (const tick of (round?.action_ticks || []).slice(0, Math.max(0, node))) {
    if (tick[0] !== 'wenzhou') continue
    if (tick[1] === 'state') info = object(tick[2]) || info
    if (tick[1] === 'win_source') info = object(tick[6]) || info
  }
  return info
}

export function wenzhouTileName(tile) {
  const n = Number(tile)
  if (n >= 11 && n <= 19) return `${n % 10}万`
  if (n >= 21 && n <= 29) return `${n % 10}饼`
  if (n >= 31 && n <= 39) return `${n % 10}条`
  return ({ 41: '东', 42: '南', 43: '西', 44: '北', 45: '中', 46: '白', 47: '发' })[n] || '未翻财'
}

export function wenzhouLedgerRows(info) {
  if (!info?.caishen_counts) return []
  return Array.from({ length: 4 }, (_, seat) => ({
    seat, original: info.seat_to_original?.[seat] ?? seat,
    count: Number(info.caishen_counts[seat]) || 0,
    caishen: Number(info.caishen_changes?.[seat]) || 0,
    total: Number(info.round_changes?.[seat]) || 0,
  }))
}


export function wenzhouLedgerEntries(info) {
  const names = { ming: '明杠', angang: '暗杠', jiagang: '加杠', win: '和牌', caishen: '财神' }
  const entries = (info?.ledger || []).filter(item => Array.isArray(item.changes)).map(item =>
    `${names[item.kind] || item.kind}：${item.changes.map((n, i) => `${['东', '南', '西', '北'][i]}${n > 0 ? '+' : ''}${n}`).join(' ')}`)
  if (info?.next_dealer_dice?.length) entries.push(`连庄上限掷骰：${info.next_dealer_dice.join('、')}；下庄为本局${['东', '南', '西', '北'][info.next_dealer_shift]}家。`)
  return entries
}

export function wenzhouWaitsAt(round, node, seat, hand) {
  let cache = object(round?.wenzhou_waits?.[seat]) || {}
  for (const tick of (round?.action_ticks || []).slice(0, Math.max(0, node)))
    if (tick[0] === 'wenzhou' && tick[1] === 'waits' && Number(tick[2]) === seat) cache = object(tick[3]) || {}
  return cache[[...hand].sort((a, b) => a - b).join(',')] || []
}

/** Replay hints consume server snapshots and count actual visible tiles only. */
export function wenzhouWaitData(round, node, seat, hand, snapshot, toTile) {
  const counts = new Map(), add = tile => counts.set(tile, (counts.get(tile) || 0) + 1)
  hand.forEach(add)
  const info = wenzhouInfoAt(round, node)
  if (info?.indicator) add(Number(info.indicator))
  for (const player of snapshot.seats) {
    player.discard_pile.forEach(tile => add(toTile(tile)))
    for (const meld of player.melds) (meld.physical_mask || []).forEach((tile, i) => { if (i % 2) add(toTile(tile)) })
  }
  const details = tiles => wenzhouWaitsAt(round, node, seat, tiles).map(wait => ({
    tile: wait.tile, base_f: wait.ron ? wait.ron_multiplier : 0,
    selfdrawn_f: wait.self_draw ? wait.self_draw_multiplier : 0, unit: '倍',
    remaining_count: Math.max(0, 4 - (counts.get(wait.tile) || 0)),
  }))
  if (hand.length % 3 === 1) { const waits = details(hand); return waits.length ? { type: 'waits', details: waits } : null }
  if (hand.length % 3 !== 2) return null
  const cuts = [...new Set(hand)].map(tile => {
    const remainder = [...hand]; remainder.splice(remainder.indexOf(tile), 1)
    return { discard_tile: tile, adds: details(remainder) }
  }).filter(item => item.adds.length)
  return cuts.length ? { type: 'waits_all', details: cuts } : null
}
