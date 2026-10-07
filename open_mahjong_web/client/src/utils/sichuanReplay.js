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

/** 分变按当局座位排列；final 是绝对分数，不参与累计。 */
export function sichuanScoreChanges(tick) {
  if (!Array.isArray(tick)) return null
  const action = tick[0]
  let values
  if (['g', 'ag', 'jg', 'gr'].includes(action)) {
    const index = tick.lastIndexOf('gs')
    if (index >= 0) values = tick.slice(index + 1, index + 5)
  } else if (['liuju', 'blood'].includes(action)) {
    const index = { settle_hu: 6, chajiao: 5, cha_refund: 2 }[tick[1]]
    if (index != null) values = tick[index]
  }
  if (typeof values === 'string') {
    try { values = JSON.parse(values) } catch { return null }
  }
  if (!Array.isArray(values) || values.length !== 4) return null
  const changes = values.map(Number)
  return changes.every(Number.isFinite) ? changes : null
}
