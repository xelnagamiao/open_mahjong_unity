import { isExchangeSix, exchangeSixInfoAt } from './changshaExchangeSixReplay.js'
import exchangeOptions from '../../../shared/changshaExchangeSixOptions.json' with { type: 'json' }
const wins = new Set(['hu_self', 'hu_first', 'hu_second', 'hu_third'])
const flag = value => value === true || value === 1 || value === '1' || value === 'T'

export function isChangshaRecord(detail) {
  return (detail?.rule || detail?.record?.game_title?.rule) === 'changsha'
}

export function isChangshaPublicDraw(tick) {
  return tick?.[0] === 'gd' && tick[3] === 'changsha_open_kong' && Array.isArray(tick[5])
}

// Older records wrote a batch as consecutive ordinary gd ticks without metadata.
export function isChangshaBatchDraw(ticks, index) {
  return isChangshaPublicDraw(ticks[index]) || (ticks[index]?.[0] === 'gd'
    && (ticks[index - 1]?.[0] === 'gd' || ticks[index + 1]?.[0] === 'gd'))
}

export function changshaRon(tick) {
  if (!wins.has(tick?.[0]) || tick[0] === 'hu_self' || tick.length < 9
    || ![0, 1, '0', '1', false, true].includes(tick[6])) return null
  return { tile: Number(tick[5]), multi: flag(tick[6]), discarder: Number(tick[7]), recycle: flag(tick[8]) }
}

export function changshaBirds(tick) {
  if (!wins.has(tick?.[0])) return []
  const value = tick[changshaRon(tick) ? 9 : 6]
  return Array.isArray(value) ? value.map(Number) : []
}

export function changshaWinningHand(tick) {
  return tick?.[0] === 'hu_self' && tick[7] === 'changsha_open_kong_win' && Array.isArray(tick[8])
    ? tick[8].map(Number) : null
}

// Match declarations, not draw groups: a short wall may yield an empty batch.
export function changshaKongDeclarations(round) {
  const result = new Map(), batches = round?.changsha_open_kong_batches || [], ticks = round?.action_ticks || []
  let batch = 0
  for (let i = 0; i < ticks.length && batch < batches.length; i++) {
    if (!['ag', 'jg', 'g'].includes(ticks[i]?.[0])) continue
    if (isChangshaPublicDraw(ticks[i + 1]) || (ticks[i + 1]?.[0] !== 'gd' && batches[batch].tiles?.length === 0)) {
      result.set(i, batches[batch++])
    }
  }
  return result
}

/** Track physical indices, including a half stack left by a dice-selected draw. Birds are revealed in place. */
export function changshaWallAt(round, requestedNode) {
  const original = round?.tiles_list || []
  const remaining = original.map((_, index) => index)
  const consumed = new Set()
  let secondFromBack = true
  for (const tick of (round?.action_ticks || []).slice(0, Math.max(0, requestedNode))) {
    let position
    if (tick?.[0] === 'changsha_open_kong') {
      const positions = tick[3].map(Number)
      if (positions.some(i => !Number.isInteger(i) || i < 0 || i >= remaining.length) || new Set(positions).size !== positions.length) throw new Error('长沙换六张开杠取牌位置无效')
      if (secondFromBack && positions.includes(remaining.length - 2)) secondFromBack = false
      for (const i of [...positions].sort((a,b) => b-a)) consumed.add(remaining.splice(i,1)[0])
      continue
    }
    if (isChangshaPublicDraw(tick)) {
      position = Number(tick[4])
      if (!Number.isInteger(position) || position < 0 || position >= remaining.length)
        throw new Error('长沙开杠牌谱的取牌位置无效')
      if (secondFromBack && position === remaining.length - 2) secondFromBack = false
    } else if (tick?.[0] === 'd' || tick?.[0] === 'changsha_sea_draw' || (tick?.[0] === 'changsha_hidden_draw' && tick[2] === 'normal')) position = 0
    else if (tick?.[0] === 'gd' || tick?.[0] === 'bd' || tick?.[0] === 'changsha_hidden_draw') {
      position = secondFromBack && remaining.length > 1 ? remaining.length - 2 : remaining.length - 1
      secondFromBack = !secondFromBack
    } else continue
    if (remaining.length) consumed.add(remaining.splice(position, 1)[0])
  }
  return {
    remaining: remaining.map(index => original[index]),
    wall: original.map((tile, index) => ({ tile, consumed: consumed.has(index) })),
  }
}

export function changshaInfoAt(round, node, title = {}) {
  const ticks = (round?.action_ticks || []).slice(0, Math.max(0, node))
  let batch = null, result = null
  for (const [index, value] of changshaKongDeclarations(round)) if (index < node) batch = value
  for (const tick of ticks) {
    if (tick?.[0] === 'changsha_open_kong') batch = { sequence: ticks.filter(t => t[0] === 'changsha_open_kong').indexOf(tick) + 1, player: tick[1], dice: tick[2], positions: tick[3], tiles: tick[4] }
    if (wins.has(tick?.[0]) && !tick[3]?.includes('错和')) result = tick
  }
  const exchange = isExchangeSix(title) ? exchangeSixInfoAt(round, node) : null
  const config = { ...title, ...title.detailed_config, ...(exchange ? exchangeOptions : {}) }
  return {
    batch,
    exchange,
    birds: exchange ? exchange.birds : changshaBirds(result),
    kamo: config.small_hu_self_draw_only === true,
    capType: config.score_cap_type || 'none',
    capValue: Number(config.score_cap_value) || 0,
    openKongCount: Number(config.open_kong_replacement_count) === 4 ? 4 : 2,
  }
}
