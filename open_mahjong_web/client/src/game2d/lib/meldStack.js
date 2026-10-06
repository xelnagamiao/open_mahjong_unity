/** 备用显示 sign：101 明面叠牌，102 暗面叠牌；现行行牌规则不生成它们。 */
export const MELD_STACK_FACE_UP = 101
export const MELD_STACK_FACE_DOWN = 102

export function isMeldStack(sign) {
  return sign === MELD_STACK_FACE_UP || sign === MELD_STACK_FACE_DOWN
}

export function hasMeldStacks(mask) {
  if (!Array.isArray(mask)) return false
  for (let index = 0; index + 1 < mask.length; index += 2)
    if (isMeldStack(Number(mask[index]))) return true
  return false
}

/** 按原始 pair 顺序挂在最近的 0/1/2/3 牌上；4 忽略，未知 sign 阻断归属。 */
export function buildMeldStacks(mask) {
  const stacks = []
  if (!Array.isArray(mask)) return stacks
  let anchorIndex = -1, layer = 0
  for (let index = 0; index + 1 < mask.length; index += 2) {
    const sign = Number(mask[index]), tile = Number(mask[index + 1])
    if (isMeldStack(sign)) {
      if (anchorIndex >= 0 && Number.isInteger(tile) && (tile === 0 || tile > 10))
        stacks.push({ pairIndex: index / 2, anchorIndex, layer: ++layer,
          sign, tile, faceDown: sign === MELD_STACK_FACE_DOWN })
    } else if (Number.isInteger(sign) && sign >= 0 && sign <= 3) {
      anchorIndex = Number.isInteger(tile) && (tile === 0 || tile > 10) ? index / 2 : -1
      layer = 0
    } else if (sign !== 4) {
      anchorIndex = -1
      layer = 0
    }
  }
  return stacks
}
