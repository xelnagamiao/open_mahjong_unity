import test from 'node:test'
import assert from 'node:assert/strict'
import { bestWinResult, classifyMeld } from '../src/game2d/calc/hongque/index.ts'
import { predictWinResults, winningTileHints } from '../src/game2d/calc/hongque/prediction.ts'

const flags = { selfDraw: true, beforeFirstDiscard: false, wallEmpty: false }
const codes = text => text.split(' ').filter(Boolean)
const meldsOf = (...groups) => groups.map(group => ({ tiles: codes(group) }))
const screenshotHand = codes('AX2 CY1 DY1 FX2 GX2')
const screenshotMelds = meldsOf('BY2 CY2 DY2', 'EY1 FY1 GY1')

function verifyPath(hand, melds, score) {
  const remaining = [...hand]
  const current = structuredClone(melds)
  for (const step of score.kongSteps) {
    assert.ok(remaining.includes(step.tile))
    const previous = current[step.meldIndex].tiles
    assert.deepEqual(new Set(step.tiles), new Set([...previous, step.tile]))
    assert.equal(step.tiles.length, previous.length + 1)
    assert.ok(classifyMeld(step.tiles), 'Each individual kong must be legal')
    remaining.splice(remaining.indexOf(step.tile), 1)
    current[step.meldIndex].tiles = step.tiles
  }
  assert.equal(bestWinResult(remaining, current, flags)?.points, score.points)
  assert.deepEqual(score.groups.flat().sort(), [...hand, ...melds.flatMap(meld => meld.tiles)].sort())
}

test('reported screenshot: three consecutive kongs produce 132 points, exclusively by self draw', () => {
  const hand = [...screenshotHand, 'AY1']
  const before = structuredClone({ hand, melds: screenshotMelds })
  assert.equal(bestWinResult(hand, screenshotMelds, flags), null)
  const [score] = predictWinResults(hand, screenshotMelds, flags)
  assert.deepEqual([score.base, score.fanTotal, score.points], [6, 22, 132])
  assert.equal(score.kongSteps.length, 3)
  verifyPath(hand, screenshotMelds, score)
  assert.deepEqual(predictWinResults(hand, screenshotMelds, { ...flags, selfDraw: false }), [])
  assert.deepEqual({ hand, melds: screenshotMelds }, before)
})

test('reported game: preserve direct scores and include complete predictions across exposed groups', () => {
  const hints = winningTileHints(screenshotHand, screenshotMelds, flags)
  assert.deepEqual(hints.map(hint => [hint.tile, hint.selfDrawScore.points, hint.selfDrawOnly]), [
    ['AY1', 132, true], ['AY2', 102, true], ['BX2', 102, true], ['BY1', 132, false],
    ['DX1', 48, false], ['EX2', 102, true], ['EY2', 102, true],
  ])
  assert.equal(hints.find(hint => hint.tile === 'BY1').ronScore.points, 54)
  assert.equal(hints.find(hint => hint.tile === 'DX1').ronScore.points, 48)
  for (const hint of hints) verifyPath([...screenshotHand, hint.tile], screenshotMelds, hint.selfDrawScore)
})

test('empty hand with twelve exposed tiles predicts legal extension draws and never ron', () => {
  const melds = meldsOf('AX1 AX2 AX3', 'BX1 BX2 BX3', 'CX1 CX2 CX3', 'DX1 DX2 DX3')
  const before = structuredClone(melds)
  const hints = winningTileHints([], melds, flags)
  assert.deepEqual(hints.map(hint => hint.tile), ['AX4', 'BX4', 'CX4', 'DX4'])
  assert.ok(hints.every(hint => hint.selfDrawOnly && hint.ronScore === null))
  for (const hint of hints) verifyPath([hint.tile], melds, hint.selfDrawScore)
  assert.deepEqual(melds, before)
})

test('prediction can chain across multiple melds and consume the entire hand', () => {
  const hand = codes('AX4 AX5 BX4')
  const melds = meldsOf('AX1 AX2 AX3', 'BX1 BX2 BX3')
  const results = predictWinResults(hand, melds, flags)
  assert.ok(results.length)
  assert.equal(results[0].kongSteps.length, 3)
  assert.deepEqual(new Set(results[0].kongSteps.map(step => step.meldIndex)), new Set([0, 1]))
  for (const score of results) verifyPath(hand, melds, score)
  assert.deepEqual(predictWinResults(hand, melds, { ...flags, selfDraw: false }), [])
})

test('missing connector must be drawn before a held tile can be added', () => {
  const hints = winningTileHints(['AX5'], meldsOf('AX1 AX2 AX3'), flags)
  assert.deepEqual(hints.map(hint => hint.tile), ['AX4'])
  assert.deepEqual(hints[0].selfDrawScore.kongSteps.map(step => step.tile), ['AX4', 'AX5'])
})

test('a legal final superset cannot bypass illegal intermediate kongs', () => {
  const melds = meldsOf('AX1 AX3 AX5')
  assert.ok(classifyMeld(codes('AX1 AX2 AX3 AX4 AX5')))
  assert.deepEqual(predictWinResults(codes('AX2 AX4'), melds, flags), [])
  assert.equal(winningTileHints(['AX2'], melds, flags).some(hint => hint.tile === 'AX4'), false)
})

test('direct wins preserve their score and a tile can support ron and a higher self draw prediction', () => {
  const hand = codes('AX4 AX5 AX6')
  const melds = meldsOf('AX1 AX2 AX3')
  const direct = bestWinResult(hand, melds, flags)
  const results = predictWinResults(hand, melds, flags)
  assert.ok(results.some(score => score.kongSteps.length === 0 && score.points === direct.points))
  assert.deepEqual(predictWinResults(hand, melds, { ...flags, selfDraw: false }).map(score => score.kongSteps), [[]])
  const hint = winningTileHints(codes('AX4 AX5'), melds, flags).find(item => item.tile === 'AX6')
  assert.equal(hint.selfDrawOnly, false)
  assert.ok(hint.ronScore)
  assert.ok(hint.selfDrawScore.points > hint.ronScore.points)
})

test('prediction excludes used tiles, honors scoring flags, and rejects malformed input', () => {
  const melds = meldsOf('AX1 AX2 AX3')
  assert.deepEqual(winningTileHints([], melds, flags, ['AX4']), [])
  const normal = predictWinResults(['AX4'], melds, flags)[0]
  const haidi = predictWinResults(['AX4'], melds, { ...flags, wallEmpty: true })[0]
  assert.ok(haidi.points > normal.points)
  assert.ok(haidi.fans.some(fan => fan.name === '海底'))
  assert.throws(() => predictWinResults(['AX1'], melds, flags), /重复/)
  assert.throws(() => predictWinResults([], meldsOf('AX1 AX2 AX5'), flags), /非法/)
  assert.throws(() => winningTileHints(['bad'], [], flags), /非法/)
  assert.deepEqual(winningTileHints([], [], flags), [])
  assert.deepEqual(winningTileHints([], meldsOf('AX1 AX2 AX3 AX4 AX5 AX6 AX7', 'BX1 BX2 BX3 BX4 BX5 BX6 BX7'), flags), [])
})

test('equivalent operation orders are deduplicated and scores are sorted best first', () => {
  const results = predictWinResults(codes('AX4 AX5 AX6 BX4'), meldsOf('AX1 AX2 AX3', 'BX1 BX2 BX3'), flags)
  const keys = results.map(score => JSON.stringify(score.groups.map(group => [...group].sort()).sort()))
  assert.equal(new Set(keys).size, keys.length)
  for (let index = 1; index < results.length; index++) assert.ok(results[index - 1].points >= results[index].points)
})
