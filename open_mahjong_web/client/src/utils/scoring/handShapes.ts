import type { PailiOptions } from '../pailiCalculator.ts'

export interface HandSet { kind: 's' | 'k' | 'g' | 'q'; tile: number; open: boolean; fixed: boolean }
export interface HandShape { shape: '一般型' | '七对' | '十三幺'; sets: HandSet[] }
export const suit = (tile: number) => Math.floor(tile / 10)
export const terminal = (tile: number) => tile < 40 && [1, 9].includes(tile % 10)
export const orphan = (tile: number) => tile > 40 || terminal(tile)
export const countsOf = (tiles: number[]) => tiles.reduce((counts, tile) => { counts[tile] = (counts[tile] || 0) + 1; return counts }, {} as Record<number, number>)
export const setTiles = (set: HandSet) => set.kind === 's' ? [set.tile - 1, set.tile, set.tile + 1] : Array(set.kind === 'g' ? 4 : set.kind === 'q' ? 2 : 3).fill(set.tile) as number[]
export const shapeGroups = (shape: HandShape) => shape.sets.map(set => ({
  label: set.kind === 'q' ? '对子' : set.kind === 's' ? (set.open ? '顺子' : '暗顺') : set.kind === 'g' ? (set.open ? '明杠' : '暗杠') : (set.open ? '明刻' : '暗刻'),
  tiles: setTiles(set),
}))

/** Enumerate shapes independently of scoring; the winning set is chosen by each rule. */
export function solveHand(hand: number[], melds: string[], options: PailiOptions): HandShape[] {
  if (hand.length + melds.length * 3 !== 14) return []
  const result: HandShape[] = []
  const counts = countsOf(hand)
  const tiles = Object.keys(counts).map(Number).sort((a, b) => a - b)
  const fixed = melds.map(code => ({ kind: code[0].toLowerCase(), tile: Number(code.slice(1)), open: code[0] === code[0].toLowerCase(), fixed: true })) as HandSet[]
  const make = (kind: HandSet['kind'], tile: number): HandSet => ({ kind, tile, open: false, fixed: false })
  if (!melds.length) {
    if (options.thirteenOrphans && tiles.length === 13 && tiles.every(orphan)) result.push({ shape: '十三幺', sets: [make('q', tiles.find(tile => counts[tile] === 2)!)] })
    const sevenPairs = options.riichiSevenPairs ? tiles.length === 7 && tiles.every(tile => counts[tile] === 2) : options.mcrSevenPairs && tiles.every(tile => counts[tile] % 2 === 0)
    if (sevenPairs) result.push({ shape: '七对', sets: tiles.flatMap(tile => Array.from({ length: counts[tile] / 2 }, () => make('q', tile))) })
  }
  function visit(sets: HandSet[], pair: boolean) {
    const tile = tiles.find(id => counts[id] > 0)
    if (!tile) { if (pair && sets.length === 5) result.push({ shape: '一般型', sets: [...sets] }); return }
    const take = (ids: number[], set: HandSet, nextPair = pair) => {
      if (!ids.every(id => (counts[id] || 0) >= ids.filter(value => value === id).length)) return
      ids.forEach(id => counts[id]--)
      visit([...sets, set], nextPair)
      ids.forEach(id => counts[id]++)
    }
    if (!pair) take([tile, tile], make('q', tile), true)
    take([tile, tile, tile], make('k', tile))
    if (tile < 40 && tile % 10 <= 7) take([tile, tile + 1, tile + 2], make('s', tile + 1))
  }
  visit(fixed, false)
  return result
}
