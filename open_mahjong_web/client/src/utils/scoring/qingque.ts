import data from './qingqueData.json' with { type: 'json' }
import type { HandDraft, HandDecomposition, WinMethod } from '../handAnalysis.ts'
import type { PailiOptions } from '../pailiCalculator.ts'
import { countsOf, orphan, setTiles, shapeGroups, solveHand, suit, terminal } from './handShapes.ts'
import type { HandSet, HandShape } from './handShapes.ts'

// Port of the repository's Qingque13 criteria. Cache keys and coverage are copied
// from QingqueFan.cs / QingqueDerepellenise.cs; values retain their full precision.
const range = (n: number, start = 1) => Array.from({ length: n }, (_, i) => start + i)
const suits = [1, 2, 3]
const suitPairs = [[1, 2], [1, 3], [2, 3]]
const permutations = [[1, 2, 3], [1, 3, 2], [2, 1, 3], [2, 3, 1], [3, 1, 2], [3, 2, 1]]
const roundEven = (n: number) => n % 1 === 0.5 ? 2 * Math.round(n / 2) : Math.round(n)

function pairedMelds(sets: HandSet[], matches: (a: HandSet, b: HandSet) => boolean) {
  const used = new Set<number>()
  let count = 0
  for (let i = 0; i < sets.length; i++) for (let j = i + 1; j < sets.length; j++) {
    if (!used.has(i) && !used.has(j) && matches(sets[i], sets[j])) { used.add(i); used.add(j); count++ }
  }
  return count
}

function evaluate(draft: HandDraft, shape: HandShape, method: WinMethod): Set<string> {
  const fans = new Set<string>(['Trivial'])
  const add = (name: string, yes: boolean) => { if (yes) fans.add(name) }
  const pairs = shape.shape === '七对'
  const sets = shape.sets.filter(set => set.kind !== 'q')
  const pair = shape.sets.find(set => set.kind === 'q')!.tile
  const all = [...draft.hand, ...shape.sets.filter(set => set.fixed).flatMap(setTiles)]
  const counts = countsOf(all)
  const count = (tile: number) => counts[tile] || 0
  const tiles = Object.keys(counts).map(Number)
  const numbers = [...new Set(tiles.filter(tile => tile < 40).map(tile => tile % 10))].sort((a, b) => a - b)
  const noHonours = tiles.every(tile => tile < 40)
  const closed = sets.every(set => !set.open)
  const seat = 41 + ['东', '南', '西', '北'].indexOf(draft.context.seat)
  const fanTiles = [seat, 45, 46, 47]
  const win = draft.winTile ?? draft.hand.at(-1)!
  add('ConcealedHand', closed)
  add('OutWithReplacementTile', method === 'tsumo' && draft.context.kongWin)
  add('RobbingTheKong', method === 'ron' && draft.context.robKong)
  add(method === 'tsumo' ? 'LastTileDraw' : 'LastTileClaim', draft.context.lastTile)
  if (shape.shape === '十三幺') {
    for (const fan of ['ThirteenOrphans', 'AllTerminalsAndHonours', 'MixedOutsideHand']) fans.add(fan)
    add('FanTile1P', fanTiles.some(tile => count(tile) >= 2))
    return fans
  }
  add('SevenPairs', pairs)
  const kongs = sets.filter(set => set.kind === 'g')
  const concealedKongs = kongs.filter(set => !set.open).length
  const concealedTriplets = sets.filter(set => set.kind !== 's' && !set.open && (set.fixed || method === 'tsumo' || set.tile !== win || draft.hand.filter(tile => tile === win).length === 4)).length
  for (const [n, prefix] of [[1, ''], [2, 'Two'], [3, 'Three'], [4, 'Four']] as const) {
    add(n === 1 ? 'ConcealedKong' : `${prefix}ConcealedKongs`, concealedKongs >= n)
    if (n >= 2) add(`${prefix}Kongs`, kongs.length >= n)
    if (n >= 3) add(`${prefix}ConcealedTriplets`, concealedTriplets >= n)
  }
  add('AllTriplets', !pairs && sets.every(set => set.kind !== 's'))
  const quadruplets = tiles.filter(tile => count(tile) === 4).length
  add('TwelveHog', quadruplets - kongs.length === 3)
  add('EightHog', quadruplets - kongs.length >= 2)
  add('DoublePair', pairs && quadruplets >= 1)
  add('TwoDoublePairs', pairs && quadruplets >= 2)
  add('ThreeDoublePairs', pairs && quadruplets >= 3)
  const winds = [41, 42, 43, 44], dragons = [45, 46, 47]
  const tripletCount = (ids: number[]) => ids.filter(tile => count(tile) >= 3).length
  const pairCount = (ids: number[]) => ids.reduce((sum, tile) => sum + (pairs ? Math.floor(count(tile) / 2) : Number(count(tile) >= 2)), 0)
  add('AllHonours', tiles.every(tile => tile > 40))
  add('BigFourWinds', !pairs && tripletCount(winds) === 4)
  add('LittleFourWinds', !pairs && tripletCount(winds) + pairCount(winds) === 7)
  add('FourWindPairs', pairs && winds.every(tile => count(tile) >= 2))
  add('ThreeWindTriplets', !pairs && tripletCount(winds) >= 3)
  add('SevenWindPairs', pairs && pairCount(winds) === 7)
  add('SixWindPairs', pairs && pairCount(winds) >= 6)
  add('BigThreeDragons', !pairs && tripletCount(dragons) === 3)
  add('LittleThreeDragons', !pairs && tripletCount(dragons) + pairCount(dragons) === 5)
  add('SixDragonPairs', pairs && pairCount(dragons) === 6)
  add('ThreeDragonPairs', pairs && dragons.every(tile => count(tile) >= 2))
  for (const n of range(4)) add(`FanTile${n}T`, !pairs && tripletCount(fanTiles) >= n)
  for (const n of range(7)) add(`FanTile${n}P`, pairCount(fanTiles) >= n)
  add('AllTerminals', tiles.every(terminal))
  add('AllTerminalsAndHonours', tiles.every(orphan))
  add('PureOutsideHand', shape.sets.every(set => setTiles(set).some(terminal)))
  add('MixedOutsideHand', shape.sets.every(set => setTiles(set).some(orphan)))
  const suitCount = new Set(tiles.filter(tile => tile < 40).map(suit)).size
  add('FullFlush', noHonours && suitCount === 1)
  add('HalfFlush', suitCount <= 1)
  add('NineGates', !pairs && all.length === 14 && suits.some(s => range(9).every(n => count(s * 10 + n) - Number(win === s * 10 + n) === (n === 1 || n === 9 ? 3 : 1))))
  add('AllTypes', !pairs && suitCount === 3 && winds.some(count) && dragons.some(count))
  add('MixedOneNumber', numbers.length <= 1)
  add('TwoNumbers', noHonours && numbers.length === 2)
  const span = numbers.at(-1)! - numbers[0]
  add('TwoConsecutiveNumbers', noHonours && numbers.length === 2 && span === 1)
  add('ThreeConsecutiveNumbers', noHonours && span <= 2)
  add('FourConsecutiveNumbers', noHonours && span <= 3)
  const unitNumbers = pairs ? shape.sets.map(set => set.tile % 10) : [pair % 10, ...sets.flatMap(set => set.kind === 's' ? setTiles(set).map(tile => tile % 10) : [set.tile % 10])]
  const distinctUnits = new Set(unitNumbers)
  add('ConnectedNumbers', noHonours && distinctUnits.size === unitNumbers.length && Math.max(...unitNumbers) - Math.min(...unitNumbers) + 1 === unitNumbers.length)
  add('GappedNumbers', noHonours && numbers.length > 2 && [2, 3].some(step => numbers.every(n => n % step === numbers[0] % step)))
  const reflection = (swap: boolean) => {
    if (!noHonours) return false
    const sum = pairs ? numbers[0] + numbers.at(-1)! : 2 * (pair % 10)
    let axis = suit(pair)
    if (pairs && swap) {
      const totals = suits.map(s => tiles.filter(tile => suit(tile) === s).reduce((n, tile) => n + count(tile), 0))
      axis = 0
      if (totals[0] === totals[1]) axis = 3
      if (totals[0] === totals[2]) axis = 2
      if (totals[1] === totals[2]) axis = 1
      if (!axis) return false
    }
    const reflected = (tile: number) => {
      const number = sum - tile % 10
      if (number < 1 || number > 9) return -1
      return (swap && suit(tile) !== axis ? 6 - axis - suit(tile) : suit(tile)) * 10 + number
    }
    if (pairs) return tiles.every(tile => count(reflected(tile)) === count(tile))
    const used = new Set<number>()
    return sets.every((set, i) => {
      if (used.has(i)) return true
      const j = sets.findIndex((other, index) => !used.has(index) && (set.kind === 's') === (other.kind === 's') && set.tile === reflected(other.tile))
      if (j < 0) return false
      used.add(i); used.add(j); return true
    })
  }
  add('ReflectedHand', reflection(false))
  add('ReflectedHand2', reflection(true))
  add('CommonNumber', !pairs && noHonours && sets.every(set => setTiles(set).some(tile => tile % 10 === pair % 10)))
  let equalPairs = 0
  sets.forEach((a, i) => sets.slice(i + 1).forEach(b => { if (a.kind === b.kind && a.tile === b.tile) equalPairs++ }))
  add('QuadrupleSequence', equalPairs === 6)
  add('TripleSequence', equalPairs >= 3)
  add('TwoDoubleSequences', equalPairs === 2 || equalPairs > 3)
  add('DoubleSequence', equalPairs >= 1)
  const has = (kind: 's' | 'k', tile: number) => sets.some(set => (kind === 's' ? set.kind === 's' : set.kind !== 's') && set.tile === tile)
  const shifted = (kind: 's' | 'k', length: number, step: number) => suits.some(s => range(9).some(start => range(length, 0).every(i => has(kind, s * 10 + start + i * step) && start + i * step <= 9)))
  for (const [length, prefix] of [[3, 'Three'], [4, 'Four']] as const) {
    add(`${prefix}ShiftedTriplets`, shifted('k', length, 1))
    add(`${prefix}ShiftedSequences`, shifted('s', length, 1))
    add(`${prefix}ChainedSequences`, shifted('s', length, 2))
  }
  add('PureStraight', suits.some(s => [2, 5, 8].every(n => has('s', s * 10 + n))))
  for (const [length, prefix] of [[4, 'Four'], [5, 'Five'], [6, 'Six'], [7, 'Seven']] as const) add(`${prefix}ShiftedPairs`, pairs && suits.some(s => range(10 - length).some(start => range(length, 0).every(i => count(s * 10 + start + i) >= 2))))
  add('MixedTripleTriplet', range(9).some(n => suits.every(s => has('k', s * 10 + n))))
  add('MixedTripleSequence', range(7, 2).some(n => suits.every(s => has('s', s * 10 + n))))
  const triplePairs = range(9).filter(n => suits.every(s => count(s * 10 + n) >= 2))
  add('TwoTriplePairs', pairs && (triplePairs.length >= 2 || range(9).some(n => suits.reduce((total, s) => total + count(s * 10 + n) / 2, 0) === 6)))
  add('MixedTriplePair', pairs && triplePairs.length > 0)
  add('MixedShiftedTriplets', permutations.some(p => range(7).some(start => p.every((s, i) => has('k', s * 10 + start + i)))))
  add('MixedStraight', permutations.some(p => p.every((s, i) => has('s', s * 10 + 2 + i * 3))))
  add('MirroredHand', new Set(sets.filter(set => set.tile < 40).map(set => suit(set.tile))).size === 2 && pairedMelds(sets, (a, b) => a.tile < 40 && b.tile < 40 && suit(a.tile) !== suit(b.tile) && a.tile % 10 === b.tile % 10 && (a.kind === 's') === (b.kind === 's')) === 2)
  add('TwoMirroredPairs', pairs && suitPairs.some(([a, b]) => range(9).filter(n => count(a * 10 + n) >= 2 && count(b * 10 + n) >= 2).length >= 2 || range(9).some(n => count(a * 10 + n) === 4 && count(b * 10 + n) === 4)))
  add('ThreeMirroredPairs', pairs && suitPairs.some(([a, b]) => {
    const matches = range(9).filter(n => count(a * 10 + n) >= 2 && count(b * 10 + n) >= 2)
    return matches.length >= 3 || matches.some(i => matches.some(j => j !== i && count(a * 10 + j) === 4 && count(b * 10 + j) === 4))
  }))
  add('TwoShortStraights', pairedMelds(sets, (a, b) => a.kind === 's' && b.kind === 's' && suit(a.tile) === suit(b.tile) && Math.abs(a.tile - b.tile) === 3) === 2)
  return fans
}

export function scoreQingque(draft: HandDraft, options: PailiOptions, method = draft.context.method): HandDecomposition[] {
  return solveHand(draft.hand, draft.melds, options).map(shape => {
    const fans = evaluate(draft, shape, method)
    let code = 0n, bonus = 0
    data.names.forEach(([key], i) => { if (fans.has(key)) { if (i >= 1 && i <= 6) bonus += i <= 2 ? 16 : 4; else code |= 1n << BigInt(i) } })
    const regular = (data.cache as Record<string, number>)[code.toString(16)]
    if (regular === undefined) throw new Error('这组青雀番种尚无精算数据，请调整手牌后重试。')
    const fan = regular + bonus
    const display = new Set(fans)
    display.delete('Trivial')
    for (const [cover, ...covered] of data.covers) if (fans.has(cover)) covered.forEach(key => display.delete(key))
    if (fans.has('ReflectedHand') && fans.has('ReflectedHand2') && fans.has('FullFlush')) display.delete('ReflectedHand2')
    const groups = shape.shape === '十三幺' ? [{ label: '十三幺', tiles: [...draft.hand].sort((a, b) => a - b) }] : shapeGroups(shape)
    return { rule: 'qingque' as const, fan, baseFan: fan, canWin: fan >= 1, points: roundEven(fan * fan), fanNames: data.names.filter(([key]) => display.has(key)).map(([, name]) => name), groups, shape: shape.shape }
  }).sort((a, b) => b.fan - a.fan)
}
