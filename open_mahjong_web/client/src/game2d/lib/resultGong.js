import { hasSingleFanAtLeast, parseFanMultiplier } from '../../constants/guessFanCatalog.js'

/** 国标和牌累计番数扣除花牌后达到 64 番时敲锣。 */
export function shouldPlayGuobiaoGong(fanNames, totalFan) {
  if (!Array.isArray(fanNames) || !Number.isFinite(totalFan)) return false
  const flowerFan = fanNames.reduce((sum, label) => {
    const { baseName, multiplier } = parseFanMultiplier(label)
    return sum + (baseName === '花牌' ? multiplier : 0)
  }, 0)
  return totalFan - flowerFan >= 64
}

export function shouldPlayResultGong(fanNames, totalFan, rule = 'guobiao') {
  return rule === 'guobiao' || rule.startsWith('guobiao/')
    ? shouldPlayGuobiaoGong(fanNames, totalFan)
    : hasSingleFanAtLeast(fanNames, 32, 'guobiao')
}
