/**
 * Hongque 2 (虹雀²) v1.6 hand check and scoring, ported from the
 * authoritative Python implementation:
 *   open_mahjong_server/server/gamestate/game_hongque/
 *     tile.py / rules.py / win_check.py / scoring.py
 *
 * Rulebook 5.1.1: every tile must belong to one and only one legal group
 * (three or more tiles).  There is no pair/head; a hand that leaves a
 * same-number pair outside every group is not a win.
 *
 * All inputs/outputs are plain JSON: tile codes are strings like "AX1".
 * Colour codes: AX AY BX BY CX CY DX DY EX EY FX FY GX GY (14 colour levels),
 * numbers 1..9, 126 unique tiles.
 */

'use strict'

// ---------------------------------------------------------------------------
// Tile helpers
// ---------------------------------------------------------------------------

const LETTERS = 'ABCDEFG'
const COLOUR_CODES = [
  'AX', 'AY', 'BX', 'BY', 'CX', 'CY', 'DX',
  'DY', 'EX', 'EY', 'FX', 'FY', 'GX', 'GY',
]

const TILE_RE = /^([A-Ga-g])([XxYy])([1-9])$/

/** Parse "AX1" style code into { colour: 0..13, number: 1..9, code }. */
function parseTile(code) {
  const match = TILE_RE.exec(String(code || '').trim())
  if (!match) throw new Error(`非法虹雀牌码: ${code}`)
  const colour = LETTERS.indexOf(match[1].toUpperCase()) * 2 + (match[2].toUpperCase() === 'Y' ? 1 : 0)
  const number = Number(match[3])
  return { colour, number, code: `${match[1].toUpperCase()}${match[2].toUpperCase()}${match[3]}` }
}

/** Primary base colours covered by a colour level (half colours cover two). */
function primaryColours(colour) {
  const base = Math.floor(colour / 2)
  if (colour % 2 === 0) return [base]
  return [base, (base + 1) % 7]
}

function colourCodes() {
  return COLOUR_CODES
}

function fullDeck() {
  const deck = []
  for (let colour = 0; colour < 14; colour++) {
    for (let number = 1; number <= 9; number++) {
      deck.push(`${COLOUR_CODES[colour]}${number}`)
    }
  }
  return deck
}

// ---------------------------------------------------------------------------
// Meld classification (rules.classify_meld)
// ---------------------------------------------------------------------------

function cyclicProgression(values, step) {
  if (!values.length) return false
  const counts = {}
  for (const value of values) counts[value] = (counts[value] || 0) + 1
  if (Object.values(counts).some((count) => count > 1)) return false
  for (const start of values) {
    const expected = new Set()
    for (let offset = 0; offset < values.length; offset++) {
      expected.add(((start + step * offset) % 14 + 14) % 14)
    }
    if (expected.size === values.length && values.every((value) => expected.has(value))) return true
  }
  return false
}

function orderedColourProgression(tiles, step, numberStep) {
  const ordered = [...tiles].sort((a, b) => a.number - b.number || a.colour - b.colour)
  if (numberStep < 0) ordered.reverse()
  for (let i = 0; i + 1 < ordered.length; i++) {
    const diff = ((ordered[i + 1].colour - ordered[i].colour) % 14 + 14) % 14
    if (diff !== step) return false
  }
  return true
}

/**
 * Classify a group of 3+ unique tiles into a meld shape.
 * Returns null when the group is not a legal Hongque meld.
 */
function classifyMeld(codes) {
  const parsed = [...new Set(codes.map(parseTile))]
  if (parsed.length !== codes.length || parsed.length < 3) return null
  const normalized = parsed.sort((a, b) => a.number - b.number || a.colour - b.colour)
  const numbers = normalized.map((tile) => tile.number)
  const colours = normalized.map((tile) => tile.colour)

  const primary = new Set()
  for (const colour of colours) {
    for (const base of primaryColours(colour)) primary.add(base)
  }
  const rainbow = primary.size === 7

  // Same number: cyclic colour progression with step 1 or 2 -> triplet family.
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

  // Different numbers in an arithmetic progression -> sequence family.
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
// Precomputed legal group universe (group_index.py)
// ---------------------------------------------------------------------------

const DECK = fullDeck()
const DECK_INDEX = {}
DECK.forEach((code, index) => {
  DECK_INDEX[code] = index
})

function tileMask(colour, number) {
  return 1n << BigInt(DECK_INDEX[`${COLOUR_CODES[colour]}${number}`])
}

function generateGroupMasks() {
  const groups = new Set()

  // Triplets: same number, cyclic colour step 1 (length 3..14) or 2 (3..7).
  for (let number = 1; number <= 9; number++) {
    for (const [colourStep, maxLength] of [[1, 14], [2, 7]]) {
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

  // Sequences: arithmetic number step, colour step 0/1/2 in the same direction.
  for (const numberStep of [-4, -3, -2, -1, 1, 2, 3, 4]) {
    for (let startNumber = 1; startNumber <= 9; startNumber++) {
      const numbers = []
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

  return [...groups]
    .sort((a, b) => (a.toString(2).replace(/0/g, '').length - b.toString(2).replace(/0/g, '').length) || (a < b ? -1 : 1))
}

const GROUP_MASKS = generateGroupMasks()

function codesFromMask(mask) {
  const codes = []
  for (let index = 0; index < DECK.length; index++) {
    if ((mask >> BigInt(index)) & 1n) codes.push(DECK[index])
  }
  return codes
}

function maskFromCodes(codes) {
  let mask = 0n
  for (const code of codes) {
    const parsed = parseTile(code)
    const bit = 1n << BigInt(DECK_INDEX[parsed.code])
    if (mask & bit) throw new Error(`虹雀牌不可重复: ${parsed.code}`)
    mask |= bit
  }
  return mask
}

/** All legal groups contained in a hand mask, sorted by ascending size. */
function groupsWithin(mask) {
  const result = []
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
function groupsByMinMask(groupCache) {
  const buckets = new Map()
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

function partitionMasks(mask, buckets) {
  if (mask === 0n) return [[]]
  const anchor = mask & -mask
  const results = []
  for (const groupMask of buckets.get(anchor) || []) {
    if ((groupMask & mask) === groupMask) {
      for (const tail of partitionMasks(mask ^ groupMask, buckets)) {
        results.push([groupMask, ...tail])
      }
    }
  }
  return results
}

/**
 * All winning decompositions of a hand plus open melds.
 * Each decomposition is { groups: [[codes...], ...], pair: [] }.
 */
function winningDecompositions(hand, openMelds = []) {
  let codes
  try {
    codes = hand.map((code) => parseTile(code).code)
  } catch (_) {
    return []
  }
  if (new Set(codes).size !== codes.length) return []

  const results = []
  const seen = new Set()
  const mask = maskFromCodes(codes)
  const buckets = groupsByMinMask(groupsWithin(mask))

  for (const partition of partitionMasks(mask, buckets)) {
    if (partition.length || openMelds.length) {
      const groups = partition.map(codesFromMask)
      const key = JSON.stringify(
        groups
          .map((group) => [...group].sort())
          .sort((a, b) => (a[0] < b[0] ? -1 : 1))
      )
      if (seen.has(key)) continue
      seen.add(key)
      results.push({ groups, pair: [] })
    }
  }
  return results
}

function isWinningHand(hand, openMelds = []) {
  try {
    return winningDecompositions(hand, openMelds).length > 0
  } catch (_) {
    return false
  }
}

// ---------------------------------------------------------------------------
// Scoring (scoring.py)
// ---------------------------------------------------------------------------

function entry(name, value, count = 1) {
  return { name, value, count, total: value * count }
}

function arithmetic(values) {
  const ordered = [...new Set(values)].sort((a, b) => a - b)
  if (ordered.length !== values.length || ordered.length < 2) return false
  const step = ordered[1] - ordered[0]
  return step > 0 && ordered.every((value, index) => index === 0 || value - ordered[index - 1] === step)
}

function orderedTiles(shape) {
  const tiles = shape.tiles.map(parseTile)
  if (shape.baseKind === 'sequence') {
    return tiles.sort((a, b) => (shape.numberStep < 0 ? b.number - a.number : a.number - b.number))
  }
  return tiles.sort((a, b) => a.colour - b.colour)
}

function sameShapeFan(shapes, baseKind, names) {
  const buckets = new Map()
  for (const shape of shapes) {
    if (shape.baseKind !== baseKind) continue
    const key = JSON.stringify(shape.tiles.map((code) => parseTile(code).number).sort((a, b) => a - b))
    buckets.set(key, (buckets.get(key) || 0) + 1)
  }
  const maximum = Math.max(0, ...buckets.values())
  const eligible = [...names.keys()].filter((count) => maximum >= count).sort((a, b) => b - a)[0]
  if (!eligible) return null
  const [name, value] = names.get(eligible)
  return entry(name, value)
}

function sameColourLayoutFan(shapes) {
  const buckets = new Map()
  for (const shape of shapes) {
    const ordered = orderedTiles(shape)
    const key = JSON.stringify([ordered.length, ordered.map((tile) => tile.colour)])
    buckets.set(key, (buckets.get(key) || 0) + 1)
  }
  const maximum = Math.max(0, ...buckets.values())
  if (maximum >= 4) return entry('四同花', 12)
  if (maximum >= 3) return entry('三同花', 6)
  if (maximum >= 2) return entry('双同花', 2)
  return null
}

function consecutiveSequenceFan(shapes) {
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

function dragonFan(shapes) {
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

/** itertools.combinations(iterable, r), small helpers only. */
function combinations(items, r) {
  const results = []
  const n = items.length
  function walk(start, chosen) {
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

function scorePartition(concealedPartition, openMelds, pair, flags) {
  const groups = concealedPartition.map((group) => [...group])
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

  const fans = []

  // 清顺：纯色（colour_step 0）且非彩虹的顺子，按组复计；清刻：四张以上的纯色刻子。
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

  const sameTriplet = sameShapeFan(shapes, 'triplet', new Map([
    [2, ['双同刻', 2]],
    [3, ['三同刻', 6]],
    [4, ['四同刻', 12]],
  ]))
  if (sameTriplet) fans.push(sameTriplet)

  const sameSequence = sameShapeFan(shapes, 'sequence', new Map([
    [2, ['双同顺', 2]],
    [3, ['三同顺', 6]],
    [4, ['四同顺', 12]],
  ]))
  if (sameSequence) fans.push(sameSequence)

  const sameColour = sameColourLayoutFan(shapes)
  if (sameColour) fans.push(sameColour)

  const consecutive = consecutiveSequenceFan(shapes)
  if (consecutive) fans.push(consecutive)

  // 花色计数：
  // - 清一色/双色/三色：按“纯色覆盖”计数。纯色牌覆盖 1 个基础色；
  //   半色牌覆盖相邻 2 个基础色（如 AY=红橙 覆盖 红+橙）。因此
  //   “红+红橙+橙”（1 12 2）为两种颜色→双色；而“红橙+橙+橙黄”
  //   （2 23 4）覆盖 红/橙/黄 三种颜色→三色，不是双色。
  // - 七归一/九归一/光谱/全彩：14 级颜色各自独立计数（7 张 AX 才算
  //   七归一，AX+AY 不能合并）。
  const colourCounts = new Map() // 14 级
  const coveredColours = new Set() // 覆盖的基础纯色
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

  // 全带幺：每组牌均含数字 1 或 9 的牌（按“牌组”判定，而非全体手牌）。
  if (groups.length && groups.every((group) => group.some((code) => [1, 9].includes(parseTile(code).number)))) {
    fans.push(entry('全带幺', 2))
  }

  const isHeavenly = flags.selfDraw && flags.beforeFirstDiscard && concealed
  if (isHeavenly) fans.push(entry('天和', 18))
  else if (concealed) fans.push(entry('门清', 1))
  if (flags.selfDraw && flags.wallEmpty) fans.push(entry('海底', 2))

  // 清一数明确“不计碰碰和”；清一数、二数均不计碰碰和。
  if (allTriplets && numbers.length > 2) fans.push(entry('碰碰和', 3))
  // 平和：仅由顺子构成；虹牌是独立牌组，不计入顺子。
  if (shapes.length && shapes.every((shape) => shape.kind === 'sequence')) fans.push(entry('平和', 1))

  if (groups.length === 1) fans.push(entry('金龙', 6))
  else if (groups.length === 2) fans.push(entry('二金', 3))
  else if (groups.length === 3) fans.push(entry('三金', 1))

  // 番种从大到小展示（稳定排序，保持 Python 的插入序）。
  fans.sort((a, b) => b.value - a.value)
  const fanTotal = fans.reduce((sum, fan) => sum + fan.total, 0)

  return {
    partition: concealedPartition.map((group) => [...group]),
    pair: [...pair],
    groups,
    base,
    fans,
    fanTotal,
    // Rulebook 1.6: 0 番和牌恰好 1 分；否则 分数 = 底分 × 番数合计。
    points: fanTotal === 0 ? 1 : base * fanTotal,
    concealed,
  }
}

/**
 * Best winning result for a hand + open melds.
 * Returns null when the hand does not win.
 */
function bestWinResult(hand, openMelds, flags) {
  const results = []
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
function allWinResults(hand, openMelds, flags) {
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

module.exports = {
  parseTile,
  primaryColours,
  classifyMeld,
  fullDeck,
  colourCodes,
  winningDecompositions,
  isWinningHand,
  bestWinResult,
  allWinResults,
  DECK,
  GROUP_MASKS,
}
