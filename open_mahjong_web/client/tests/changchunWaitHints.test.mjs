import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'

const compiled = await build({ entryPoints: [new URL('../src/game2d/calc/changchun/index.ts', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')], bundle: true, platform: 'node', format: 'esm', write: false, logLevel: 'silent' })
const { basicWaits, qualifiedWaits, basicFan, isQualified, buildChangchunWaitData, meldTiles } = await import(`data:text/javascript;base64,${Buffer.from(compiled.outputFiles[0].text).toString('base64')}`)
const ready = [11,12,13,21,22,23,31,32,33,45,45,45,19]
const context = (hand, combinations = [], extra = {}) => ({ hand, combinations, playerIndex: 0, seatDiscards: [[],[],[],[]], seatCombinations: [combinations,[],[],[]], ...extra })

test('a nonwaiting hand has no wait rows or stale fan labels', () => {
  const hand = [11,12,13,21,22,23,41,42,43,44,45,46,47]
  assert.deepEqual(basicWaits(hand), [])
  assert.deepEqual(qualifiedWaits(hand), [])
  assert.equal(buildChangchunWaitData(context(hand)), null)
})

test('basic waits remain visible without yaojiu, three suits or a pung', () => {
  for (const [hand, waits] of [
    [[12,13,14,22,23,24,32,33,34,16,16,16,18],[17,18]],
    [[11,12,13,14,15,16,21,22,23,25,25,25,29],[29]],
    [[11,12,13,14,15,16,21,22,23,31,32,33,19],[19]],
  ]) {
    assert.deepEqual(basicWaits(hand), waits)
    assert.deepEqual(qualifiedWaits(hand), [])
    const data = buildChangchunWaitData(context(hand))
    assert.deepEqual(data.details.map(row => [row.tile, row.label, row.kind]), waits.map(t => [t,'未满足','wuyi']))
  }
})

test('mixed qualified and unmet waits do not lower the legal narrow-wait fan', () => {
  const hand = [12,13,22,23,24,32,33,34,16,16,16,28,28]
  assert.deepEqual(basicWaits(hand), [11,14])
  assert.deepEqual(qualifiedWaits(hand), [11])
  assert.equal(basicFan([...hand,11],[],11), 2)
  assert.deepEqual(buildChangchunWaitData(context(hand)).details.map(row => [row.tile,row.label,row.kind]), [[11,'2番','dianhe'],[14,'未满足','wuyi']])
})

test('ordinary fan, seven pairs, luxury pairs and the dragon-pair exception use Changchun values', () => {
  assert.equal(basicFan([...ready,19],[],19), 2)
  assert.equal(basicFan([24,24,19,19,28,28,29,29,37,37,38,38,39,39],[],19), 3)
  assert.equal(basicFan([11,11,11,11,22,22,33,33,19,19,29,29,39,39],[],11), 4)
  assert.equal(isQualified([11,12,13,14,15,16,21,22,23,31,32,33,45,45]), true)
  assert.equal(basicFan([19,19],['k24','k29','k39','k38'],19), 2)
})

test('legal zero fan is displayed as qualified, not 未满足', () => {
  const hand = [22,23,24,31,31,31,25,26,35,35]
  const row = buildChangchunWaitData(context(hand,['s12'])).details.find(row => row.tile === 27)
  assert.deepEqual([row.base_f,row.selfdrawn_f,row.label,row.kind], [0,0,'0番','dianhe'])
})

test('discard preview preserves the same qualified/unmet labels and counts the pending discard', () => {
  const hand = [12,13,22,23,24,32,33,34,16,16,16,28,28,14]
  const data = buildChangchunWaitData(context(hand), true)
  const preview = data.details.find(row => row.discard_tile === 14)
  assert.deepEqual(preview.adds.map(row => [row.tile,row.label,row.kind]), [[11,'2番','dianhe'],[14,'未满足','wuyi']])
  assert.equal(preview.adds.find(row => row.tile === 14).remaining_count, 3)
  assert.equal(buildChangchunWaitData(context(hand), false), null)
})

test('special melds qualify using logical tiles but count physical tiles and four-copy supply', () => {
  const code = 'Cyao:31,31,31:11,21,31', hand = [14,15,16,17,18,19,45,45,45,47]
  assert.deepEqual(meldTiles(code), [31,31,31])
  assert.deepEqual(meldTiles(code,true), [11,21,31])
  const row = buildChangchunWaitData(context(hand,[code])).details.find(row => row.tile === 47)
  assert.deepEqual([row.base_f,row.kind], [1,'dianhe'])
  assert.deepEqual(basicWaits([31,31,31,21,22,23,11,12,13,45],[code]), [])
  assert.equal(meldTiles('Cwind:31,31,31:41,41,43'), null)
})

test('remaining counts use public physical melds, own concealed kongs and visible indicators', () => {
  assert.equal(buildChangchunWaitData(context(ready,[],{ knownTiles:[19],seatDiscards:[[],[19],[],[]] })).details[0].remaining_count, 1)
  assert.equal(buildChangchunWaitData(context(ready,[],{ seatCombinations:[[],['G19'],[],[]] })).details[0].remaining_count, 3)
  assert.equal(buildChangchunWaitData(context(ready,[],{ seatCombinations:[[],['g19'],[],[]] })).details[0].remaining_count, 0)
})

test('normal one bamboo is not a joker; cached arrays and caller hand remain independent', () => {
  const hand = [...ready], melds = []
  const a = basicWaits(hand,melds); a.push(31)
  assert.deepEqual(basicWaits(hand,melds), [19])
  assert.deepEqual(hand, ready)
  assert.equal(isQualified([...hand,31]), false)
})
