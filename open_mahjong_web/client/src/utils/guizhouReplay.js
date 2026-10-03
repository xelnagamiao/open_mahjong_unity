export function parseGuizhouFan(value) {
  const parts = String(value || '').split('|')
  if (parts.length !== 4 || parts[0] !== 'GZ' || !Number.isInteger(Number(parts[2]))) return null
  return { id: parts[1], name: parts[3], value: `${Number(parts[2])}分` }
}

export function guizhouInfoAt(round, node) {
  let info = round?.guizhou || null
  for (const tick of (round?.action_ticks || []).slice(0, node)) {
    if (tick[0] !== 'guizhou') continue
    if (tick[1] === 'ledger' || tick[1] === 'state') info = tick[2]
    else if (tick[1] === 'draw_score') info = tick[3]
    else if (tick[1] === 'win_source') info = tick[7]
  }
  return info
}

export function guizhouLedgerRows(info) {
  if (!info?.ledger) return []
  const rows = Array.from({ length: 4 }, (_, seat) => ({
    seat, original: info.seat_to_original?.[seat] ?? seat,
    ready: Boolean(info.ledger.ready?.[seat]), hand: 0, chicken: 0, kong: 0, total: 0,
  }))
  for (const item of info.ledger.transfers || []) {
    const category = item.category === 'ready' ? 'hand' : item.category
    if (!['hand', 'chicken', 'kong'].includes(category)) continue
    if (!rows[item.payer] || !rows[item.payee] || !Number.isInteger(item.points)) continue
    rows[item.payer][category] -= item.points
    rows[item.payee][category] += item.points
    rows[item.payer].total -= item.points
    rows[item.payee].total += item.points
  }
  return rows
}
