// MIL 2023 uses additive points, including the actual winning tile's value.
const PATTERN_POINTS = Object.freeze({
  '平和': 0, '一条龙': 20, '清一色': 20,
  '七对': 20, '豪华七对': 40, '十三幺': 60,
})

export function parseShanxiFan(name) {
  const tile = /^和牌张（([3-9]|10)分）$/.exec(name)
  if (tile) return { name: '和牌张', value: `${tile[1]}分` }
  return { name, value: Object.hasOwn(PATTERN_POINTS, name) ? `${PATTERN_POINTS[name]}分` : '' }
}
