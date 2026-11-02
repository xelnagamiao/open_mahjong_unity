import test from 'node:test'
import assert from 'node:assert/strict'
import fixtures from './fixtures/calculatorScoring.json' with { type: 'json' }
import { analyzeHand, emptyDraft, parseHandText, drawTile, expandMeld } from '../src/utils/handAnalysis.ts'
import { ruleOptions, analysisOptions, ruleFromPath, calculatorPath, pailiPath } from '../src/utils/calculatorRules.ts'
import { scoreQingque } from '../src/utils/scoring/qingque.ts'
import { scoreRiichi, riichiPoints } from '../src/utils/scoring/riichi.ts'
import { createGuobiaoCalculator } from '../src/composables/useGuobiaoCalculator.js'

const hand = (text, melds = []) => parseHandText(text, { ...emptyDraft(), melds })
const check = (text, rule, options) => analyzeHand(hand(text), options, rule)
function session(t, extra = {}) {
  const errors = []
  const state = createGuobiaoCalculator({ onError: message => errors.push(message), ...extra })
  t.after(() => state.destroy())
  return { state, errors }
}

test('presets configure winning shapes, with stable matching routes for both pages', () => {
  for (const rule of ['guobiao', 'qingque', 'riichi']) {
    assert.equal(ruleFromPath(calculatorPath(rule)), rule)
    assert.equal(ruleFromPath(pailiPath(rule)), rule)
    assert.deepEqual(ruleOptions(rule), { mcrSevenPairs: rule !== 'riichi', riichiSevenPairs: rule === 'riichi', thirteenOrphans: true, unrelatedTiles: rule === 'guobiao', combinationDragon: rule === 'guobiao' })
  }
  assert.equal(analysisOptions('riichi', { unrelatedTiles: true }).unrelatedTiles, false)
})

for (const rule of ['guobiao', 'qingque', 'riichi']) {
  test(`${rule}: 13 → 14 → discard → draw keeps scoring and shanten connected`, async t => {
    const { state, errors } = session(t)
    state.setRule(rule)
    state.textInput.value = '123456789m23p55s9s'
    assert.equal(await state.calculate(), true)
    assert.equal(state.result.value.state, 'discard')
    assert.equal(await state.applyDiscard(39), true)
    assert.equal(state.result.value.state, 'ready')
    assert.deepEqual(state.result.value.waits.map(row => row.tile), [21,24])
    assert.equal(await state.applyDraw(21), true)
    assert.equal(state.result.value.state, 'complete')
    assert.equal(state.best.value.rule || 'guobiao', rule)
    const before = structuredClone(JSON.parse(JSON.stringify(state.draft.value)))
    assert.equal(state.prepareTransfer(), true)
    assert.deepEqual(state.draft.value, before)
    assert.equal(await state.calculate(), true)
    assert.equal(state.best.value.rule || 'guobiao', rule)
    assert.deepEqual(errors, [])
  })

  test(`${rule}: disabling thirteen orphans affects both waits and completed hands`, () => {
    const ready = hand('19m19p19s1234567z')
    assert.equal(analyzeHand(ready, undefined, rule).waits.length, 13)
    const complete = drawTile(ready, 11)
    assert.equal(analyzeHand(complete, undefined, rule).state, 'complete')
    assert.equal(analyzeHand(complete, { thirteenOrphans: false }, rule).state, 'discard')
    assert.notEqual(analyzeHand(ready, { thirteenOrphans: false }, rule).state, 'ready')
  })

  test(`${rule}: a one-shanten hand advances to ready and then scores without re-entry`, async t => {
    const { state, errors } = session(t)
    state.setRule(rule)
    state.textInput.value = '123456m23p55s11z2z'
    assert.equal(await state.calculate(), true)
    assert.equal(state.result.value.state, 'improve')
    assert.equal(state.pailiResult.value.shanten, 1)
    assert.ok(state.pailiResult.value.accept.some(row => row.tile === 21))
    assert.equal(await state.applyDraw(21), true)
    assert.equal(state.pailiResult.value.best_shanten, 0)
    assert.equal(await state.applyDiscard(42), true)
    assert.equal(state.result.value.state, 'ready')
    assert.deepEqual(state.result.value.waits.map(row => row.tile), [35, 41])
    assert.equal(await state.applyDraw(41), true)
    assert.equal(state.result.value.state, 'complete')
    assert.equal(state.best.value.rule || 'guobiao', rule)
    assert.deepEqual(errors, [])
  })
}

test('Qingque accepts repeated seven pairs; Riichi rejects them and excludes knitted hands', () => {
  // Four identical tiles can count as two pairs only in MCR/Qingque.
  const complete = hand('111122m44p66s77z55s')
  assert.equal(analyzeHand(complete, undefined, 'qingque').state, 'complete')
  assert.equal(analyzeHand(complete, undefined, 'riichi').state, 'discard')
  for (const rule of ['qingque','riichi']) for (const knitted of ['147m258p3s123456z + 7z', '1147m258p369s111z + 1m']) assert.equal(check(knitted, rule).state, 'discard')
  assert.equal(check('1122m44p66s77z55s3z + 3z', 'riichi').state, 'complete')
})

test('Riichi scores have correct points, payments, fu, yaku and no eight-fan restriction', () => {
  const d = hand('123456789m23p55s + 1p')
  let best = analyzeHand(d, undefined, 'riichi').decompositions[0]
  assert.deepEqual([best.fan,best.fu,best.points,best.canWin], [3,30,5800,true])
  d.context.seat = '南'
  best = analyzeHand(d, undefined, 'riichi').decompositions[0]
  assert.equal(best.points,3900)
  d.context.method = 'tsumo'
  best = analyzeHand(d, undefined, 'riichi').decompositions[0]
  assert.deepEqual([best.fan,best.fu,best.points], [4,20,5200])
  assert.match(best.payment,/庄家 2600 点，闲家各 1300 点/)
  assert.equal(riichiPoints(13,30,false,false).points,32000)
  assert.equal(riichiPoints(26,25,true,true,2).points,96000)
})

test('Riichi bonus tiles never replace a yaku; riichi, ippatsu and ura work on closed hands', () => {
  const d = hand('123m456p678s12s22p + 3s')
  d.context.dora = 5
  let best = analyzeHand(d, undefined, 'riichi').decompositions[0]
  assert.equal(best.canWin,false)
  assert.equal(best.points,0)
  d.context.riichi = 1; d.context.ippatsu = true; d.context.uraDora = 2
  best = analyzeHand(d, undefined, 'riichi').decompositions[0]
  assert.equal(best.canWin,true)
  assert.equal(best.fan,9)
  assert.ok(best.fanNames.includes('里宝牌（2 番）'))
  d.context.riichi = 0
  best = analyzeHand(d, undefined, 'riichi').decompositions[0]
  assert.ok(!best.fanNames.some(name => /一发|里宝/.test(name)))
})

test('Riichi white/green dragon mapping, green hand and red-five notation are correct', () => {
  const green = check('222333444666s6z + 6z','riichi').decompositions[0]
  const white = check('222333444666s5z + 5z','riichi').decompositions[0]
  assert.ok(green.fanNames.includes('绿一色'))
  assert.ok(!white.fanNames.includes('绿一色'))
  for (const [z, label] of [['5','白'],['6','发'],['7','中']]) {
    const best = analyzeHand(hand(`123m456p789s1z + 1z`, [`k${z==='5'?46:z==='6'?47:45}`]),undefined,'riichi').decompositions[0]
    assert.ok(best.fanNames.includes(`役牌·${label}（1 番）`))
  }
  const red = hand('123406789m23p55s + 1p')
  assert.equal(red.context.redDora,1)
  assert.ok(analyzeHand(red,undefined,'riichi').decompositions[0].fanNames.includes('赤宝牌（1 番）'))
})

test('Qingque precision, base points and context are independent of MCR flower flags', () => {
  const d = hand('123456789m23p55s + 1p')
  const before = analyzeHand(d,undefined,'qingque').decompositions[0]
  assert.deepEqual([before.fan,before.points],[10.7826,116])
  d.context.flowers = 8; d.context.lastCopy = true
  assert.equal(analyzeHand(d,undefined,'qingque').decompositions[0].fan,before.fan)
  d.context.lastTile = true
  assert.equal(analyzeHand(d,undefined,'qingque').decompositions[0].fan,before.fan+4)
})

test('Qingque reflection never carries a negative rank into a different suit', () => {
  const best = check('333678p11999s56s + 4s','qingque').decompositions[0]
  assert.equal(best.fan,3.60603)
  assert.deepEqual(best.fanNames,['门前清'])
})

test('rule changes preserve the hand, reset all switches and restore rule/context/options from storage', async t => {
  const map = new Map()
  const storage = {getItem: key => map.get(key), setItem: (key,value) => map.set(key,value)}
  const { state } = session(t,{storage})
  await state.loadExample('win')
  state.options.thirteenOrphans = false
  const text = state.textInput.value
  for(const rule of ['qingque','riichi','guobiao','riichi']) {
    state.setRule(rule)
    assert.equal(state.best.value,null)
    assert.equal(state.textInput.value,text)
    assert.deepEqual({...state.options},ruleOptions(rule))
    await state.calculate()
  }
  state.form.riichi = 2; state.form.ippatsu = true; state.form.dora = 2
  const { state: restored } = session(t,{storage})
  assert.equal(restored.rule.value,'riichi')
  assert.equal(restored.form.riichi,2)
  assert.equal(restored.form.ippatsu,true)
  assert.deepEqual(restored.draft.value,state.draft.value)
  await restored.calculate()
  assert.ok(restored.best.value.fanNames.includes('双立直（2 番）'))
})

test('rule changes cancel an old worker; fallback uses the selected local rules', async t => {
  const workers=[]
  const {state,errors}=session(t,{workerFactory:()=>{const worker={postMessage(data){this.input=data},terminate(){this.terminated=true}};workers.push(worker);return worker}})
  state.textInput.value='123456789m23p55s + 1p'
  const old=state.calculate()
  state.setRule('riichi')
  assert.equal(workers[0].terminated,true)
  const current=state.calculate()
  workers[0].onmessage({data:{id:workers[0].input.id,result:analyzeHand(workers[0].input.draft)}})
  assert.equal(await old,false)
  assert.equal(state.best.value,null)
  workers[1].onerror()
  assert.equal(await current,true)
  assert.equal(state.best.value.points,5800)
  assert.deepEqual(errors,[])
})

test('offline reference fixtures: Qingque exact fan/names and Riichi han/fu/points/names', () => {
  for(const [index,{draft,expected}] of fixtures.entries()) {
    const qq=scoreQingque(draft,ruleOptions('qingque'))[0]
    assert.equal(qq.fan,expected.qingque.fan,`Qingque ${index}`)
    assert.deepEqual([...qq.fanNames].sort(),[...expected.qingque.names].sort(),`Qingque names ${index}`)
    const riichi=scoreRiichi(draft,ruleOptions('riichi'))[0]
    assert.equal(!!riichi?.canWin,expected.riichi.valid,`Riichi win ${index}`)
    if(expected.riichi.valid) {
      assert.equal(riichi.fan,expected.riichi.fan,`Riichi fan ${index}`)
      assert.equal(riichi.points,expected.riichi.points,`Riichi points ${index}`)
      if(!riichi.yakuman) assert.equal(riichi.fu,expected.riichi.fu,`Riichi fu ${index}`)
      assert.deepEqual([...riichi.fanNames].sort(),expected.riichi.names.map(name=>name.replace(/\((\d+)han\)/,'（$1 番）')).sort(),`Riichi names ${index}`)
    }
    for(const score of [qq,riichi].filter(Boolean)) assert.deepEqual(score.groups.flatMap(group=>group.tiles).sort((a,b)=>a-b), [...draft.hand,...draft.melds.flatMap(expandMeld)].sort((a,b)=>a-b),`physical tiles ${index}`)
  }
})
