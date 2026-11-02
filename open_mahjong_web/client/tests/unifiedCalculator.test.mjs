import test from 'node:test'
import assert from 'node:assert/strict'
import { effectScope } from 'vue'
import { useUnifiedCalculator, initialState, encodeState, decodeState } from '../src/design-lab/unified/useUnifiedCalculator.js'
import { draftNotation, effectiveCount } from '../src/utils/handAnalysis.ts'
import { ruleOptions } from '../src/utils/calculatorRules.ts'
import { parseNotationText } from '../src/composables/useMahjongTiles.js'

const pause = ms => new Promise(resolve => setTimeout(resolve, ms))
function setup(t, hash = '', storage = new Map()) {
  Object.defineProperty(globalThis, 'location', { configurable: true, value: { hash, href: `http://localhost/unified-calculator.html${hash}` } })
  Object.defineProperty(globalThis, 'sessionStorage', { configurable: true, value: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) } })
  const scope = effectScope()
  const lab = scope.run(useUnifiedCalculator)
  t.after(() => scope.stop())
  return lab
}
const input = async (lab, text) => { lab.session.value.textInput = text; return lab.parse() }

test('one hand flows through discard, wait and score, with reversible river and win tile', async t => {
  const lab = setup(t)
  await lab.example('discard')
  assert.equal(lab.session.value.result.state, 'discard')
  await lab.discard(39)
  assert.equal(lab.session.value.result.state, 'ready')
  assert.deepEqual(lab.session.value.draft.river, [39])
  await lab.draw(21, null, 'tsumo')
  assert.equal(lab.session.value.best.fan, 23)
  const complete = lab.snapshot()
  await lab.clearWin()
  assert.equal(lab.session.value.result.state, 'ready')
  assert.deepEqual(lab.session.value.draft.river, [39])
  await lab.undo()
  assert.deepEqual(lab.snapshot(), complete)
  await lab.undo()
  assert.equal(lab.session.value.result.state, 'ready')
  await lab.redo()
  assert.deepEqual(lab.snapshot(), complete)
})

test('new branch replaces redo, and history restores full rules and conditions', async t => {
  const lab = setup(t)
  await lab.example('ready')
  await lab.draw(21)
  await lab.undo()
  await lab.draw(24, null, 'tsumo')
  assert.equal(lab.canRedo.value, false)
  assert.equal(lab.session.value.draft.winTile, 24)
  for (const rule of ['qingque', 'riichi', 'guobiao']) {
    const before = lab.snapshot()
    await lab.changeRule(rule)
    assert.deepEqual({ ...lab.session.value.options }, ruleOptions(rule))
    assert.equal(lab.session.value.result.state, 'complete')
    await lab.undo()
    assert.deepEqual(lab.snapshot(), before)
    await lab.redo()
    assert.equal(lab.session.value.rule, rule)
  }
})

test('pending notation is applied before rule or context changes and never displays a stale score', async t => {
  const lab = setup(t)
  await lab.example('win')
  lab.session.value.textInput = '123456789m23p55s'
  await lab.changeRule('riichi')
  assert.equal(lab.session.value.result.state, 'ready')
  lab.session.value.textInput = '11111m'
  lab.session.value.form.riichi = 1
  await pause(250)
  assert.match(lab.error.value, /4 张/)
  assert.equal(lab.session.value.result, null)
  assert.equal(await lab.changeRule('qingque'), false)
  assert.equal(lab.session.value.rule, 'riichi')
  await lab.clear()
  assert.equal(lab.error.value, '')
  assert.equal(effectiveCount(lab.session.value.draft), 0)
})

test('condition changes recalculate automatically and clear dependent riichi bonuses', async t => {
  const lab = setup(t)
  await lab.changeRule('riichi')
  await lab.example('noYaku')
  assert.equal(lab.session.value.best.canWin, false)
  lab.session.value.form.riichi = 1
  lab.session.value.form.ippatsu = true
  lab.session.value.form.uraDora = 2
  await pause(250)
  assert.equal(lab.session.value.best.canWin, true)
  assert.ok(lab.session.value.best.fan >= 4)
  lab.session.value.form.riichi = 0
  await pause(250)
  assert.equal(lab.session.value.form.ippatsu, false)
  assert.equal(lab.session.value.form.uraDora, 0)
  assert.equal(lab.session.value.best.canWin, false)
})

test('explicit winning tile changes no tiles and remains reversible', async t => {
  const lab = setup(t)
  await lab.example('win')
  const hand = [...lab.session.value.draft.hand]
  await lab.chooseWin(35)
  assert.deepEqual(lab.session.value.draft.hand, hand)
  assert.equal(lab.session.value.draft.winTile, 35)
  assert.match(lab.session.value.textInput, /\+ 5s$/)
  await lab.undo()
  assert.equal(lab.session.value.draft.winTile, 21)
})

test('visible tiles reduce ukeire, reject fifth copies, and restore honor notation faithfully', async t => {
  const lab = setup(t)
  await lab.example('ready')
  lab.visibleInput.value = '111p4444p567z'
  await lab.applyVisible()
  assert.equal(lab.session.value.result.paili.total_accept, 1)
  assert.equal(lab.session.value.result.waits.length, 1)
  const before = lab.snapshot()
  lab.visibleInput.value = '11111p'
  assert.equal(await lab.applyVisible(), false)
  assert.match(lab.error.value, /4 张/)
  assert.deepEqual(lab.snapshot(), before)
  await lab.undo()
  await lab.redo()
  assert.deepEqual(parseNotationText(lab.visibleInput.value).sort(), [...lab.session.value.draft.visible].sort())
  lab.visibleInput.value = '11114444p'
  await lab.applyVisible()
  assert.equal(lab.session.value.result.state, 'ready')
  assert.equal(lab.session.value.result.waits.length, 0)
})

test('meld entry pauses analysis until committed; kan counts four physical and three effective tiles', async t => {
  const lab = setup(t)
  await input(lab, '123456m23p55s')
  lab.session.value.fulu.activate(0)
  await lab.pick(41)
  assert.equal(lab.pendingMeld.value, true)
  assert.equal(lab.session.value.result, null)
  await lab.pick(41)
  await lab.pick(41)
  await lab.pick(41)
  const option = lab.session.value.fulu.slots[0].options.find(item => item.kind === 'G')
  await lab.meldAction('lockSlot', 0, option)
  assert.deepEqual(lab.session.value.draft.melds, ['G41'])
  assert.equal(effectiveCount(lab.session.value.draft), 13)
  assert.equal(lab.session.value.result.state, 'ready')
  await lab.draw(21)
  assert.equal(lab.session.value.result.state, 'complete')
  assert.equal(lab.session.value.draft.hand.length, 11)
  assert.equal(lab.session.value.fulu.slots[0].locked.displayTiles.length, 4)
  await lab.undo()
  assert.equal(lab.session.value.fulu.slots[0].locked.code, 'G41')
})

test('opening a meld clears closed-only context and is undoable', async t => {
  const lab = setup(t)
  await lab.changeRule('riichi')
  await input(lab, '456789m23p55s')
  lab.session.value.form.riichi = 1
  lab.session.value.form.ippatsu = true
  await pause(220)
  lab.session.value.fulu.slots[0].input = '123m'
  await lab.meldAction('onSlotInput', 0)
  assert.equal(lab.session.value.closed, false)
  assert.equal(lab.session.value.form.riichi, 0)
  assert.equal(lab.session.value.form.ippatsu, false)
  await lab.undo()
  assert.equal(lab.session.value.closed, true)
  assert.equal(lab.session.value.form.riichi, 1)
})

test('all analysis states exist under all three rule presets', async t => {
  const lab = setup(t)
  for (const rule of ['guobiao', 'qingque', 'riichi']) {
    await lab.changeRule(rule)
    for (const [example, state] of [['improve', 'improve'], ['discard', 'discard'], ['ready', 'ready'], ['win', 'complete'], ['orphans', 'ready']]) {
      await lab.example(example)
      assert.equal(lab.session.value.result.state, state, `${rule}/${example}`)
    }
    assert.equal(lab.session.value.result.waits.length, 13)
  }
})

test('full-state URL preserves melds, visible tiles, river, conditions and customized shapes', async t => {
  const lab = setup(t)
  await lab.example('discard')
  await lab.discard(39)
  await lab.changeRule('riichi')
  lab.session.value.form.riichi = 1
  lab.session.value.form.dora = 2
  lab.session.value.form.changFeng = '场风南'
  lab.session.value.form.menFeng = '自风西'
  await pause(220)
  lab.visibleInput.value = '567z'
  await lab.applyVisible()
  await lab.changeOption('thirteenOrphans', false)
  const state = lab.snapshot(), url = await lab.share()
  assert.ok(url.startsWith('http://localhost/unified-calculator.html#hand='))
  assert.deepEqual(decodeState(new URL(url).hash.slice(6)), state)
  const restored = setup(t, new URL(url).hash)
  assert.deepEqual(restored.snapshot(), state)
  await pause(20)
  assert.equal(restored.session.value.result.state, 'ready')
})

test('serialization rejects malformed, oversized and physically impossible shares', () => {
  assert.deepEqual(decodeState(encodeState(initialState())), initialState())
  for (const text of ['bad', 'x'.repeat(16001), btoa('{}')]) assert.throws(() => decodeState(text))
  const bad = initialState()
  bad.draft.hand = [11,11,11,11,11]
  assert.throws(() => encodeState(bad), /4 张/)
  bad.draft.hand = []
  bad.options.riichiSevenPairs = true
  assert.throws(() => encodeState(bad), /同时开启/)
  const mismatched = initialState()
  mismatched.text = '123m'
  assert.throws(() => encodeState(mismatched), /不一致/)
})

test('refresh restores the prototype state without touching the existing pages storage', async t => {
  const storage = new Map([['salasasa.guobiao-calculator.v1', 'existing-page-state']])
  const lab = setup(t, '', storage)
  await lab.example('win')
  const restored = setup(t, '', storage)
  assert.deepEqual(restored.snapshot(), lab.snapshot())
  assert.equal(storage.get('salasasa.guobiao-calculator.v1'), 'existing-page-state')
  assert.equal(restored.session.value.textInput, draftNotation(restored.session.value.draft))
})

test('invalid URL can be corrected with normal input', async t => {
  const lab = setup(t, '#hand=invalid')
  assert.match(lab.error.value, /链接/)
  await lab.example('ready')
  assert.equal(lab.error.value, '')
  assert.equal(lab.session.value.result.state, 'ready')
})

test('history retains at most sixty complete snapshots', async t => {
  const lab = setup(t)
  for (let i = 0; i < 66; i++) await lab.changeRule(i % 2 ? 'guobiao' : 'qingque')
  assert.equal(lab.history.value.length, 60)
  assert.equal(lab.cursor.value, 59)
  assert.equal(await lab.restore(-1), false)
  assert.equal(await lab.restore(60), false)
})
