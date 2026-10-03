export function isGuangdongMilRecord(detail) {
  return (detail?.sub_rule || detail?.record?.game_title?.sub_rule) === 'guangdong/mil2023'
}

/** Metadata is explanatory. Scores are applied only by hu/kong_score/refund_kongs. */
export function guangdongInfoAt(round, node) {
  const info = { ghost_discard_counts: [0, 0, 0, 0], horses: null, result: null, tips: {}, kongs: [] }
  for (const tick of (round?.action_ticks || []).slice(0, node)) {
    if (tick[0] !== 'guangdong') continue
    if (tick[1] === 'ghost_discard') info.ghost_discard_counts[Number(tick[2])] = Number(tick[3])
    else if (tick[1] === 'horses') info.horses = tick[2]
    else if (tick[1] === 'settlement') { info.result = tick[2]; info.horses = tick[2]?.horses || info.horses }
    else if (tick[1] === 'tips') info.tips[Number(tick[2])] = tick[3]
    else if (tick[1] === 'kong_score') info.kongs.push({ changes: tick[2], kind: tick[3], payer: tick[4], tile: tick[5] })
    else if (tick[1] === 'refund_kongs') info.refund = tick[2]
  }
  return info
}

export function parseGuangdongFan(value) {
  const fields = String(value || '').split('|')
  if (fields.length !== 4 || fields[0] !== 'GD') return null
  return { id: fields[1], name: fields[3], value: /^[×x]/.test(fields[2]) ? fields[2] : `${fields[2]}番` }
}

export function guangdongTileName(tile) {
  const names = { 41: '东', 42: '南', 43: '西', 44: '北', 45: '中', 46: '白', 47: '发', 55: '梅（鬼）', 56: '兰（鬼）', 57: '竹（鬼）', 58: '菊（鬼）' }
  return names[tile] || `${Number(tile) % 10}${({ 1: '万', 2: '筒', 3: '条' })[Math.floor(Number(tile) / 10)] || ''}`
}
