/** Changchun base-hand hints. Passed wins and incidental win methods are intentionally not inferred. */
import type { WaitInfoData, WaitDetail } from '../../game/scene/WaitDisplay'

const TILES = [1, 2, 3].flatMap(s => Array.from({ length: 9 }, (_, n) => s * 10 + n + 1)).concat(Array.from({ length: 7 }, (_, n) => 41 + n))
const DOMAINS: Record<string, number[]> = { yao: [11, 21, 31], jiu: [19, 29, 39], wind: [41, 42, 43, 44], dragon: [45, 46, 47] }
type Shape = { pair: number; sequences: number[]; pungs: number; seven: boolean }
const count = (tiles: number[], tile: number) => tiles.filter(t => t === tile).length

export function meldTiles(code: string, logical = false): number[] | null {
  if (typeof code !== 'string') return null
  if (code.startsWith('C')) {
    const [kind, physicalText, logicalText, extra] = code.split(':')
    const domain = DOMAINS[kind.slice(1)]
    if (!domain || extra !== undefined || !physicalText || !logicalText) return null
    const physical = physicalText.split(',').map(Number), declared = logicalText.split(',').map(Number)
    if (physical.length < 3 || physical.length !== declared.length || new Set(declared.slice(0, 3)).size !== 3
      || declared.some(t => !domain.includes(t)) || physical.some(t => !TILES.includes(t))
      || physical.some((t, i) => t !== 31 && t !== declared[i]) || physical.some(t => count(physical, t) > 4)) return null
    return logical ? declared : physical
  }
  if (!/^[skgG]\d{2}$/.test(code)) return null
  const tile = Number(code.slice(1)), prefix = code[0]
  if (!TILES.includes(tile) || (prefix === 's' && (tile >= 40 || tile % 10 < 2 || tile % 10 > 8))) return null
  return prefix === 's' ? [tile - 1, tile, tile + 1] : Array(prefix === 'k' ? 3 : 4).fill(tile)
}

function shapes(hand: number[], codes: string[], requireConditions: boolean): Shape[] {
  if (codes.length > 4 || hand.length + codes.length * 3 !== 14 || hand.some(t => !TILES.includes(t))) return []
  const special = codes.filter(c => c?.startsWith('C')).map(c => c.split(':')[0])
  if (new Set(special).size !== special.length) return []
  const physical = [...hand], logical = [...hand]
  for (const code of codes) {
    const p = meldTiles(code), l = meldTiles(code, true)
    if (!p || !l) return []
    physical.push(...p); logical.push(...l)
  }
  if (physical.some(t => count(physical, t) > 4)) return []
  if (requireConditions && (new Set(logical.filter(t => t < 40).map(t => Math.floor(t / 10))).size !== 3
    || !logical.some(t => t >= 41 || t % 10 === 1 || t % 10 === 9))) return []
  const result: Shape[] = []
  if (!codes.length && [...new Set(hand)].every(t => count(hand, t) % 2 === 0)) result.push({ pair: 0, sequences: [], pungs: 0, seven: true })
  const externalPung = codes.some(c => c[0] !== 's')
  function split(rest: number[], pair: number, sequences: number[], pungs: number) {
    if (!rest.length) {
      if (!requireConditions || externalPung || pungs > 0 || pair >= 45) result.push({ pair, sequences, pungs, seven: false })
      return
    }
    const tile = rest[0]
    if (count(rest, tile) >= 3) {
      const next = [...rest]
      for (let i = 0; i < 3; i++) next.splice(next.indexOf(tile), 1)
      split(next, pair, sequences, pungs + 1)
    }
    if (tile < 40 && tile % 10 <= 7 && rest.includes(tile + 1) && rest.includes(tile + 2)) {
      const next = [...rest]
      for (const t of [tile, tile + 1, tile + 2]) next.splice(next.indexOf(t), 1)
      split(next, pair, [...sequences, tile + 1], pungs)
    }
  }
  for (const pair of new Set(hand)) {
    if (count(hand, pair) < 2) continue
    const rest = [...hand].sort((a, b) => a - b)
    rest.splice(rest.indexOf(pair), 1); rest.splice(rest.indexOf(pair), 1)
    split(rest, pair, [], 0)
  }
  return result
}

const cache = new Map<string, number[]>()
function waits(hand: number[], codes: string[], requireConditions: boolean): number[] {
  if (hand.length + codes.length * 3 !== 13) return []
  const key = `${requireConditions ? 'Q' : 'B'}|${[...hand].sort((a, b) => a - b)}|${codes.join('|')}`
  const cached = cache.get(key)
  if (cached) return [...cached]
  const result = TILES.filter(t => shapes([...hand, t], codes, requireConditions).length > 0)
  if (cache.size >= 2048) cache.clear()
  cache.set(key, result)
  return [...result]
}

export const basicWaits = (hand: number[], codes: string[] = []) => waits(hand, codes, false)
export const qualifiedWaits = (hand: number[], codes: string[] = []) => waits(hand, codes, true)
export const isQualified = (hand: number[], codes: string[] = []) => shapes(hand, codes, true).length > 0

export function basicFan(hand: number[], codes: string[], win: number): number {
  if (!hand.includes(win)) return 0
  const before = [...hand]; before.splice(before.indexOf(win), 1)
  // The narrow-wait fan uses legal waits, not the additional unmet hint tiles.
  const single = qualifiedWaits(before, codes).length === 1
  let best = 0
  for (const shape of shapes(hand, codes, true)) {
    let fan = 0
    if (shape.seven) fan = count(before, win) === 3 ? 4 : 3
    else {
      if (codes.every(c => c[0] === 'G')) fan++
      if (!shape.sequences.length && codes.every(c => c[0] !== 's')) fan += 2
      else if (single || shape.pair === win || shape.sequences.some(m => m === win || (m % 10 === 2 && win === m + 1) || (m % 10 === 8 && win === m - 1))) fan++
    }
    best = Math.max(best, fan)
  }
  return best
}

export interface ChangchunWaitContext {
  hand: number[]
  combinations: string[]
  playerIndex: number
  seatDiscards: number[][]
  seatCombinations: string[][]
  knownTiles?: number[]
}

export function buildChangchunWaitData(ctx: ChangchunWaitContext, includeDiscards = false): WaitInfoData {
  function details(hand: number[], pendingCut?: number): WaitDetail[] {
    const known = [...hand, ...(ctx.knownTiles ?? []), ...(pendingCut === undefined ? [] : [pendingCut]), ...ctx.seatDiscards.flat()]
    ctx.seatCombinations.forEach((codes, seat) => {
      for (const code of codes) {
        if (seat !== ctx.playerIndex && code.startsWith('G')) continue
        known.push(...(meldTiles(code) ?? []))
      }
    })
    return basicWaits(hand, ctx.combinations).map(tile => {
      const complete = [...hand, tile], qualified = isQualified(complete, ctx.combinations)
      const fan = qualified ? basicFan(complete, ctx.combinations, tile) : 0
      return { tile, base_f: fan, selfdrawn_f: fan, remaining_count: Math.max(0, 4 - count(known, tile)),
        label: qualified ? `${fan}番` : '未满足', kind: qualified ? 'dianhe' as const : 'wuyi' as const }
    })
  }
  const total = ctx.hand.length + ctx.combinations.length * 3
  if (total === 13) {
    const rows = details(ctx.hand)
    return rows.length ? { type: 'waits', details: rows } : null
  }
  if (!includeDiscards || total !== 14) return null
  const rows = [...new Set(ctx.hand)].sort((a, b) => a - b).map(tile => {
    const hand = [...ctx.hand]; hand.splice(hand.indexOf(tile), 1)
    return { discard_tile: tile, adds: details(hand, tile) }
  }).filter(row => row.adds.length)
  return rows.length ? { type: 'waits_all', details: rows } : null
}
