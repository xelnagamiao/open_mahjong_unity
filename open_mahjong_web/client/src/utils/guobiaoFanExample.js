// 番表与 Unity 共用 m/p/s/z/f 牌例；这里的 z 顺序为东南西北中发白。
export function parseFanExample(example) {
  return example.trim().split(/\s+/).map((group) => {
    if (!/^[1-9]+[mpszf]$/.test(group)) throw new Error(`无效番表牌例：${group}`)
    const suit = group.at(-1)
    return [...group.slice(0, -1)].map((digit) => {
      const value = Number(digit)
      if (suit === 'z') {
        if (value > 7) throw new Error(`无效字牌：${group}`)
        return [0, 41, 42, 43, 44, 45, 47, 46][value]
      }
      if (suit === 'f' && value > 8) throw new Error(`无效花牌：${group}`)
      return { m: 10, p: 20, s: 30, f: 50 }[suit] + value
    })
  })
}
/** Display-only orientation: keep the original tile IDs intact for notation and scoring. */
export function fanExampleFaceDown(entry, groups, groupIndex, tileIndex) {
  const offset = groups.slice(0, groupIndex).reduce((sum, group) => sum + group.length, 0)
  return (entry.faceDownTiles || []).includes(offset + tileIndex)
}
