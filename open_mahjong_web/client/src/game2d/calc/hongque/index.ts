/**
 * Hongque 2 (虹雀²) v1.6 hand check and scoring.
 * TypeScript port of the authoritative Python implementation:
 *   open_mahjong_server/server/gamestate/game_hongque/
 *     tile.py / rules.py / win_check.py / scoring.py
 *
 * Rulebook 5.1.1: every tile must belong to one and only one legal group
 * (three or more tiles).  There is no pair/head.
 *
 * Tile codes: "AX1".."GY9" (14 colour levels × numbers 1..9, 126 unique tiles).
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface HongqueTile {
  colour: number // 0..13
  number: number // 1..9
  code: string
}

export interface MeldShape {
  kind: 'sequence' | 'triplet' | 'rainbow'
  baseKind: 'sequence' | 'triplet'
  tiles: string[]
  colourStep: number
  numberStep: number
  isRainbow: boolean
}

export interface OpenMeld {
  kind?: string
  tiles: string[]
  [key: string]: unknown
}

export interface WinDecomposition {
  groups: string[][]
  pair: string[]
}

export interface FanEntry {
  name: string
  value: number
  count: number
  total: number
}

export interface WinFlags {
  selfDraw: boolean
  beforeFirstDiscard: boolean
  wallEmpty: boolean
}

export interface WinScore {
  partition: string[][]
  pair: string[]
  groups: string[][]
  base: number
  fans: FanEntry[]
  fanTotal: number
  points: number
  concealed: boolean
}

// ---------------------------------------------------------------------------
// Tile helpers
// ---------------------------------------------------------------------------

const LETTERS = 'ABCDEFG'
const COLOUR_CODES = [
  'AX', 'AY', 'BX', 'BY', 'CX', 'CY', 'DX',
  'DY', 'EX', 'EY', 'FX', 'FY', 'GX', 'GY',
]

const TILE_RE = /^([A-Ga-g])([XxYy])([1-9])$/

export function parseTile(code: string): HongqueTile {
  const match = TILE_RE.exec(String(code || '').trim())
  if (!match) throw new Error(`非法虹雀牌码: ${code}`)
  const colour = LETTERS.indexOf(match[1].toUpperCase()) * 2 + (match[2].toUpperCase() === 'Y' ? 1 : 0)
  const number = Number(match[3])
  return { colour, number, code: `${match[1].toUpperCase()}${match[2].toUpperCase()}${match[3]}` }
}

export function primaryColours(colour: number): number[] {
  const base = Math.floor(colour / 2)
  if (colour % 2 === 0) return [base]
  return [base, (base + 1) % 7]
}

export function fullDeck(): string[] {
  const deck: string[] = []
  for (let colour = 0; colour < 14; colour++) {
    for (let number = 1; number <= 9; number++) {
      deck.push(`${COLOUR_CODES[colour]}${number}`)
    }
  }
  return deck
}

export function colourCodes(): string[] {
  return [...COLOUR_CODES]
}

// ---------------------------------------------------------------------------
// Meld classification (rules.classify_meld)
// ---------------------------------------------------------------------------

function cyclicProgression(values: number[], step: number): boolean {
  if (!values.length) return false
  const counts = new Map<number, number>()
  for (const value of values) counts.set(value, (counts.get(value) || 0) + 1)
  if ([...counts.values()].some((count) => count > 1)) return false
  for (const start of values) {
    const expected = new Set<number>()
    for (let offset = 0; offset < values.length; offset++) {
      expected.add(((start + step * offset) % 14 + 14) % 14)
    }
    if (expected.size === values.length && values.every((value) => expected.has(value))) return true
  }
  return false
}

function orderedColourProgression(tiles: HongqueTile[], step: number, numberStep: number): boolean {
  const ordered = [...tiles].sort((a, b) => a.number - b.number || a.colour - b.colour)
  if (numberStep < 0) ordered.reverse()
  for (let i = 0; i + 1 < ordered.length; i++) {
    const diff = ((ordered[i + 1].colour - ordered[i].colour) % 14 + 14) % 14
    if (diff !== step) return false
  }
  return true
}

export function classifyMeld(codes: string[]): MeldShape | null {
  const parsed = [...new Set(codes.map(parseTile))]
  if (parsed.length !== codes.length || parsed.length < 3) return null
  const normalized = parsed.sort((a, b) => a.number - b.number || a.colour - b.colour)
  const numbers = normalized.map((tile) => tile.number)
  const colours = normalized.map((tile) => tile.colour)

  const primary = new Set<number>()
  for (const colour of colours) {
    for (const base of primaryColours(colour)) primary.add(base)
  }
  const rainbow = primary.size === 7

  if (new Set(numbers).size === 1) {
    for (const colourStep of [1, 2]) {
      if (cyclicProgression(colours, colourStep)) {
        return {
          kind: rainbow ? 'rainbow' : 'triplet',
          baseKind: 'triplet',
          tiles: normalized.map((tile) => tile.code),
          colourStep,
          numberStep: 0,
          isRainbow: rainbow,
        }
      }
    }
  }

  for (let numberStep = -4; numberStep <= 4; numberStep++) {
    if (numberStep === 0) continue
    const orderedNumbers = [...numbers].sort((a, b) => a - b)
    if (numberStep < 0) orderedNumbers.reverse()
    let stepOk = true
    for (let i = 0; i + 1 < orderedNumbers.length; i++) {
      if (orderedNumbers[i + 1] - orderedNumbers[i] !== numberStep) {
        stepOk = false
        break
      }
    }
    if (!stepOk) continue
    for (const colourStep of [0, 1, 2]) {
      if (colourStep === 0) {
        if (new Set(colours).size === 1) {
          return {
            kind: rainbow ? 'rainbow' : 'sequence',
            baseKind: 'sequence',
            tiles: normalized.map((tile) => tile.code),
            colourStep: 0,
            numberStep,
            isRainbow: rainbow,
          }
        }
      } else if (orderedColourProgression(normalized, colourStep, numberStep)) {
        return {
          kind: rainbow ? 'rainbow' : 'sequence',
          baseKind: 'sequence',
          tiles: normalized.map((tile) => tile.code),
          colourStep,
          numberStep,
          isRainbow: rainbow,
        }
      }
    }
  }
  return null
}

// ---------------------------------------------------------------------------
// Legal group universe (group_index.py)
// ---------------------------------------------------------------------------

const DECK = fullDeck()
const DECK_INDEX = new Map<string, number>()
DECK.forEach((code, index) => DECK_INDEX.set(code, index))

function tileMask(colour: number, number: number): bigint {
  return 1n << BigInt(DECK_INDEX.get(`${COLOUR_CODES[colour]}${number}`) as number)
}

function bitCount(value: bigint): number {
  let count = 0
  let v = value
  while (v) {
    v &= v - 1n
    count++
  }
  return count
}

function generateGroupMasks(): bigint[] {
  const groups = new Set<bigint>()

  for (let number = 1; number <= 9; number++) {
    for (const [colourStep, maxLength] of [[1, 14], [2, 7]] as [number, number][]) {
      for (let length = 3; length <= maxLength; length++) {
        for (let startColour = 0; startColour < 14; startColour++) {
          let mask = 0n
          for (let offset = 0; offset < length; offset++) {
            mask |= tileMask((startColour + offset * colourStep) % 14, number)
          }
          groups.add(mask)
        }
      }
    }
  }

  for (const numberStep of [-4, -3, -2, -1, 1, 2, 3, 4]) {
    for (let startNumber = 1; startNumber <= 9; startNumber++) {
      const numbers: number[] = []
      let number = startNumber
      while (number >= 1 && number <= 9) {
        numbers.push(number)
        number += numberStep
      }
      for (let length = 3; length <= numbers.length; length++) {
        for (const colourStep of [0, 1, 2]) {
          for (let startColour = 0; startColour < 14; startColour++) {
            let mask = 0n
            for (let offset = 0; offset < length; offset++) {
              mask |= tileMask((startColour + offset * colourStep) % 14, numbers[offset])
            }
            groups.add(mask)
          }
        }
      }
    }
  }

  return [...groups].sort((a, b) => bitCount(a) - bitCount(b) || (a < b ? -1 : 1))
}

const GROUP_MASKS = generateGroupMasks()

function codesFromMask(mask: bigint): string[] {
  const codes: string[] = []
  for (let index = 0; index < DECK.length; index++) {
    if ((mask >> BigInt(index)) & 1n) codes.push(DECK[index])
  }
  return codes
}

function maskFromCodes(codes: string[]): bigint {
  let mask = 0n
  for (const code of codes) {
    const parsed = parseTile(code)
    const bit = 1n << BigInt(DECK_INDEX.get(parsed.code) as number)
    if (mask & bit) throw new Error(`虹雀牌不可重复: ${parsed.code}`)
    mask |= bit
  }
  return mask
}

function groupsWithin(mask: bigint): bigint[] {
  const result: bigint[] = []
  for (const groupMask of GROUP_MASKS) {
    if ((groupMask & mask) === groupMask) result.push(groupMask)
  }
  return result
}

/**
 * 按“组内最小牌”建立索引。锚点递归中当前锚点（未覆盖的最小牌）必属于
 * 下一组，且该组的最小牌就是锚点，因此只需枚举最小牌==锚点的牌组。
 * 这既剪掉了与锚点无关的分支，也让同一组合只以固定顺序生成一次
 * （不再出现“相同组合不同顺序”的重复拆解）。
 */
function groupsByMinMask(groupCache: bigint[]): Map<bigint, bigint[]> {
  const buckets = new Map<bigint, bigint[]>()
  for (const groupMask of groupCache) {
    const minBit = groupMask & -groupMask
    const list = buckets.get(minBit)
    if (list) list.push(groupMask)
    else buckets.set(minBit, [groupMask])
  }
  return buckets
}

// ---------------------------------------------------------------------------
// Winning decomposition (win_check.py)
// ---------------------------------------------------------------------------

function partitionMasks(mask: bigint, buckets: Map<bigint, bigint[]>): bigint[][] {
  if (mask === 0n) return [[]]
  const anchor = mask & -mask
  const results: bigint[][] = []
  for (const groupMask of buckets.get(anchor) || []) {
    if ((groupMask & mask) === groupMask) {
      for (const tail of partitionMasks(mask ^ groupMask, buckets)) {
        results.push([groupMask, ...tail])
      }
    }
  }
  return results
}

export function winningDecompositions(hand: string[], openMelds: OpenMeld[] = []): WinDecomposition[] {
  let codes: string[]
  try {
    codes = hand.map((code) => parseTile(code).code)
  } catch (_) {
    return []
  }
  if (new Set(codes).size !== codes.length) return []

  const results: WinDecomposition[] = []
  const seen = new Set<string>()
  const mask = maskFromCodes(codes)
  const buckets = groupsByMinMask(groupsWithin(mask))

  for (const partition of partitionMasks(mask, buckets)) {
    if (partition.length || openMelds.length) {
      // 组内按（数字，花色）升序：顺子/彩虹按数字自然递增，同数刻子按花色；
      // 组间按（组内最小数字，最小花色）稳定排序，展示美观且保证同一组合只出现一次。
      const groups = partition
        .map(codesFromMask)
        .map((group) =>
          [...group].sort(
            (a, b) =>
              parseTile(a).number - parseTile(b).number ||
              parseTile(a).colour - parseTile(b).colour
          )
        )
        .sort((a, b) => groupSortKey(a) - groupSortKey(b))
      const key = JSON.stringify(groups)
      if (seen.has(key)) continue
      seen.add(key)
      results.push({ groups, pair: [] })
    }
  }
  return results
}

/** 组间排序键：先比组内最小数字，再比最小花色。 */
function groupSortKey(group: string[]): number {
  let minNumber = 10
  let minColour = 14
  for (const code of group) {
    const tile = parseTile(code)
    if (tile.number < minNumber) minNumber = tile.number
    if (tile.colour < minColour) minColour = tile.colour
  }
  return minNumber * 100 + minColour
}

export function isWinningHand(hand: string[], openMelds: OpenMeld[] = []): boolean {
  try {
    return winningDecompositions(hand, openMelds).length > 0
  } catch (_) {
    return false
  }
}

// ---------------------------------------------------------------------------
// Scoring (scoring.py)
// ---------------------------------------------------------------------------

function entry(name: string, value: number, count = 1): FanEntry {
  return { name, value, count, total: value * count }
}

function arithmetic(values: number[]): boolean {
  const ordered = [...new Set(values)].sort((a, b) => a - b)
  if (ordered.length !== values.length || ordered.length < 2) return false
  const step = ordered[1] - ordered[0]
  return step > 0 && ordered.every((value, index) => index === 0 || value - ordered[index - 1] === step)
}

function orderedTiles(shape: MeldShape): HongqueTile[] {
  const tiles = shape.tiles.map(parseTile)
  if (shape.baseKind === 'sequence') {
    return tiles.sort((a, b) => (shape.numberStep < 0 ? b.number - a.number : a.number - b.number))
  }
  return tiles.sort((a, b) => a.colour - b.colour)
}

function mergeEntries(entries: FanEntry[]): FanEntry[] {
  const merged = new Map<string, FanEntry>()
  for (const item of entries) {
    const existing = merged.get(item.name)
    if (existing) {
      existing.count += 1
      existing.total = existing.value * existing.count
    } else {
      merged.set(item.name, { ...item })
    }
  }
  return [...merged.values()]
}

function sameShapeFans(shapes: MeldShape[], baseKind: string, names: Map<number, [string, number]>): FanEntry[] {
  // 同刻/同顺系列：按“数字集合”分组，组内取最高档（双/三/四）。
  // 同刻不要求各组张数相等：只看同数字刻子的个数（3/3/4/4 也算四同刻）；
  // 不同数字可复计；同组的牌不会同时计入两档。
  const buckets = new Map<string, number>()
  for (const shape of shapes) {
    if (shape.baseKind !== baseKind) continue
    const key = JSON.stringify([...new Set(shape.tiles.map((code) => parseTile(code).number))].sort((a, b) => a - b))
    buckets.set(key, (buckets.get(key) || 0) + 1)
  }
  const entries: FanEntry[] = []
  for (const count of buckets.values()) {
    const eligible = [...names.keys()].filter((n) => count >= n).sort((a, b) => b - a)[0]
    if (!eligible) continue
    const [name, value] = names.get(eligible) as [string, number]
    entries.push(entry(name, value))
  }
  return mergeEntries(entries)
}

function sameColourLayoutFans(shapes: MeldShape[]): FanEntry[] {
  // 同花系列：按（长度，花色对应）分组，组内取最高档，不同分组可复计。
  const buckets = new Map<string, number>()
  for (const shape of shapes) {
    const ordered = orderedTiles(shape)
    const key = JSON.stringify([ordered.length, ordered.map((tile) => tile.colour)])
    buckets.set(key, (buckets.get(key) || 0) + 1)
  }
  const entries: FanEntry[] = []
  for (const count of buckets.values()) {
    if (count >= 4) entries.push(entry('四同花', 12))
    else if (count >= 3) entries.push(entry('三同花', 6))
    else if (count >= 2) entries.push(entry('双同花', 2))
  }
  return mergeEntries(entries)
}

function combinations<T>(items: T[], r: number): T[][] {
  const results: T[][] = []
  const n = items.length
  function walk(start: number, chosen: T[]) {
    if (chosen.length === r) {
      results.push([...chosen])
      return
    }
    for (let i = start; i < n; i++) {
      chosen.push(items[i])
      walk(i + 1, chosen)
      chosen.pop()
    }
  }
  if (r <= n && r >= 0) walk(0, [])
  return results
}

function consecutiveSequenceFan(shapes: MeldShape[]): FanEntry | null {
  const sequences = shapes.filter((shape) => shape.baseKind === 'sequence')
  let best = 0
  for (const count of [4, 3]) {
    for (const selected of combinations(sequences, count)) {
      if (new Set(selected.map((shape) => shape.tiles.length)).size !== 1) continue
      if (new Set(selected.map((shape) => Math.abs(shape.numberStep))).size !== 1) continue
      const starts = selected.map((shape) =>
        Math.min(...shape.tiles.map((code) => parseTile(code).number))
      )
      if (arithmetic(starts)) {
        best = count
        break
      }
    }
    if (best) break
  }
  if (best === 4) return entry('四连顺', 6)
  if (best === 3) return entry('三连顺', 3)
  return null
}

function dragonFan(shapes: MeldShape[]): FanEntry | null {
  const sequences = shapes.filter((shape) => shape.baseKind === 'sequence')
  for (const count of [1, 2, 3]) {
    for (const selected of combinations(sequences, count)) {
      if (new Set(selected.map((shape) => Math.abs(shape.numberStep))).size !== 1) continue
      const numbers = selected.flatMap((shape) => shape.tiles.map((code) => parseTile(code).number))
      if (numbers.length === 9 && [...numbers].sort((a, b) => a - b).every((value, index) => value === index + 1)) {
        return entry('一条龙', 3)
      }
    }
  }
  return null
}

function scorePartition(
  concealedPartition: string[][],
  openMelds: OpenMeld[],
  pair: string[],
  flags: WinFlags,
): WinScore {
  const groups: string[][] = concealedPartition.map((group) => [...group])
  for (const meld of openMelds) groups.push([...(meld.tiles || [])])

  const shapes = groups.map(classifyMeld)
  if (shapes.some((shape) => shape === null)) throw new Error('和牌拆解中存在非法牌组')

  const pairTiles = pair.map(parseTile)
  if (pairTiles.length && (pairTiles.length !== 2 || pairTiles[0].number !== pairTiles[1].number)) {
    throw new Error('雀头必须为两张同数字牌')
  }
  const tiles = groups.flatMap((group) => group.map(parseTile)).concat(pairTiles)

  let base = 3 + groups.reduce((sum, group) => sum + Math.max(0, group.length - 3), 0)
  const concealed = openMelds.length === 0
  if (concealed) base += 2

  const fans: FanEntry[] = []

  const cleanSequences = shapes.filter(
    (shape) => shape.baseKind === 'sequence' && shape.colourStep === 0 && !shape.isRainbow
  ).length
  const cleanTriplets = shapes.filter(
    (shape) => shape.baseKind === 'triplet' && shape.tiles.length >= 4
  ).length
  if (cleanSequences) fans.push(entry('清顺', 1, cleanSequences))
  if (cleanTriplets) fans.push(entry('清刻', 1, cleanTriplets))

  const dragon = dragonFan(shapes)
  if (dragon) fans.push(dragon)

  fans.push(...sameShapeFans(shapes, 'triplet', new Map<number, [string, number]>([
    [2, ['双同刻', 2]],
    [3, ['三同刻', 6]],
    [4, ['四同刻', 12]],
  ])))

  fans.push(...sameShapeFans(shapes, 'sequence', new Map<number, [string, number]>([
    [2, ['双同顺', 2]],
    [3, ['三同顺', 6]],
    [4, ['四同顺', 12]],
  ])))

  fans.push(...sameColourLayoutFans(shapes))

  const consecutive = consecutiveSequenceFan(shapes)
  if (consecutive) fans.push(consecutive)

  // 花色计数：
  // - 清一色/双色/三色：按“纯色覆盖”计数。纯色牌覆盖 1 个基础色；
  //   半色牌覆盖相邻 2 个基础色（如 AY=红橙 覆盖 红+橙）。因此
  //   “红+红橙+橙”（1 12 2）为两种颜色→双色；而“红橙+橙+橙黄”
  //   （2 23 4）覆盖 红/橙/黄 三种颜色→三色，不是双色。
  // - 七归一/九归一/光谱/全彩：14 级颜色各自独立计数（7 张 AX 才算
  //   七归一，AX+AY 不能合并）。
  const colourCounts = new Map<number, number>() // 14 级
  const coveredColours = new Set<number>() // 覆盖的基础纯色
  for (const tile of tiles) {
    colourCounts.set(tile.colour, (colourCounts.get(tile.colour) || 0) + 1)
    for (const base of primaryColours(tile.colour)) coveredColours.add(base)
  }
  const maxColourLevelCount = Math.max(0, ...colourCounts.values())
  if (maxColourLevelCount >= 9) fans.push(entry('九归一', 6))
  else if (maxColourLevelCount >= 7) fans.push(entry('七归一', 3))

  const rainbowCount = shapes.filter((shape) => shape.isRainbow).length
  if (rainbowCount >= 2) fans.push(entry('双虹会', 12))
  else if (rainbowCount === 1) fans.push(entry('彩虹', 6))

  const distinctLevels = colourCounts.size
  const colourCoverCount = coveredColours.size
  if (colourCoverCount === 1) {
    fans.push(entry('清一色', 18))
  } else if (distinctLevels === 14) {
    fans.push(entry('全彩', 12))
  } else if (distinctLevels === tiles.length) {
    fans.push(entry('光谱', 6))
  } else if (colourCoverCount === 2) {
    fans.push(entry('双色', 12))
  } else if (colourCoverCount === 3) {
    fans.push(entry('三色', 6))
  }
  if (tiles.length && tiles.every((tile) => tile.colour % 2 === 0)) fans.push(entry('全纯色', 1))
  if (tiles.length && tiles.every((tile) => tile.colour % 2 === 1)) fans.push(entry('全半色', 1))

  const numbers = [...new Set(tiles.map((tile) => tile.number))].sort((a, b) => a - b)
  const allTriplets = shapes.length > 0 && shapes.every((shape) => shape.baseKind === 'triplet')
  if (numbers.length === 1) fans.push(entry('清一数', 18))
  else if (numbers.length === 2) fans.push(entry('二数', 12))
  else if (numbers.length === 3 && arithmetic(numbers)) fans.push(entry('三数', 6))
  else if (numbers.length === 4 && arithmetic(numbers)) fans.push(entry('四数', 3))

  if (groups.length && groups.every((group) => group.some((code) => [1, 9].includes(parseTile(code).number)))) {
    fans.push(entry('全带幺', 2))
  }

  const isHeavenly = flags.selfDraw && flags.beforeFirstDiscard && concealed
  if (isHeavenly) fans.push(entry('天和', 18))
  else if (concealed) fans.push(entry('门清', 1))
  if (flags.selfDraw && flags.wallEmpty) fans.push(entry('海底', 2))

  if (allTriplets && numbers.length > 2) fans.push(entry('碰碰和', 3))
  // 平和：仅由顺子构成。彩虹组本质也是顺子（长顺子），不影响平和；
  // 因此按 baseKind 判断，彩虹顺子计入平和，彩虹刻子（刻子类）不计。
  if (shapes.length && shapes.every((shape) => shape.baseKind === 'sequence')) fans.push(entry('平和', 1))

  if (groups.length === 1) fans.push(entry('金龙', 6))
  else if (groups.length === 2) fans.push(entry('二金', 3))
  else if (groups.length === 3) fans.push(entry('三金', 1))

  fans.sort((a, b) => b.value - a.value)
  const fanTotal = fans.reduce((sum, fan) => sum + fan.total, 0)

  return {
    partition: concealedPartition.map((group) => [...group]),
    pair: [...pair],
    groups,
    base,
    fans,
    fanTotal,
    points: fanTotal === 0 ? 1 : base * fanTotal,
    concealed,
  }
}

/**
 * Best winning result for a hand + open melds.
 * Returns null when the hand does not win.
 */
export function bestWinResult(hand: string[], openMelds: OpenMeld[], flags: WinFlags): WinScore | null {
  const results: WinScore[] = []
  for (const decomposition of winningDecompositions(hand, openMelds)) {
    results.push(scorePartition(decomposition.groups, openMelds, decomposition.pair, flags))
  }
  if (!results.length) return null
  return results.reduce((best, candidate) => {
    const bestKey = [best.points, best.fanTotal, best.base]
    const candidateKey = [candidate.points, candidate.fanTotal, candidate.base]
    for (let i = 0; i < 3; i++) {
      if (candidateKey[i] !== bestKey[i]) return candidateKey[i] > bestKey[i] ? candidate : best
    }
    return best
  })
}

/** All winning decompositions with their per-partition scores (best first). */
export function allWinResults(hand: string[], openMelds: OpenMeld[], flags: WinFlags): WinScore[] {
  const results = winningDecompositions(hand, openMelds).map((decomposition) =>
    scorePartition(decomposition.groups, openMelds, decomposition.pair, flags)
  )
  results.sort((a, b) =>
    (b.points - a.points) ||
    (b.fanTotal - a.fanTotal) ||
    (b.base - a.base)
  )
  return results
}

// ---------------------------------------------------------------------------
// Meld editing helpers for the calculator UI
// ---------------------------------------------------------------------------

/** Legal group universe: every 3+ tile shape accepted by classifyMeld. */
export function legalGroupUniverse(): string[][] {
  return GROUP_MASKS.map(codesFromMask)
}

/**
 * Tiles that may extend the current partial meld into at least one legal
 * group (sequence/triplet/rainbow).  An empty meld allows every unused tile.
 */
export function meldCandidateTiles(
  picked: string[],
  used: Iterable<string>,
): Set<string> {
  const usedSet = new Set(used)
  const pickedSet = new Set(picked)
  const candidates = new Set<string>()
  if (picked.length === 0) {
    for (const code of DECK) {
      if (!usedSet.has(code)) candidates.add(code)
    }
    return candidates
  }
  let pickedMask = 0n
  try {
    pickedMask = maskFromCodes(picked)
  } catch (_) {
    return candidates
  }
  // 未成组（1~2 张）时只允许补成 3 张合法牌组的候选；已成组（3+ 张）
  // 后允许继续向更长合法牌组延伸。
  for (const groupMask of GROUP_MASKS) {
    if (picked.length < 3 && bitCount(groupMask) !== 3) continue
    if ((groupMask & pickedMask) === pickedMask) {
      for (const code of codesFromMask(groupMask)) {
        if (!usedSet.has(code)) candidates.add(code)
      }
    }
  }
  return candidates
}

/**
 * Infer the meld kind for an editing slot without distinguishing open/
 * concealed melds: sequence family, triplet family or rainbow.
 * Returns null when the group is not yet a legal meld.
 */
export function inferMeldKind(codes: string[]): {
  kind: string
  baseKind: string
  label: string
  isRainbow: boolean
} | null {
  if (codes.length < 3) return null
  const shape = classifyMeld(codes)
  if (!shape) return null
  if (shape.isRainbow) {
    return { kind: 'rainbow', baseKind: shape.baseKind, label: '彩虹', isRainbow: true }
  }
  if (shape.kind === 'triplet') {
    return { kind: 'triplet', baseKind: 'triplet', label: '刻子', isRainbow: false }
  }
  return { kind: 'sequence', baseKind: 'sequence', label: '顺子', isRainbow: false }
}
