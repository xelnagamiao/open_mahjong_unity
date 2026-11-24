export const CHALLENGE_ROUNDS = 50
export const RANKED_ROUNDS = 5
export const ROUND_SECONDS = 20
export const LEVEL_POINTS = 500
export const CHALLENGE_LEVELS = [
  { level: 1, name: '普通', multiplier: 1 },
  { level: 2, name: '含未听牌', multiplier: 1 },
  { level: 3, name: '多面听', multiplier: 2 },
  { level: 4, name: '乱序多面听', multiplier: 3 },
]

export function speedPoints(level, remainingMs) {
  const ticks = Math.ceil(Math.max(0, Math.min(ROUND_SECONDS * 1000, remainingMs)) / 100)
  return ticks * CHALLENGE_LEVELS[level - 1].multiplier
}

const powers = Array.from({ length: 9 }, (_, rank) => 5 ** rank)
const melds = [
  ...powers.map((_, rank) => [rank, rank, rank]),
  ...powers.slice(0, 7).map((_, rank) => [rank, rank + 1, rank + 2]),
]
let handsByDifficulty
let waitMasks

function buildHands() {
  if (waitMasks) return
  waitMasks = new Map()
  handsByDifficulty = [[], [], []]
  const counts = Array(9).fill(0)
  const wins = new Set()

  function recordWin() {
    const key = counts.reduce((value, count, rank) => value + count * powers[rank], 0)
    if (wins.has(key)) return
    wins.add(key)
    counts.forEach((count, rank) => {
      if (!count) return
      const hand = key - powers[rank]
      waitMasks.set(hand, (waitMasks.get(hand) || 0) | (1 << rank))
    })
  }

  function addMelds(start, remaining) {
    if (!remaining) return recordWin()
    for (let i = start; i < melds.length; i++) {
      const tiles = melds[i]
      for (const tile of tiles) counts[tile]++
      if (tiles.every(tile => counts[tile] <= 4)) addMelds(i, remaining - 1)
      for (const tile of tiles) counts[tile]--
    }
  }

  for (let pair = 0; pair < 9; pair++) {
    counts[pair] = 2
    addMelds(0, 4)
    counts[pair] = 0
  }
  for (let first = 0; first < 8; first++) {
    for (let second = first + 1; second < 9; second++) {
      counts.fill(2)
      counts[first] = counts[second] = 0
      recordWin()
    }
  }
  for (const [key, mask] of waitMasks) {
    const size = powers.filter((_, rank) => mask & (1 << rank)).length
    handsByDifficulty[Math.min(2, size - 1)].push(key)
  }
}

export function levelFor(points) {
  return 1 + Math.floor(points / LEVEL_POINTS)
}

export function newQuestion(level = 1, previous = new Set()) {
  buildHands()
  const tier = Math.floor(Math.random() * Math.min(3, Math.max(2, level + 1)))
  return deal(pickKey(handsByDifficulty[tier], previous), previous)
}

function pickKey(choices, previous) {
  let index = Math.floor(Math.random() * choices.length)
  while (previous.has(choices[index])) index = (index + 1) % choices.length
  return choices[index]
}

function deal(key, previous) {
  previous.add(key)
  const firstTile = 11 + 10 * Math.floor(Math.random() * 3)
  const hand = []
  const waits = []
  const mask = waitMasks.get(key) || 0
  for (let rank = 0; rank < 9; rank++) {
    const tile = firstTile + rank
    hand.push(...Array(Math.floor(key / powers[rank]) % 5).fill(tile))
    if (mask & (1 << rank)) waits.push(tile)
  }
  return { hand, waits }
}

function shuffle(tiles) {
  for (let end = tiles.length - 1; end > 0; end--) {
    const index = Math.floor(Math.random() * (end + 1))
    const tile = tiles[index]
    tiles[index] = tiles[end]
    tiles[end] = tile
  }
  return tiles
}

export function challengeQuestion(level, previous) {
  buildHands()
  let key
  if (level === 2 && Math.random() < 0.2) {
    const wall = Array.from({ length: 36 }, (_, i) => powers[Math.floor(i / 4)])
    do {
      key = shuffle(wall).slice(0, 13).reduce((sum, tile) => sum + tile, 0)
    } while (previous.has(key))
  } else {
    const tier = level >= 3 ? 2 : Math.floor(Math.random() * handsByDifficulty.length)
    key = pickKey(handsByDifficulty[tier], previous)
  }
  const question = deal(key, previous)
  if (level === 4) shuffle(question.hand)
  return question
}

export function candidates(hand) {
  const base = Math.floor(hand[0] / 10) * 10 + 1
  return Array.from({ length: 9 }, (_, i) => base + i)
}

export function correctAnswer(selected, waits) {
  return selected.length === waits.length && waits.every(tile => selected.includes(tile))
}
