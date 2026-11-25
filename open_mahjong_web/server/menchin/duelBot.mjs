import { canWin, completedShape, countsOf } from './duel.mjs'

const powers = Array.from({ length: 9 }, (_, i) => 5 ** i)
const kongCost = { anGang: 4, mingGang: 3, jiaGang: 1 }

function buildShantenTable() {
  const distance = new Uint8Array(5 ** 9)
  const counts = Array(9).fill(0)
  let frontier = []
  function seedWinningHands(index, size, key) {
    if (index === 9) {
      const openMelds = (14 - size) / 3
      if (Number.isInteger(openMelds) && openMelds <= 4 && completedShape(counts, openMelds)) {
        distance[key] = 1
        frontier.push(key)
      }
      return
    }
    for (let n = 0; n <= 4 && size + n <= 14; n++) {
      counts[index] = n
      seedWinningHands(index + 1, size + n, key + n * powers[index])
    }
  }
  seedWinningHands(0, 0, 0)
  for (let step = 1; frontier.length; step++) {
    const next = []
    const remove = step % 2 === 1
    for (const key of frontier) {
      let digits = key
      for (const power of powers) {
        const copies = digits % 5
        digits = Math.floor(digits / 5)
        if (remove ? copies === 0 : copies === 4) continue
        const neighbor = remove ? key - power : key + power
        if (distance[neighbor]) continue
        distance[neighbor] = step + 1
        next.push(neighbor)
      }
    }
    frontier = next
  }
  return distance
}

const shantenTable = buildShantenTable()
const shanten = key => Math.floor(shantenTable[key] / 2) - 1

export function chooseBotMove(view) {
  const { actions, hand, lastDiscard, turn } = view
  const open = view.melds[turn].length
  if (actions.some(a => a.type === 'win')) {
    const complete = view.phase === 'discard' ? hand : [...hand, lastDiscard.tile]
    if (canWin(complete, open)) return { type: 'win' }
  }
  if (view.phase === 'rob') return { type: 'pass' }
  const counts = countsOf(hand)
  const unseen = counts.map(n => 4 - n)
  for (const { tile, claimed } of view.rivers.flat()) {
    if (!claimed) unseen[tile - 11]--
  }
  for (const meld of view.melds.flat()) {
    for (const tile of meld.tiles) {
      if (tile !== 0) unseen[tile - 11]--
    }
  }

  function handScore(tiles) {
    const key = tiles.reduce((sum, count, i) => sum + count * powers[i], 0)
    const steps = shanten(key)
    let improving = 0
    for (let i = 0; i < 9; i++) {
      if (unseen[i] && shanten(key + powers[i]) < steps) improving += unseen[i]
    }
    const effectiveSteps = improving ? steps : steps + 1
    return improving - 36 * effectiveSteps
  }

  const pass = actions.find(a => a.type === 'pass')
  let choice = pass
  let best = pass ? handScore(counts) : -Infinity
  for (const action of actions) {
    const { type, tile } = action
    if (type === 'win' || type === 'pass') continue
    const after = [...counts]
    let score
    if (type === 'discard') {
      after[tile - 11]--
      score = handScore(after)
    } else if (type === 'chi' || type === 'peng') {
      const rank = lastDiscard.tile - 11
      if (type === 'peng') {
        after[rank] -= 2
      } else {
        for (let suitedTile = tile; suitedTile <= tile + 2; suitedTile++) {
          if (suitedTile !== lastDiscard.tile) after[suitedTile - 11]--
        }
      }
      score = -Infinity
      for (let i = 0; i < 9; i++) {
        if (!after[i]) continue
        after[i]--
        score = Math.max(score, handScore(after))
        after[i]++
      }
    } else {
      const rank = (tile ?? lastDiscard.tile) - 11
      after[rank] -= kongCost[type]
      score = handScore(after)
    }
    if (score > best || (score === best && kongCost[type])) {
      best = score
      choice = action
    }
  }
  return choice
}
