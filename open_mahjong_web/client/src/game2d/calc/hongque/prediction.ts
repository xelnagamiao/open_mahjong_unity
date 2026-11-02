import {
  allWinResults,
  classifyMeld,
  fullDeck,
  isWinningHand,
  legalGroupUniverse,
  parseTile,
} from './index.ts'
import type { OpenMeld, WinFlags, WinScore } from './index.ts'

export interface KongStep {
  meldIndex: number
  tile: string
  tiles: string[]
}

export interface PredictedWinScore extends WinScore {
  kongSteps: KongStep[]
}

export interface WinningTileHint {
  tile: string
  selfDrawOnly: boolean
  ronScore: PredictedWinScore | null
  selfDrawScore: PredictedWinScore
}

const DECK = fullDeck()
const tileBit = (code: string): bigint => {
  const tile = parseTile(code)
  return 1n << BigInt(tile.colour * 9 + tile.number - 1)
}
const maskOf = (codes: string[]): bigint => codes.reduce((mask, code) => mask | tileBit(code), 0n)
const LEGAL_GROUPS = new Set(legalGroupUniverse().map(maskOf))
const DECK_BITS = DECK.map(tile => ({ tile, bit: tileBit(tile) }))
const EXTENSIONS = new Map<bigint, typeof DECK_BITS>()

function extensions(mask: bigint): typeof DECK_BITS {
  let result = EXTENSIONS.get(mask)
  if (!result) {
    // 每一步只并入一张牌；不能只验证最终超集而跳过中间的非法牌组。
    result = DECK_BITS.filter(({ bit }) => !(mask & bit) && LEGAL_GROUPS.has(mask | bit))
    if (EXTENSIONS.size >= 4096) EXTENSIONS.clear()
    EXTENSIONS.set(mask, result)
  }
  return result
}

function normalizeInput(hand: string[], openMelds: OpenMeld[]) {
  const codes = hand.map(code => parseTile(code).code)
  const melds = openMelds.map(meld => ({ ...meld, tiles: meld.tiles.map(code => parseTile(code).code) }))
  const all = [...codes, ...melds.flatMap(meld => meld.tiles)]
  if (new Set(all).size !== all.length) throw new Error('手牌与副露存在重复牌')
  if (melds.some(meld => !classifyMeld(meld.tiles))) throw new Error('副露中存在非法牌组')
  return { codes, melds }
}

/** 自摸时枚举可实际执行的连续加杠；捉和只计算原副露下的直接和牌。 */
export function predictWinResults(hand: string[], openMelds: OpenMeld[], flags: WinFlags): PredictedWinScore[] {
  const { codes, melds } = normalizeInput(hand, openMelds)
  const results: PredictedWinScore[] = []
  const visited = new Set<string>()
  const visit = (remaining: string[], current: OpenMeld[], masks: bigint[], steps: KongStep[]) => {
    // 同一最终副露可能由多种加杠顺序到达，只保留一条合法操作路径。
    const key = masks.join(',')
    if (visited.has(key)) return
    visited.add(key)
    if (isWinningHand(remaining, current)) {
      for (const score of allWinResults(remaining, current, flags)) {
        results.push({ ...score, kongSteps: steps })
      }
    }
    if (!flags.selfDraw || !remaining.length) return
    const handMask = maskOf(remaining)
    for (let meldIndex = 0; meldIndex < current.length; meldIndex++) {
      for (const { tile, bit } of extensions(masks[meldIndex])) {
        if (!(handMask & bit)) continue
        const tiles = [...current[meldIndex].tiles, tile]
        const nextMelds = current.map((meld, index) => index === meldIndex ? { ...meld, tiles } : meld)
        const nextMasks = masks.map((mask, index) => index === meldIndex ? mask | bit : mask)
        visit(remaining.filter(code => code !== tile), nextMelds, nextMasks,
          [...steps, { meldIndex, tile, tiles }])
      }
    }
  }
  visit(codes, melds, melds.map(meld => maskOf(meld.tiles)), [])
  return results.sort((a, b) => b.points - a.points || b.fanTotal - a.fanTotal || b.base - a.base
    || a.kongSteps.length - b.kongSteps.length)
}

/** 摸入一张后的完整预测，空手牌也可扩展副露；已用牌不会再次列为听口。 */
export function winningTileHints(
  hand: string[],
  openMelds: OpenMeld[],
  flags: WinFlags,
  used: Iterable<string> = [],
): WinningTileHint[] {
  const { codes, melds } = normalizeInput(hand, openMelds)
  const all = [...codes, ...melds.flatMap(meld => meld.tiles)]
  if (!all.length || all.length >= 14) return []
  const unavailable = new Set([...all, ...[...used].map(code => parseTile(code).code)])
  const hints: WinningTileHint[] = []
  for (const tile of DECK) {
    if (unavailable.has(tile)) continue
    const complete = [...codes, tile]
    const selfDrawScore = predictWinResults(complete, melds, { ...flags, selfDraw: true })[0]
    if (!selfDrawScore) continue
    const ronScore = predictWinResults(complete, melds, { ...flags, selfDraw: false })[0] ?? null
    hints.push({ tile, selfDrawOnly: !ronScore, ronScore, selfDrawScore })
  }
  return hints
}
