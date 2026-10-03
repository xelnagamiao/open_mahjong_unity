import test from 'node:test'
import assert from 'node:assert/strict'
import { calculatePaili } from '../src/utils/pailiCalculator.ts'
import { HAND_EXAMPLES, analyzeHand, emptyDraft, cloneDraft, parseHandText, exampleDraft, effectiveCount, validateDraft, expandMeld, draftNotation, setWinMethod, discardTile, drawTile, encodeDraft, decodeDraft } from '../src/utils/handAnalysis.ts'
import { parseNotationText } from '../src/composables/useMahjongTiles.js'

const create = (text, melds = []) => { const draft = emptyDraft(); draft.melds = melds; return parseHandText(text, draft) }
const sortedTiles = groups => groups.flatMap(group => group.tiles).sort((a, b) => a - b)

for (const example of HAND_EXAMPLES) {
  test(`example ${example.id}: valid, immutable and shareable`, () => {
    const draft = exampleDraft(example.id)
    const before = JSON.stringify(draft)
    const result = analyzeHand(draft)
    assert.ok(['ready', 'discard', 'complete'].includes(result.state))
    assert.equal(JSON.stringify(draft), before)
    assert.deepEqual(decodeDraft(encodeDraft(draft)), draft)
    assert.deepEqual(parseHandText(draftNotation(draft), draft).hand.sort((a, b) => a - b), [...draft.hand].sort((a, b) => a - b))
  })
}

test('13 tiles: both ron and tsumo scores for each wait', () => {
  const result = analyzeHand(exampleDraft('ready'))
  assert.equal(result.state, 'ready')
  assert.deepEqual(result.waits.map(w => [w.tile, w.remaining, w.ron.fan, w.tsumo.fan]), [[21, 4, 21, 23], [24, 4, 20, 22]])
  assert.equal(result.paili.total_accept, 8)
})

test('14 winning tiles: complete shape, fan and every tile in decomposition', () => {
  const draft = exampleDraft('win')
  const result = analyzeHand(draft)
  assert.equal(result.state, 'complete')
  assert.equal(result.decompositions[0].fan, 21)
  assert.equal(result.decompositions[0].canWin, true)
  assert.deepEqual(sortedTiles(result.decompositions[0].groups), [...draft.hand].sort((a, b) => a - b))
})

test('14 non-winning tiles: discard → wait → win without re-entering', () => {
  const initial = exampleDraft('discard')
  const result = analyzeHand(initial)
  assert.equal(result.state, 'discard')
  assert.equal(result.paili.discards[0].discard, 39)
  const ready = discardTile(initial, 39)
  assert.equal(analyzeHand(ready).state, 'ready')
  assert.deepEqual(ready.river, [39])
  const won = drawTile(ready, 21)
  assert.equal(analyzeHand(won).state, 'complete')
  assert.equal(won.winTile, 21)
  assert.equal(initial.hand.length, 14)
})

test('flowers never rescue a below-eight hand', () => {
  const draft = exampleDraft('low')
  draft.context.flowers = 8
  const result = analyzeHand(draft)
  assert.equal(result.state, 'complete')
  assert.equal(result.decompositions[0].fan, 11)
  assert.equal(result.decompositions[0].baseFan, 3)
  assert.equal(result.decompositions[0].canWin, false)
})

test('mixed seven-pairs/normal hand keeps all shapes and deduplicates permutations', () => {
  const result = analyzeHand(exampleDraft('pairs'))
  assert.deepEqual(result.shapes, ['七对', '一般型'])
  assert.equal(result.decompositions[0].fan, 88)
  assert.equal(result.decompositions.length, 4)
  for (const item of result.decompositions) assert.equal(sortedTiles(item.groups).length, 14)
})

test('thirteen-sided orphan wait and completed special hand', () => {
  const draft = exampleDraft('orphans')
  assert.equal(analyzeHand(draft).waits.length, 13)
  const won = drawTile(draft, 11)
  const result = analyzeHand(won)
  assert.equal(result.state, 'complete')
  assert.equal(result.decompositions[0].fan, 88)
  assert.equal(result.decompositions[0].groups[0].tiles.length, 14)
  assert.deepEqual(result.shapes, ['十三幺'])
})

test('knitted straight and all-unrelated show all fourteen physical tiles', () => {
  for (const text of ['1147m258p369s111z + 1m', '147m258p3s123456z + 7z']) {
    const draft = create(text)
    const result = analyzeHand(draft)
    assert.equal(result.state, 'complete', text)
    assert.ok(result.shapes.some(s => ['组合龙', '全不靠'].includes(s)), text)
    assert.deepEqual(sortedTiles(result.decompositions[0].groups), [...draft.hand].sort((a, b) => a - b))
  }
})

test('MCR seven pairs allows a quadruplet; special hands disappear when open', () => {
  const result = analyzeHand(create('1111223344556m + 6m'))
  assert.ok(result.shapes.includes('七对'))
  const open = analyzeHand(create('123456m789p1z + 1z', ['k47']))
  assert.ok(!open.shapes.includes('七对'))
})

test('kong uses three effective tiles but counts four physical copies', () => {
  const draft = create('123456m789p5z + 5z', ['G41'])
  assert.equal(effectiveCount(draft), 14)
  assert.equal(draft.hand.length + draft.melds.flatMap(expandMeld).length, 15)
  const result = analyzeHand(draft)
  assert.equal(result.state, 'complete')
  assert.equal(sortedTiles(result.decompositions[0].groups).length, 15)
  draft.visible.push(41)
  assert.throws(() => analyzeHand(draft), /超过 4 张/)
})

test('four open melds: single pair wait and winning pair', () => {
  const draft = create('5s', ['s12', 's15', 's18', 's22'])
  const result = analyzeHand(draft)
  assert.equal(result.state, 'ready')
  assert.deepEqual(result.waits.map(w => [w.tile, w.remaining]), [[35, 3]])
  assert.equal(analyzeHand(drawTile(draft, 35)).state, 'complete')
})

test('discarded tiles remain visible and are never returned to available counts', () => {
  const draft = exampleDraft('pairs')
  const paili = calculatePaili({ handTiles: draft.hand, combinations: [] })
  for (const row of paili.discards) {
    for (const accept of row.accept) {
      assert.equal(accept.remaining, 4 - draft.hand.filter(tile => tile === accept.tile).length)
    }
  }
  const cut = discardTile(draft, 17)
  const after = analyzeHand(cut)
  assert.equal(after.waits.find(wait => wait.tile === 17).remaining, 2)
  const reapplied = parseHandText(draftNotation(cut), cut)
  assert.deepEqual(reapplied.river, [17], 're-analyzing unchanged text must not return the discard to the wall')
  assert.equal(analyzeHand(reapplied).waits.find(wait => wait.tile === 17).remaining, 2)
})

test('visible tiles reduce waits; dead waits are not offered as draws', () => {
  const draft = exampleDraft('ready')
  draft.visible = [21, 21, 21, 21, 24]
  const result = analyzeHand(draft)
  assert.deepEqual(result.waits.map(w => [w.tile, w.remaining]), [[24, 3]])
  assert.throws(() => drawTile(draft, 21), /超过 4 张/)
  draft.visible.push(24, 24, 24)
  const dead = analyzeHand(draft)
  assert.equal(dead.state, 'ready')
  assert.equal(dead.waits.length, 0)
  assert.equal(dead.paili.total_accept, 0)
})

test('illegal copy counts, malformed melds, counts and flags are rejected', () => {
  for (const text of ['11111m123456789p', '123m++1p', '123m+', '+1p', '123m+11p', '8z', '123x', '123']) assert.throws(() => create(text), undefined, text)
  for (const meld of ['s41', 's11', 'g11x', 'g99', 'x11']) assert.throws(() => create('1m', [meld]))
  assert.throws(() => create('123456789m123456p'), /14 张/)
  const draft = exampleDraft('win')
  draft.context.kongWin = true
  assert.throws(() => analyzeHand(draft), /冲突/)
  draft.context.kongWin = false
  draft.context.flowers = 9
  assert.throws(() => validateDraft(draft), /花牌/)
  assert.throws(() => decodeDraft('%invalid'))
  assert.throws(() => decodeDraft(encodeURIComponent(JSON.stringify({ ...emptyDraft(), version: 2 }))), /数据/)
})

test('incomplete hand is an editing state, not a scoring error', () => {
  assert.deepEqual(analyzeHand(emptyDraft()), { state: 'incomplete', count: 0 })
  assert.deepEqual(analyzeHand(create('123456m')), { state: 'incomplete', count: 6 })
})

test('winning tile is preserved after sorting / link import; changing it changes wait fan', () => {
  const draft = exampleDraft('win')
  assert.equal(draft.winTile, 21)
  assert.equal(decodeDraft(encodeDraft(draft)).winTile, 21)
  assert.equal(parseHandText(draftNotation(draft), draft).winTile, 21)
  const lastInput = create('123456789m123p5s5s')
  assert.equal(lastInput.winTile, 35)
  assert.ok(draftNotation(lastInput).endsWith('+ 5s'))
})

test('switching ron / tsumo clears incompatible flags', () => {
  const draft = exampleDraft('win')
  draft.context.robKong = true
  const self = setWinMethod(draft, 'tsumo')
  assert.equal(self.context.robKong, false)
  self.context.kongWin = true
  const ron = setWinMethod(self, 'ron')
  assert.equal(ron.context.kongWin, false)
  validateDraft(ron)
})

test('invalid transitions fail without changing the input', () => {
  const draft = exampleDraft('ready')
  const before = cloneDraft(draft)
  assert.throws(() => discardTile(draft, 11))
  assert.throws(() => drawTile(exampleDraft('win'), 21))
  assert.deepEqual(draft, before)
})

test('seeded valid hands: returned accept counts and transitions stay consistent', () => {
  let seed = 917
  const random = max => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed % max }
  const ids = parseNotationText('123456789m123456789p123456789s1234567z')
  for (let n = 0; n < 60; n++) {
    const wall = ids.flatMap(tile => [tile, tile, tile, tile])
    const draft = emptyDraft()
    for (let i = 0; i < 14; i++) draft.hand.push(wall.splice(random(wall.length), 1)[0])
    draft.winTile = draft.hand.at(-1)
    const result = analyzeHand(draft)
    for (const row of result.paili.discards) {
      const next = discardTile(draft, row.discard)
      const direct = calculatePaili({ handTiles: next.hand, combinations: [], visibleTiles: next.river })
      assert.equal(row.shanten, direct.shanten)
      assert.deepEqual(row.accept, direct.accept)
    }
  }
})
