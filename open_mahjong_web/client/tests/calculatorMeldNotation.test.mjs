import test from 'node:test'
import assert from 'node:assert/strict'
import { effectScope } from 'vue'
import { emptyDraft, parseHandText, draftNotation, expandMeld, effectiveCount } from '../src/utils/handAnalysis.ts'
import { createGuobiaoCalculator } from '../src/composables/useGuobiaoCalculator.js'
import { initialState, encodeState, decodeState, useUnifiedCalculator } from '../src/composables/useUnifiedCalculator.js'

const parse = (text, draft = emptyDraft()) => parseHandText(text, draft, { replaceMelds: true })
const notation = '567m45s22z(s13)(K23) + 6s'
function calculator(t) {
  const errors = []
  const calc = createGuobiaoCalculator({ onError: message => errors.push(message) })
  t.after(() => calc.destroy())
  return { calc, errors }
}
function unified(t, storage = new Map(), hash = '') {
  Object.defineProperty(globalThis, 'location', { configurable: true, value: { hash, href: 'http://localhost/calc/chinese' + hash } })
  Object.defineProperty(globalThis, 'sessionStorage', { configurable: true, value: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) } })
  const scope = effectScope(), calc = scope.run(useUnifiedCalculator)
  t.after(() => scope.stop())
  return calc
}

test('project notation uses center tile IDs and preserves open/concealed case', () => {
  const draft = parse(notation)
  assert.deepEqual(draft.melds, ['s13', 'K23'])
  assert.deepEqual(expandMeld('s13'), [12,13,14])
  assert.deepEqual(expandMeld('K23'), [23,23,23])
  assert.equal(draft.winTile, 36)
  assert.equal(effectiveCount(draft), 14)
  assert.equal(draftNotation(draft), notation)
  const restored = parse(draftNotation(draft))
  assert.deepEqual(restored, draft)
})

test('all six meld kinds round trip, with actual honor IDs rather than z notation indices', () => {
  for (const code of ['s13', 'S13', 'k23', 'K23', 'g45', 'G46', 'K47']) {
    const draft = parse(`123p(${code})`)
    assert.deepEqual(draft.melds, [code])
    assert.equal(draftNotation(draft), `123p(${code})`)
    assert.equal(expandMeld(code).length, /^[gG]/.test(code) ? 4 : 3)
  }
  assert.deepEqual(expandMeld('g45'), [45,45,45,45])
})

test('group position and whitespace do not change the hand or winning tile', () => {
  for (const text of ['(s13)567m45s22z(K23) + 6s', '567m45s22z + 6s(s13)(K23)', '567m45s22z ( s13 ) ( K23 ) + 6s']) {
    assert.deepEqual(parse(text), parse(notation))
  }
})

test('malformed groups, a fifth meld, invalid chows and fifth physical copies reject atomically', t => {
  const draft = parse(notation), original = JSON.stringify(draft)
  for (const text of ['123m(s13', '123m((s13))', '123m(q23)', '123m(k20)', '123m(s11)', '123m(s49)', '(k11)(k12)(k13)(k14)(k15)', '22p(k22)', '1z(G41)', '123m(s13)+1p+2p']) {
    assert.throws(() => parse(text, draft), text)
    assert.equal(JSON.stringify(draft), original)
  }
})

test('removing groups from complete notation removes locked slots instead of retaining hidden melds', async t => {
  const { calc } = calculator(t)
  calc.textInput.value = '567m45s22z(s13)(K23)'
  assert.equal(await calc.calculate(), true)
  assert.equal(calc.result.value.state, 'ready')
  assert.equal(calc.fulu.slots[0].locked.code, 's13')
  assert.equal(calc.fulu.slots[1].locked.code, 'K23')
  calc.textInput.value = '567m45s22z(s13)'
  assert.equal(calc.prepareTransfer(), true)
  assert.deepEqual(calc.draft.value.melds, ['s13'])
  assert.equal(calc.fulu.slots[1].locked, null)
  calc.textInput.value = '567m45s22z'
  assert.equal(calc.prepareTransfer(), true)
  assert.deepEqual(calc.draft.value.melds, [])
  assert.ok(calc.fulu.slots.every(slot => slot.locked === null))
})

test('locked slot edits update the same text without losing another meld or the winning tile', async t => {
  const { calc } = calculator(t)
  calc.textInput.value = notation
  await calc.calculate()
  assert.equal(calc.textInput.value, notation)
  calc.fulu.clearSlot(0)
  assert.equal(calc.textInput.value, '567m456s22z(K23)')
  assert.equal(calc.draft.value.winTile, null)
  calc.fulu.slots[0].input = '234m'
  calc.fulu.onSlotInput(0)
  assert.match(calc.textInput.value, /\(s13\)\(K23\)/)
  calc.textInput.value = notation
  await calc.calculate()
  assert.equal(calc.draft.value.winTile, 36)
})

test('bad pasted meld notation does not overwrite the last valid draft or its slots', async t => {
  const { calc, errors } = calculator(t)
  calc.textInput.value = notation; await calc.calculate()
  const before = JSON.parse(JSON.stringify(calc.draft.value))
  calc.textInput.value = '567m45s22z(s11)(K23) + 6s'
  assert.equal(await calc.calculate(), false)
  assert.deepEqual(calc.draft.value, before)
  assert.equal(calc.fulu.slots[0].locked.code, 's13')
  assert.equal(calc.result.value, null)
  assert.ok(errors.length)
})

test('standard meld codes survive rule changes, draw, undo, redo and full state sharing', async t => {
  const calc = unified(t)
  calc.session.value.textInput = '567m45s22z(s13)(K23)'
  await calc.parse()
  for (const rule of ['guobiao', 'qingque', 'riichi']) {
    await calc.changeRule(rule)
    assert.equal(calc.session.value.result.state, 'ready')
    assert.match(calc.session.value.textInput, /\(s13\)\(K23\)$/)
    await calc.draw(36)
    assert.equal(calc.session.value.result.state, 'complete')
    assert.equal(calc.session.value.textInput, notation)
    const complete = calc.snapshot()
    await calc.undo(); assert.equal(calc.session.value.draft.winTile, null)
    await calc.redo(); assert.deepEqual(calc.snapshot(), complete)
    assert.deepEqual(decodeState(encodeState(complete)), complete)
    await calc.clearWin()
  }
})

test('existing calculator sessions migrate without losing separate melds or modifying their saved original', async t => {
  const old = initialState(); old.draft = parse(notation); old.text = '567m45s22z + 6s'
  const saved = JSON.stringify(old), storage = new Map([['salasasa.guobiao-calculator.v1', saved]])
  const calc = unified(t, storage)
  assert.equal(calc.session.value.textInput, notation)
  assert.deepEqual(calc.session.value.draft.melds, ['s13','K23'])
  assert.equal(storage.get('salasasa.guobiao-calculator.v1'), saved)
  assert.equal(JSON.parse(storage.get('salasasa.unified-calculator.v1')).text, notation)
})

test('old share format migrates to bracket notation without changing context or options', () => {
  const old = initialState(); old.draft = parse(notation); old.text = '567m45s22z + 6s'
  old.draft.context.flowers = 3; old.options.thirteenOrphans = false
  const encoded = btoa(String.fromCharCode(...new TextEncoder().encode(JSON.stringify(old))))
  const restored = decodeState(encoded)
  assert.equal(restored.text, notation)
  assert.deepEqual(restored.draft, old.draft)
  assert.deepEqual(restored.options, old.options)
})

test('sessions from before rule presets migrate as Guobiao hands', async t => {
  const old = initialState(); old.draft = parse(notation); old.text = '567m45s22z + 6s'
  delete old.rule
  const storage = new Map([['salasasa.guobiao-calculator.v1', JSON.stringify(old)]])
  const calc = unified(t, storage)
  assert.equal(calc.session.value.rule, 'guobiao')
  assert.equal(calc.session.value.textInput, notation)
  assert.deepEqual(calc.session.value.draft.melds, ['s13', 'K23'])
})
