/**
 * Client-side guobiao wait tips, mirroring Unity TipsContainer + HeJuezhangTableCounter.
 * Output shape matches MMCR WaitDisplay (waits / waits_all).
 */

import {
  tingpaiCheck,
  hepaiCheck,
  hepaiCheckXiaolin,
  hepaiCheckKshen,
  type HepaiResult,
} from './index'

const FLOWERS = new Set([51, 52, 53, 54, 55, 56, 57, 58])

export interface WaitDetail {
  tile: number
  base_f: number
  selfdrawn_f: number
  remaining_count: number
}

export type WaitInfoData =
  | { type: 'waits'; details: WaitDetail[] }
  | { type: 'waits_all'; details: Array<{ discard_tile: number; adds: WaitDetail[] }> }
  | null

export interface WaitTipsContext {
  tips: boolean
  hand: number[]
  combinations: string[]
  flowerCount: number
  playerIndex: number
  currentRound: number
  hepaiLimit: number
  subRule: string
  /** Discard piles for seats 0-3 (Salasasa tile ids). */
  seatDiscards: number[][]
  /** Open meld keys for seats 0-3 (`s37`, `k39`, `g41`, …). */
  seatCombinations: string[][]
}

function scoreHepai(
  subRule: string,
  hand: number[],
  combinations: string[],
  way: string[],
  tile: number,
): number {
  let result: HepaiResult
  if (subRule === 'guobiao/xiaolin') {
    result = hepaiCheckXiaolin(hand, combinations, way, tile, false)
  } else if (subRule === 'guobiao/kshen') {
    result = hepaiCheckKshen(hand, combinations, way, tile, false)
  } else {
    result = hepaiCheck(hand, combinations, way, tile, false)
  }
  return Math.max(0, Number(result.fan) || 0)
}

function buildBaseWay(ctx: WaitTipsContext, waitCount: number): string[] {
  const way: string[] = []
  for (let i = 0; i < ctx.flowerCount; i += 1) way.push('花牌')
  const roundIndex = Math.max(1, Number(ctx.currentRound) || 1)
  if (roundIndex <= 4) way.push('场风东')
  else if (roundIndex <= 8) way.push('场风南')
  else if (roundIndex <= 12) way.push('场风西')
  else way.push('场风北')
  const seat = ((Number(ctx.playerIndex) % 4) + 4) % 4
  way.push(['自风东', '自风南', '自风西', '自风北'][seat])
  if (waitCount === 1) way.push('和单张')
  return way
}

/** C# HeJuezhangTableCounter: river + k/s melds only (no g/G), optional pending cut. */
function countShowTilesOnTable(
  hepaiTile: number,
  seatDiscards: number[][],
  seatCombinations: string[][],
  pendingCut: number | null,
): number {
  let count = 0
  for (const discards of seatDiscards) {
    for (const tile of discards) {
      if (tile === hepaiTile) count += 1
    }
  }
  for (const combinations of seatCombinations) {
    for (const combination of combinations) {
      if (!combination) continue
      if (combination.includes(`k${hepaiTile}`)) count += 3
      if (combination.includes(`s${hepaiTile - 1}`)) count += 1
      if (combination.includes(`s${hepaiTile}`)) count += 1
      if (combination.includes(`s${hepaiTile + 1}`)) count += 1
    }
  }
  if (pendingCut != null && pendingCut === hepaiTile) count += 1
  return count
}

function expandMeldTiles(target: string): number[] {
  if (!target || target.length < 2) return []
  const tile = Number(target.slice(1))
  if (!Number.isFinite(tile)) return []
  const prefix = target[0]
  if (prefix.toLowerCase() === 's') return [tile - 1, tile, tile + 1]
  if (prefix === 'k') return [tile, tile, tile]
  if (prefix === 'g' || prefix === 'G') return [tile, tile, tile, tile]
  if (prefix.toLowerCase() === 'q') return [tile, tile]
  return []
}

function remainingCount(
  hand: number[],
  seatDiscards: number[][],
  seatCombinations: string[][],
  tile: number,
  pendingCut: number | null,
): number {
  let used = 0
  for (const value of hand) {
    if (value === tile) used += 1
  }
  if (pendingCut != null && pendingCut === tile) used += 1
  for (const discards of seatDiscards) {
    for (const value of discards) {
      if (value === tile) used += 1
    }
  }
  for (const combinations of seatCombinations) {
    for (const combination of combinations) {
      for (const value of expandMeldTiles(combination)) {
        if (value === tile) used += 1
      }
    }
  }
  return Math.max(0, 4 - used)
}

function detailsForHand(
  ctx: WaitTipsContext,
  hand: number[],
  pendingCut: number | null = null,
): WaitDetail[] {
  let waiting: number[]
  try {
    waiting = tingpaiCheck(hand, ctx.combinations, false)
  } catch {
    return []
  }
  if (!waiting.length) return []

  const baseWay = buildBaseWay(ctx, waiting.length)
  const details: WaitDetail[] = []

  for (const tile of waiting) {
    const show = countShowTilesOnTable(tile, ctx.seatDiscards, ctx.seatCombinations, pendingCut)
    // Tips timing: and-tile not yet on table; show==3 ⇒ 和绝张 (C# TipsContainer).
    const hejuezhang = show === 3 ? ['和绝张'] : []
    const completeHand = [...hand, tile]

    const ronWay = [...baseWay, ...hejuezhang, '点和']
    const tsumoWay = [...baseWay, ...hejuezhang, '自摸']

    let ronFan = 0
    let tsumoFan = 0
    try {
      ronFan = scoreHepai(ctx.subRule, completeHand, ctx.combinations, ronWay, tile)
      tsumoFan = scoreHepai(ctx.subRule, completeHand, ctx.combinations, tsumoWay, tile)
    } catch {
      ronFan = 0
      tsumoFan = 0
    }

    const ronAllowed = ronFan - ctx.flowerCount >= ctx.hepaiLimit
    const tsumoAllowed = tsumoFan - ctx.flowerCount >= ctx.hepaiLimit
    details.push({
      tile,
      base_f: ronAllowed ? ronFan : 0,
      selfdrawn_f: tsumoAllowed ? tsumoFan : 0,
      remaining_count: remainingCount(hand, ctx.seatDiscards, ctx.seatCombinations, tile, pendingCut),
    })
  }
  return details
}

/**
 * Build wait tip payload for WaitDisplay. Never throws.
 * When includeDiscards is true (can cut), returns waits_all keyed by discard tile.
 */
export function buildLocalWaitData(
  ctx: WaitTipsContext,
  options: { includeDiscards: boolean },
): WaitInfoData {
  try {
    if (!ctx.tips) return null
    if (ctx.hand.some((tile) => FLOWERS.has(tile))) return null

    if (!options.includeDiscards) {
      const details = detailsForHand(ctx, ctx.hand)
      return details.length ? { type: 'waits', details } : null
    }

    const byDiscard: Array<{ discard_tile: number; adds: WaitDetail[] }> = []
    const seen = new Set<number>()
    for (let index = 0; index < ctx.hand.length; index += 1) {
      const discard = ctx.hand[index]
      if (seen.has(discard) || FLOWERS.has(discard)) continue
      seen.add(discard)
      const remainingHand = [...ctx.hand.slice(0, index), ...ctx.hand.slice(index + 1)]
      const adds = detailsForHand(ctx, remainingHand, discard)
      if (adds.length) byDiscard.push({ discard_tile: discard, adds })
    }
    return byDiscard.length ? { type: 'waits_all', details: byDiscard } : null
  } catch {
    return null
  }
}
