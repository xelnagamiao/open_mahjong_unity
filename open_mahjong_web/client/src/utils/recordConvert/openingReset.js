/** 国标开局补花轮结束后插入 ['reset', startPlayerIndex]。已有则跳过。 */
export function insertOpeningReset(ticks, startPlayerIndex = 0) {
  if (!Array.isArray(ticks)) return false
  const start = ((Number(startPlayerIndex) % 4) + 4) % 4
  let index = 0
  while (index < ticks.length) {
    const tick = ticks[index]
    if (!Array.isArray(tick) || tick.length === 0) {
      index += 1
      continue
    }
    if (tick[0] === 'bh' || tick[0] === 'bd') {
      index += 1
      continue
    }
    break
  }
  if (Array.isArray(ticks[index]) && ticks[index][0] === 'reset') return false
  ticks.splice(index, 0, ['reset', start])
  return true
}
