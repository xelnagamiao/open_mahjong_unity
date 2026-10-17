// Ordinary hand-shape hints, matching the server's Changsha_Hepai_Check.
// Settlement uses the recorded result; birds, dealer payments and caps are not guessed here.
const tiles = Array.from({ length: 27 }, (_, i) => (Math.floor(i / 9) + 1) * 10 + i % 9 + 1)
const jiang = tile => [2, 5, 8].includes(tile % 10)
export function changshaMeldTiles(code) {
  const tile = Number(code.slice(1))
  return /^[sS]/.test(code) ? [tile - 1, tile, tile + 1] : Array(/^[gG]/.test(code) ? 4 : 3).fill(tile)
}
function sets(counts, remaining, tripletsOnly) {
  if (remaining === 0) return counts.every(n => n === 0)
  const tile = counts.findIndex(n => n > 0)
  if (tile < 0) return false
  if (counts[tile] >= 3) {
    counts[tile] -= 3
    const success = sets(counts, remaining - 1, tripletsOnly)
    counts[tile] += 3
    if (success) return true
  }
  if (!tripletsOnly && tile % 10 <= 7 && counts[tile + 1] && counts[tile + 2]) {
    counts[tile]--; counts[tile + 1]--; counts[tile + 2]--
    const success = sets(counts, remaining - 1, false)
    counts[tile]++; counts[tile + 1]++; counts[tile + 2]++
    if (success) return true
  }
  return false
}
export function changshaShapeFans(hand, melds = [], exchange = null) {
  if (hand.length !== 14 - melds.length * 3 || melds.some(code => !/^[sSkgG][1-3][1-9]$/.test(code))) return []
  const all = [...hand, ...melds.flatMap(changshaMeldTiles)], counts = Array(40).fill(0)
  if (all.some(tile => !tiles.includes(tile) || all.filter(t => t === tile).length > 4)) return []
  for (const tile of hand) counts[tile]++
  let standard = false, small = false, triplets = false
  for (const pair of tiles.filter(tile => counts[tile] >= 2)) {
    counts[pair] -= 2
    const shape = sets(counts, 4 - melds.length, false)
    standard ||= shape
    small ||= shape && jiang(pair)
    triplets ||= !melds.some(code => /^[sS]/.test(code)) && sets(counts, 4 - melds.length, true)
    counts[pair] += 2
  }
  const pairs = melds.length === 0 && counts.every(n => n % 2 === 0)
  const allJiang = all.every(jiang), shape = standard || pairs || allJiang, fans = []
  if (shape && triplets) fans.push('碰碰胡')
  if (allJiang) fans.push('将将胡')
  if ((exchange ? standard || pairs : shape) && new Set(all.map(tile => Math.floor(tile / 10))).size === 1) fans.push('清一色')
  if (standard && melds.length === 4 && (!exchange || melds.every(m => /^[skg]/.test(m)))) fans.push('全求人')
  if (pairs) fans.push(['七小对','豪华七小对','双豪华七小对','三豪华七小对'][counts.filter(n => n === 4).length])
  if (exchange) {
    const terminals = [11, 19, 21, 29, 31, 39]
    if (!melds.length && terminals.every(t => counts[t] === 1)) {
      const rest = hand.filter(t => !terminals.includes(t)), distinct = new Set(rest).size
      if (rest.every(jiang) && [7, 8].includes(distinct)) fans.push(distinct === 7 ? '十三将' : '无对十三将')
    }
    if (all.every(t => terminals.includes(t))) fans.push('全幺九')
  }
  return fans.length ? fans : small || (exchange?.ready && standard) ? ['小胡'] : []
}
export function changshaWaits(hand, melds = [], kamo = false) {
  if (hand.length !== 13 - melds.length * 3) return []
  return tiles.flatMap(tile => {
    const fans = changshaShapeFans([...hand, tile], melds)
    return fans.length ? [{ tile, fans, canRon: !kamo || fans.some(fan => fan !== '小胡') }] : []
  })
}
export function exchangeSixWaits(hand, melds, info, seat) {
  if (info.disqualified.includes(seat) || hand.length !== 13 - melds.length * 3) return []
  return tiles.flatMap(tile => {
    let fans = changshaShapeFans([...hand, tile], melds, { ready: info.ready.includes(seat) })
    if (!fans.length) return []
    if (info.locks[seat]) {
      if (!(tile in info.locks[seat])) return []
      fans = info.locks[seat][tile].length ? [...info.locks[seat][tile]] : ['小胡']
    }
    if (info.ready.includes(seat)) fans = [...fans.filter(f => f !== '小胡'), '报听']
    if (info.open.includes(seat)) fans = [...fans.filter(f => f !== '小胡'), '明牌']
    const weights = { 豪华七小对: 2, 双豪华七小对: 3, 三豪华七小对: 4, 无对十三将: 2, 全幺九: 2 }
    const weight = fans.reduce((n, f) => n + (f === '小胡' ? 0 : weights[f] || 1), 0)
    const blocked = info.forbidden === seat && weight === 0
    return [{ tile, fans, canRon: !blocked, blocked, score: weight ? 8 * 2 ** (weight - 1) : 2 }]
  })
}
export function buildChangshaWaitData(hand, melds, kamo, knownTiles, exchange = null, seat = 0) {
  const details = waitingHand => (exchange ? exchangeSixWaits(waitingHand, melds, exchange, seat) : changshaWaits(waitingHand, melds, kamo)).map(wait => ({
    tile: wait.tile, base_f: 0, selfdrawn_f: 0,
    kind: wait.canRon ? 'dianhe' : 'zimo',
    label: exchange ? `${wait.score}分${wait.blocked ? '\n禁小胡，须升级' : ''}` : `${wait.fans.includes('小胡') ? '小胡' : '大胡'}${wait.canRon ? '' : '\n仅自摸'}`,
    remaining_count: Math.max(0, 4 - knownTiles.filter(tile => tile === wait.tile).length),
  }))
  if (hand.length % 3 === 2) {
    const rows = [...new Set(hand)].flatMap(discard => {
      const rest = [...hand]; rest.splice(rest.indexOf(discard), 1)
      const adds = details(rest)
      return adds.length ? [{ discard_tile: discard, adds }] : []
    })
    return rows.length ? { type: 'waits_all', details: rows } : null
  }
  const waits = details(hand)
  return waits.length ? { type: 'waits', details: waits } : null
}
