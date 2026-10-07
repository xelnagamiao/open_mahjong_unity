import type { WaitInfoData, WaitDetail } from '../../game/scene/WaitDisplay'

const TILES = [11,12,13,14,15,16,17,18,19,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36,37,38,39,41,42,43,44,45,46,47]
const ORPHANS = [11,19,21,29,31,39,41,42,43,44,45,46,47]
export const shanxiTilePoints = (tile: number): number => TILES.includes(tile) ? tile >= 41 ? 10 : tile % 10 : 0

export function shanxiMeldTiles(code: string): number[] {
  if (!/^[kgG](?:[123][1-9]|4[1-7])$/.test(code)) return []
  return Array(code[0] === 'k' ? 3 : 4).fill(Number(code.slice(1)))
}

function shapes(hand: number[], melds: string[]): string[][] {
  if (melds.length > 4 || hand.length + melds.length * 3 !== 14 || hand.some(t => !TILES.includes(t))) return []
  const physical = [...hand]
  for (const meld of melds) {
    const tiles = shanxiMeldTiles(meld)
    if (!tiles.length) return []
    physical.push(...tiles)
  }
  if (TILES.some(t => physical.filter(x => x === t).length > 4)) return []
  const result: string[][] = []
  if (!melds.length) {
    if ([...new Set(hand)].every(t => hand.filter(x => x === t).length % 2 === 0)) result.push(['seven'])
    if (new Set(hand).size === 13 && ORPHANS.every(t => hand.includes(t))) result.push(['orphans'])
  }
  function split(rest: number[], sets: string[]) {
    if (!rest.length) { result.push(sets); return }
    const tile = rest[0]
    if (rest.filter(x => x === tile).length >= 3) {
      const next = [...rest]
      for (let i = 0; i < 3; i++) next.splice(next.indexOf(tile), 1)
      split(next, [...sets, `k${tile}`])
    }
    if (tile < 40 && tile % 10 <= 7 && rest.includes(tile + 1) && rest.includes(tile + 2)) {
      const next = [...rest]
      for (let i = 0; i < 3; i++) next.splice(next.indexOf(tile + i), 1)
      split(next, [...sets, `s${tile + 1}`])
    }
  }
  for (const pair of new Set(hand)) {
    if (hand.filter(t => t === pair).length < 2) continue
    const rest = [...hand].sort((a,b) => a-b)
    rest.splice(rest.indexOf(pair), 1); rest.splice(rest.indexOf(pair), 1)
    split(rest, [])
  }
  return result
}

export function shanxiWaits(hand: number[], melds: string[] = []): number[] {
  if (hand.length + melds.length * 3 !== 13) return []
  return TILES.filter(tile => shapes([...hand, tile], melds).length > 0)
}

/** Base points after declaration; dealer premium and kong accounts are separate. */
export function shanxiBaseScore(hand: number[], melds: string[], tile: number, selfDraw: boolean): number {
  if (!hand.includes(tile) || shanxiTilePoints(tile) < (selfDraw ? 3 : 6)) return 0
  const candidates = shapes(hand, melds)
  if (!candidates.length) return 0
  let best = 0
  for (const shape of candidates) {
    if (shape.includes('orphans')) best = Math.max(best, 60)
    else if (shape.includes('seven')) best = Math.max(best, hand.filter(t => t === tile).length === 4 ? 40 : 20)
    else if ([1,2,3].some(s => [2,5,8].every(n => shape.includes(`s${s}${n}`)))) best = Math.max(best, 20)
  }
  const flush = new Set([...hand,...melds.flatMap(shanxiMeldTiles)].map(t => Math.floor(t / 10))).size === 1
  return best + (flush ? 20 : 0) + shanxiTilePoints(tile)
}

export function buildShanxiWaitData(ctx: { hand: number[]; combinations: string[]; publicTiles: number[] }, includeDiscards = false): WaitInfoData {
  function details(hand: number[], pendingCut?: number): WaitDetail[] {
    const waits = shanxiWaits(hand, ctx.combinations)
    // Basic declaration requirement only; do not simulate missed wins or
    // situational bonuses. Availability is displayed separately as copies.
    const canDeclare = waits.some(tile => shanxiTilePoints(tile) >= 6)
    const visible = [...hand, ...ctx.publicTiles, ...(pendingCut == null ? [] : [pendingCut])]
    return waits.map(tile => {
      const base_f = canDeclare ? shanxiBaseScore([...hand,tile], ctx.combinations, tile, false) : 0
      const selfdrawn_f = canDeclare ? shanxiBaseScore([...hand,tile], ctx.combinations, tile, true) : 0
      return { tile, base_f, selfdrawn_f, unit:'分', remaining_count:Math.max(0,4-visible.filter(t=>t===tile).length),
        ...(base_f <= 0 && selfdrawn_f <= 0 ? { label:shanxiTilePoints(tile) < 3 ? '未起和' : '未满足' } : {}) }
    })
  }
  if (!includeDiscards) {
    const entries = details(ctx.hand)
    return entries.length ? {type:'waits',details:entries} : null
  }
  const entries = [...new Set(ctx.hand)].sort((a,b)=>a-b).map(discard => {
    const hand = [...ctx.hand]; hand.splice(hand.indexOf(discard),1)
    return {discard_tile:discard,adds:details(hand,discard)}
  }).filter(entry=>entry.adds.length)
  return entries.length ? {type:'waits_all',details:entries} : null
}

/** Only the selected player's already discarded concealed tiles are known. */
export function shanxiKnownConcealedDiscards(round: {start_player_index?: number; action_ticks?: unknown[][]} | null | undefined, node: number, seat: number): number[] {
  let actor = Number(round?.start_player_index ?? 0)
  const result: number[] = []
  for (const tick of (round?.action_ticks ?? []).slice(0, Math.max(0,node))) {
    const action = String(tick[0] ?? '')
    if (action === 'reset') actor = Number(tick[1] ?? actor)
    else if (['p','g','bd'].includes(action)) actor = Number(tick[2] ?? actor)
    if (action === 'c') {
      if (actor === seat && tick[3] === 'C' && TILES.includes(Number(tick[1]))) result.push(Number(tick[1]))
      actor = (actor + 1) % 4
    }
  }
  return result
}
