import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { changshaWaits, buildChangshaWaitData } from '../src/utils/changshaWaits.js'
const {cases} = JSON.parse(await readFile(new URL('./fixtures/changshaWaits.json',import.meta.url),'utf8'))
test('Changsha shape hints agree with the Python calculator across generated hands and melds', () => {
  for (const row of cases) {
    const actual=changshaWaits(row.hand,row.melds)
    assert.deepEqual(actual.map(({tile,fans})=>({tile,fans})),row.waits,JSON.stringify(row))
    const kamo=changshaWaits(row.hand,row.melds,true)
    assert.deepEqual(kamo.map(w=>w.tile),actual.map(w=>w.tile))
    for (const w of kamo) assert.equal(w.canRon,w.fans.some(f=>f!=='小胡'))
  }
})
test('kamo keeps small-hand self-draw hints and discard previews without leaking hidden counts',()=>{
  const hand=[11,12,13,21,22,23,31,32,33,16,17,18,25]
  const plain=buildChangshaWaitData(hand,[],false,[...hand,25])
  const kamo=buildChangshaWaitData(hand,[],true,[...hand,25])
  assert.deepEqual(plain.details.map(w=>w.tile),[25])
  assert.equal(plain.details[0].kind,'dianhe')
  assert.equal(kamo.details[0].kind,'zimo')
  assert.equal(kamo.details[0].remaining_count,2)
  assert.match(kamo.details[0].label,/仅自摸/)
  const preview=buildChangshaWaitData([...hand,39],[],true,[...hand,39])
  assert.equal(preview.type,'waits_all')
  assert.deepEqual(preview.details.find(row=>row.discard_tile===39).adds.map(w=>w.tile),[25])
  assert.deepEqual(changshaWaits([41],['k11','k22','k33','k19']),[])
})
