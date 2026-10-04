import test from 'node:test'
import assert from 'node:assert/strict'
import { createGuobiaoCalculator } from '../src/composables/useGuobiaoCalculator.js'
import { analyzeHand, expandMeld } from '../src/utils/handAnalysis.ts'

function session(t, extra = {}) {
  const errors = []
  const state = createGuobiaoCalculator({ onError: value => errors.push(value), ...extra })
  t.after(() => state.destroy())
  return { state, errors }
}
function memoryStorage() {
  const map = new Map()
  return { getItem: key => map.get(key) || null, setItem: (key, value) => map.set(key, value) }
}
function deferredWorkers() {
  const workers = []
  return { workers, workerFactory() {
    const worker = { terminated: false, postMessage(data) { this.input = data }, terminate() { this.terminated = true }, finish() {
      this.onmessage({ data: { id: this.input.id, result: analyzeHand(this.input.draft, this.input.options) } })
    } }
    workers.push(worker)
    return worker
  } }
}

test('13/14 input dispatches to waits, score/decompositions or discards', async t => {
  const { state } = session(t)
  for (const [notation, expected] of [['123456789m23p55s', 'ready'], ['123456789m23p55s + 1p', 'complete'], ['123456789m23p55s9s', 'discard']]) {
    state.textInput.value = notation
    assert.equal(await state.calculate({ decompose: true }), true)
    assert.equal(state.result.value.state, expected)
    assert.equal(!!state.pailiResult.value, expected !== 'complete')
    assert.equal(state.showDecompositions.value, expected === 'complete')
  }
})

test('editing input or conditions immediately invalidates score and decomposition', async t => {
  const { state } = session(t)
  await state.loadExample('win')
  state.form.flowerCount = 2
  assert.equal(state.result.value, null)
  assert.equal(state.showDecompositions.value, false)
  await state.calculate({ decompose: true })
  assert.equal(state.best.value.fan, 23)
  state.textInput.value = '123'
  assert.equal(state.best.value, null)
  assert.equal(state.showDecompositions.value, false)
})

test('invalid input and empty notation never fall back to an old winning result', async t => {
  const { state, errors } = session(t)
  await state.loadExample('win')
  for (const text of ['123456789m23p55s + 1p + 1p', '11111m234p567s12z', '123m', '']) {
    state.textInput.value = text
    assert.equal(await state.calculate(), false)
    assert.equal(state.result.value, null)
  }
  assert.equal(errors.length, 4)
})

test('storage round trip preserves winning tile, melds, winds, flowers, options and flags', async t => {
  const storage = memoryStorage()
  const { state } = session(t, { storage })
  await state.loadExample('low')
  state.form.flowerCount = 4
  state.form.changFeng = '场风南'
  state.form.menFeng = '自风西'
  state.form.flagSet.gangShangKaiHua = true
  state.options.unrelatedTiles = false
  assert.equal(state.prepareTransfer(), true)
  const { state: restored } = session(t, { storage })
  assert.deepEqual(restored.draft.value, state.draft.value)
  assert.deepEqual(restored.options, state.options)
  assert.equal(restored.form.getTile, 38)
  assert.equal(restored.form.hand.length, 10)
  assert.equal(restored.fulu.slots[0].locked.code, 's22')
  assert.equal(await restored.calculate(), true)
  assert.match(restored.conditionText.value, /自摸.*场风南.*自风西.*花牌 4.*杠上开花/)
})

test('13-tile transfer preserves an empty winning slot and does not add a tile', async t => {
  const { state } = session(t)
  await state.loadExample('ready')
  assert.equal(state.prepareTransfer(), true)
  assert.equal(state.form.getTile, null)
  assert.equal(state.form.hand.length, 13)
  assert.equal(state.draft.value.hand.length, 13)
  await state.calculate()
  assert.equal(state.result.value.state, 'ready')
})

test('cut then draw maintains visible discards; explicit edits begin a new analysis', async t => {
  const { state } = session(t)
  await state.loadExample('discard')
  assert.equal(await state.applyDiscard(39), true)
  assert.equal(state.result.value.state, 'ready')
  assert.deepEqual(state.draft.value.river, [39])
  assert.equal(await state.applyDraw(21), true)
  assert.equal(state.best.value.fan, 21)
  assert.equal(state.form.getTile, 21)
  assert.deepEqual(state.draft.value.river, [39])
  state.clearGetTile()
  assert.equal(state.form.getTile, null)
  assert.equal(state.draft.value.hand.length, 13)
  assert.deepEqual(state.draft.value.river, [])
})

test('clicking a discard-row draw applies cut+draw atomically', async t => {
  const { state } = session(t)
  await state.loadExample('discard')
  assert.equal(await state.applyDraw(21, 39), true)
  assert.equal(state.best.value.fan, 21)
  assert.deepEqual(state.draft.value.river, [39])
})

test('locking a meld cannot silently trim hand tiles or admit a fifth copy', async t => {
  const { state, errors } = session(t)
  await state.loadExample('ready')
  const before = [...state.draft.value.hand]
  state.fulu.slots[0].input = '123p'
  assert.equal(state.fulu.lockSlot(0, { kind: 's', tileId: 22, label: '明顺' }), false)
  assert.deepEqual(state.draft.value.hand, before)
  assert.equal(state.fulu.lockedCount.value, 0)
  state.resetAll()
  state.pickTile(11)
  state.fulu.slots[0].input = '1111m'
  assert.equal(state.fulu.lockSlot(0, { kind: 'G', tileId: 11, label: '暗杠' }), false)
  assert.equal(errors.length, 2)
})

test('unresolved meld input blocks calculation and transfer instead of disappearing', async t => {
  const { state, errors } = session(t)
  await state.loadExample('ready')
  state.fulu.slots[0].input = '333p'
  state.fulu.onSlotInput(0)
  assert.equal(await state.calculate(), false)
  assert.equal(state.prepareTransfer(), false)
  assert.equal(state.fulu.slots[0].input, '333p')
  assert.equal(errors.length, 2)
})

test('kong transfer displays four physical tiles and preserves reduced concealed count', async t => {
  const { state } = session(t)
  state.fulu.slots[0].input = '1111z'
  assert.equal(state.fulu.lockSlot(0, { kind: 'G', tileId: 41, label: '暗杠' }), true)
  state.textInput.value = '123m456p789s1z + 1z'
  // This conflicts with the kong; the error must not replace the current input.
  assert.equal(await state.calculate(), false)
  state.textInput.value = '123m456p789s5z + 5z'
  assert.equal(await state.calculate({ decompose: true }), true)
  assert.equal(state.expectedTotalCount.value, 11)
  const kong = state.best.value.groups.find(group => group.label === '暗杠')
  assert.deepEqual(kong.tiles, expandMeld('G41'))
  assert.equal(state.best.value.groups.flatMap(group => group.tiles).length, 15)
})

test('condition switches keep incompatible flags coherent with their original labels', async t => {
  const { state } = session(t)
  await state.loadExample('win')
  state.form.flagSet.gangShangKaiHua = true
  assert.equal(state.form.hepaiType, 'zimo')
  state.form.flagSet.qiangGangHe = true
  assert.equal(state.form.hepaiType, 'dianhe')
  assert.equal(state.form.flagSet.gangShangKaiHua, false)
  state.form.flagSet.haiDiLaoYue = true
  assert.equal(state.form.flagSet.qiangGangHe, false)
  state.form.hepaiType = 'zimo'
  assert.equal(state.form.flagSet.haiDiLaoYue, false)
  assert.equal(state.form.flagSet.miaoShouHuiChun, true)
  assert.equal(await state.calculate(), true)
})

test('special-hand options survive calculation and mutually exclude seven-pair variants', async t => {
  const { state } = session(t)
  await state.loadExample('orphans')
  state.options.thirteenOrphans = false
  await state.calculate()
  assert.notEqual(state.result.value.state, 'ready')
  state.options.thirteenOrphans = true
  await state.calculate()
  assert.equal(state.result.value.state, 'ready')
  state.setSevenPairs('riichiSevenPairs', true)
  assert.equal(state.options.mcrSevenPairs, false)
})

test('late worker replies cannot overwrite edits or a newer calculation', async t => {
  const fake = deferredWorkers()
  const { state } = session(t, fake)
  state.textInput.value = '123456789m23p55s'
  const first = state.calculate()
  const old = fake.workers[0]
  state.textInput.value = '123456789m23p55s + 1p'
  assert.equal(old.terminated, true)
  const second = state.calculate()
  old.finish()
  assert.equal(await first, false)
  assert.equal(state.result.value, null)
  fake.workers[1].finish()
  assert.equal(await second, true)
  assert.equal(state.best.value.fan, 21)
})

test('worker failure falls back to the same real scoring engine', async t => {
  const fake = deferredWorkers()
  const { state } = session(t, fake)
  state.textInput.value = '123456789m23p55s + 1p'
  const pending = state.calculate()
  fake.workers[0].onerror()
  assert.equal(await pending, true)
  assert.equal(state.best.value.fan, 21)
})

test('clear removes shared input, melds, win context and results; unavailable storage is harmless', async t => {
  const { state } = session(t, { storage: { getItem() { throw Error('blocked') }, setItem() { throw Error('blocked') } } })
  await state.loadExample('low')
  state.form.flowerCount = 8
  state.resetAll()
  assert.deepEqual(state.draft.value.hand, [])
  assert.deepEqual(state.draft.value.melds, [])
  assert.equal(state.form.flowerCount, 0)
  assert.equal(state.fulu.lockedCount.value, 0)
  assert.equal(state.textInput.value, '')
  assert.equal(state.result.value, null)
})
