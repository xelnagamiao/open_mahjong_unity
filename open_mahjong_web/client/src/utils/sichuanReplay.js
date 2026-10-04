/** Sichuan family and ordinary continuous-win replay state helpers. */
export function isSichuanRecord(detail) {
  return detail?.rule === 'sichuan' || detail?.record?.game_title?.rule === 'sichuan'
}

export function isSichuanBloodFlow(detail) {
  const subRule = detail?.record?.game_title?.sub_rule ?? detail?.sub_rule
  return isSichuanRecord(detail) && ['sichuan/xueliu', 'sichuan/xueliu_exchange'].includes(subRule)
}

export function isSichuanBloodBattle(detail) {
  if (!isSichuanRecord(detail) || isSichuanBloodFlow(detail)) return false
  const value = detail?.record?.game_title?.blood_battle
  if (value == null) return true
  return value === true || value === 1 || ['true', 't', '1'].includes(String(value).toLowerCase())
}

export function nextSichuanPlayer(from, retired) {
  for (let offset = 1; offset <= 4; offset++) {
    const next = (from + offset) % 4
    if (!retired.has(next)) return next
  }
  return from
}
