/** MIL 贵州基本分提示。鸡杠单独结算，过水与偶然役不参与提示预测。 */
const TILES = Array.from({ length: 27 }, (_, i) => (Math.floor(i / 9) + 1) * 10 + i % 9 + 1)
const validTile = (tile: number) => Number.isInteger(tile) && TILES.includes(tile)

function groups(counts: number[], remaining: number): boolean {
  const tile = counts.findIndex((count) => count > 0)
  if (tile < 0) return remaining === 0
  if (remaining <= 0) return false
  if (counts[tile] >= 3) {
    counts[tile] -= 3
    const complete = groups(counts, remaining - 1)
    counts[tile] += 3
    if (complete) return true
  }
  if (tile % 10 <= 7 && counts[tile + 1] > 0 && counts[tile + 2] > 0) {
    counts[tile]--; counts[tile + 1]--; counts[tile + 2]--
    const complete = groups(counts, remaining - 1)
    counts[tile]++; counts[tile + 1]++; counts[tile + 2]++
    if (complete) return true
  }
  return false
}

export function guizhouBasicScore(hand: number[], melds: string[] = [], ready = ''): { points: number; canRon: boolean } | null {
  if (melds.length > 4 || hand.length !== 14 - 3 * melds.length) return null
  const hidden = Array<number>(40).fill(0)
  const physical = Array<number>(40).fill(0)
  for (const tile of hand) {
    if (!validTile(tile) || ++physical[tile] > 4) return null
    hidden[tile]++
  }
  let passport = false
  for (const code of melds) {
    if (!/^[kgG][1-3][1-9]$/.test(code)) return null
    const tile = Number(code.slice(1))
    const kong = code[0] !== 'k'
    physical[tile] += kong ? 4 : 3
    if (physical[tile] > 4) return null
    passport ||= kong
  }
  let standard = false
  let allPungs = false
  for (const tile of TILES) {
    if (hidden[tile] < 2) continue
    hidden[tile] -= 2
    standard ||= groups(hidden, 4 - melds.length)
    allPungs ||= hidden.every((count) => count % 3 === 0)
    hidden[tile] += 2
  }
  const pairs = melds.length === 0 && hidden.every((count) => count % 2 === 0)
  if (!standard && !pairs) return null
  const pure = new Set(TILES.filter((tile) => physical[tile] > 0).map((tile) => Math.floor(tile / 10))).size === 1
  const common = (pure ? 13 : 0) + (ready === 'hard_ready' ? 26 : ready === 'soft_ready' ? 13 : 0)
  let pattern = standard ? (melds.length === 4 ? 13 : allPungs ? 8 : 0) : 0
  if (pairs) pattern = Math.max(pattern, hidden.includes(4) ? 26 : 13)
  const plain = common + pattern === 0
  return { points: Math.min(39, plain ? 3 : common + pattern), canRon: !plain || passport }
}

/** 物理待牌始终保留，仅自摸的牌也必须显示；不能允许第五张实体牌。 */
export function guizhouWaitingTiles(hand: number[], melds: string[] = []): number[] {
  if (hand.length !== 13 - 3 * melds.length) return []
  return TILES.filter((tile) => guizhouBasicScore([...hand, tile], melds) != null)
}

export function guizhouRonWaitingTiles(hand: number[], melds: string[] = [], ready = ''): number[] {
  return guizhouWaitingTiles(hand, melds).filter((tile) => guizhouBasicScore([...hand, tile], melds, ready)?.canRon)
}

export interface GuizhouWaitContext {
  hand: number[]
  combinations: string[]
  ready?: string
  seatDiscards: number[][]
  seatCombinations: string[][]
}
export interface GuizhouWaitDetail {
  tile: number
  base_f: number
  selfdrawn_f: number
  remaining_count: number
  unit: string
}
export type GuizhouWaitData =
  | { type: 'waits'; details: GuizhouWaitDetail[] }
  | { type: 'waits_all'; details: Array<{ discard_tile: number; adds: GuizhouWaitDetail[] }> }
  | null

function details(ctx: GuizhouWaitContext, hand: number[], pendingCut?: number): GuizhouWaitDetail[] {
  return guizhouWaitingTiles(hand, ctx.combinations).map((tile) => {
    const score = guizhouBasicScore([...hand, tile], ctx.combinations, ctx.ready)!
    let used = hand.filter((value) => value === tile).length + (pendingCut === tile ? 1 : 0)
    used += ctx.seatDiscards.flat().filter((value) => value === tile).length
    for (const code of ctx.seatCombinations.flat()) {
      if (/^[kgG][1-3][1-9]$/.test(code) && Number(code.slice(1)) === tile) used += code[0] === 'k' ? 3 : 4
    }
    return { tile, base_f: score.canRon ? score.points : 0, selfdrawn_f: score.points,
      remaining_count: Math.max(0, 4 - used), unit: '分' }
  })
}

export function buildGuizhouWaitData(ctx: GuizhouWaitContext, options: { includeDiscards: boolean }): GuizhouWaitData {
  if (!options.includeDiscards) {
    const waits = details(ctx, ctx.hand)
    return waits.length ? { type: 'waits', details: waits } : null
  }
  const byDiscard = [...new Set(ctx.hand)].flatMap((tile) => {
    const hand = [...ctx.hand]
    hand.splice(hand.indexOf(tile), 1)
    const adds = details(ctx, hand, tile)
    return adds.length ? [{ discard_tile: tile, adds }] : []
  })
  return byDiscard.length ? { type: 'waits_all', details: byDiscard } : null
}
