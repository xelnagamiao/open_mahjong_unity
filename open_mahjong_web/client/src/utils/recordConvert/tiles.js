/**
 * 各平台牌面编码互转（国标 / 日麻共用基础）。
 *
 * salasasa:
 *   万 11-19, 饼 21-29, 条 31-39, 风 41-44, 中45 白46 发47, 花 51-58
 *   赤宝 105/205/305
 *
 * 雀渣 (tziakcha) 0-143:
 *   万0-35, 条36-71, 饼72-107, 风108-123, 中124-127, 发128-131, 白132-135, 花136-143
 *
 * Botzone: W1-9 B1-9 T1-9 F1-4 J1-3 H1-8
 *
 * MJAI: 1-9m/p/s, E/S/W/N, P/F/C(白发中), 赤 5mr/5pr/5sr
 */

export const S2O = [
  [0, 1, 2, 3],
  [1, 2, 3, 0],
  [2, 3, 0, 1],
  [3, 0, 1, 2],
  [1, 0, 3, 2],
  [0, 3, 2, 1],
  [3, 2, 1, 0],
  [2, 1, 0, 3],
  [2, 3, 1, 0],
  [3, 1, 0, 2],
  [1, 0, 2, 3],
  [0, 2, 3, 1],
  [3, 2, 0, 1],
  [2, 0, 1, 3],
  [0, 1, 3, 2],
  [1, 3, 2, 0]
]

export function seatsFromRoundIndex(roundI) {
  const mapping = S2O[roundI % S2O.length]
  const seats = [0, 0, 0, 0]
  mapping.forEach((original, playerIndex) => {
    seats[original] = playerIndex
  })
  return seats
}

/** 雀渣 id → salasasa */
export function tzToSalasasa(tid) {
  if (tid < 0 || tid > 143) throw new Error(`非法雀渣牌 id: ${tid}`)
  if (tid < 36) return 11 + (tid >> 2)
  if (tid < 72) return 31 + ((tid - 36) >> 2)
  if (tid < 108) return 21 + ((tid - 72) >> 2)
  if (tid < 124) return 41 + ((tid - 108) >> 2)
  if (tid < 128) return 45 // 中
  if (tid < 132) return 47 // 发
  if (tid < 136) return 46 // 白
  return 51 + (tid - 136)
}

/**
 * salasasa → 雀渣唯一实例 id。
 * pool: Map<salasasaNorm, nextInstance 0..3>
 */
export function salasasaToTz(tile, pool = new Map()) {
  let base
  if (tile >= 51 && tile <= 58) {
    return 136 + (tile - 51) // 花唯一
  }
  if (tile === 105) tile = 15
  if (tile === 205) tile = 25
  if (tile === 305) tile = 35

  if (tile >= 11 && tile <= 19) base = (tile - 11) << 2
  else if (tile >= 31 && tile <= 39) base = 36 + ((tile - 31) << 2)
  else if (tile >= 21 && tile <= 29) base = 72 + ((tile - 21) << 2)
  else if (tile >= 41 && tile <= 44) base = 108 + ((tile - 41) << 2)
  else if (tile === 45) base = 124 // 中
  else if (tile === 47) base = 128 // 发
  else if (tile === 46) base = 132 // 白
  else throw new Error(`无法映射到雀渣: ${tile}`)

  const used = pool.get(base) || 0
  pool.set(base, used + 1)
  // 超过 4 张时循环复用实例（反向导出近似；正向雀渣→salasasa 不受影响）
  return base + (used % 4)
}

/** salasasa → Botzone 字符串 */
export function salasasaToBotzone(tile) {
  const n = normalizeAka(tile)
  if (n >= 11 && n <= 19) return `W${n - 10}`
  if (n >= 21 && n <= 29) return `B${n - 20}`
  if (n >= 31 && n <= 39) return `T${n - 30}`
  if (n >= 41 && n <= 44) return `F${n - 40}`
  if (n === 45) return 'J1'
  if (n === 47) return 'J2'
  if (n === 46) return 'J3'
  if (n >= 51 && n <= 58) return `H${n - 50}`
  throw new Error(`Botzone 无法映射: ${tile}`)
}

/** Botzone → salasasa（无赤宝区分） */
export function botzoneToSalasasa(code) {
  const s = String(code).trim().toUpperCase()
  const m = s.match(/^([WBTJFHwbjfth])(\d+)$/)
  if (!m) throw new Error(`非法 Botzone 牌: ${code}`)
  const kind = m[1].toUpperCase()
  const num = Number(m[2])
  if (kind === 'W') return 10 + num
  if (kind === 'B') return 20 + num
  if (kind === 'T') return 30 + num
  if (kind === 'F') return 40 + num
  if (kind === 'J') {
    if (num === 1) return 45
    if (num === 2) return 47
    if (num === 3) return 46
  }
  if (kind === 'H') return 50 + num
  throw new Error(`非法 Botzone 牌: ${code}`)
}

/** salasasa → MJAI 牌字符串 */
export function salasasaToMjai(tile) {
  if (tile === 105) return '5mr'
  if (tile === 205) return '5pr'
  if (tile === 305) return '5sr'
  if (tile >= 11 && tile <= 19) return `${tile - 10}m`
  if (tile >= 21 && tile <= 29) return `${tile - 20}p`
  if (tile >= 31 && tile <= 39) return `${tile - 30}s`
  if (tile === 41) return 'E'
  if (tile === 42) return 'S'
  if (tile === 43) return 'W'
  if (tile === 44) return 'N'
  if (tile === 46) return 'P' // 白
  if (tile === 47) return 'F' // 发
  if (tile === 45) return 'C' // 中
  throw new Error(`MJAI 无法映射: ${tile}`)
}

/** MJAI → salasasa */
export function mjaiToSalasasa(pai) {
  const s = String(pai).trim()
  if (s === '?' || s === '') return null
  if (s === '5mr') return 105
  if (s === '5pr') return 205
  if (s === '5sr') return 305
  if (s === 'E') return 41
  if (s === 'S') return 42
  if (s === 'W') return 43
  if (s === 'N') return 44
  if (s === 'P') return 46
  if (s === 'F') return 47
  if (s === 'C') return 45
  const m = s.match(/^([1-9])([mps])$/)
  if (!m) throw new Error(`非法 MJAI 牌: ${pai}`)
  const n = Number(m[1])
  if (m[2] === 'm') return 10 + n
  if (m[2] === 'p') return 20 + n
  return 30 + n
}

export function normalizeAka(tile) {
  if (tile === 105) return 15
  if (tile === 205) return 25
  if (tile === 305) return 35
  return tile
}

export function huClassFromRelative(winner, discarder) {
  if (discarder == null || discarder === winner) return 'hu_self'
  const delta = (discarder - winner + 4) % 4
  if (delta === 3) return 'hu_first'
  if (delta === 2) return 'hu_second'
  if (delta === 1) return 'hu_third'
  return 'hu_self'
}

/** 从 hu_class 反推点炮者 */
export function discarderFromHuClass(winner, huClass) {
  if (!huClass || huClass === 'hu_self') return null
  if (huClass === 'hu_first') return (winner + 3) % 4
  if (huClass === 'hu_second') return (winner + 2) % 4
  if (huClass === 'hu_third') return (winner + 1) % 4
  return null
}

export function prettyJson(obj) {
  return JSON.stringify(obj, null, 2)
}

export function parseJsonInput(text) {
  const t = String(text || '').trim()
  if (!t) throw new Error('输入为空')
  // NDJSON：多行 JSON
  if (t.includes('\n') && t.trimStart().startsWith('{') && !t.trimStart().startsWith('{\n  "game_')) {
    const lines = t.split(/\r?\n/).map((l) => l.trim()).filter(Boolean)
    if (lines.length > 1 && lines.every((l) => l.startsWith('{'))) {
      return lines.map((l) => JSON.parse(l))
    }
  }
  return JSON.parse(t)
}
