import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import { transform } from 'esbuild'

// The calculator is pure TypeScript; transform in memory without a Vite build.
const source = await fs.readFile(new URL('../src/game2d/calc/shanxi/waitTips.ts', import.meta.url), 'utf8')
const { code } = await transform(source, { loader: 'ts', format: 'esm', target: 'es2022' })
const { buildShanxiWaitData, shanxiWaits, shanxiBaseScore, shanxiKnownConcealedDiscards } =
  await import(`data:text/javascript;base64,${Buffer.from(code).toString('base64')}`)

const basis = [11,12,13,21,22,23,31,32,33,17,18,19]
const mixed = [14,15,21,22,23,31,32,33,27,28,29,46,46]
function waits(hand, publicTiles = []) {
  const result = buildShanxiWaitData({ hand, combinations: [], publicTiles })
  assert.equal(result?.type, 'waits')
  return result.details
}

for (const point of [1,2,3,4,5]) {
  const label = point < 3 ? '未起和' : '未满足'
  test(`basic ${point}-point wait remains visible as ${label}`, () => {
    const tile = 10 + point
    const hand = [...basis, tile]
    assert.ok(shanxiWaits(hand).includes(tile))
    const entry = waits(hand).find(entry => entry.tile === tile)
    assert.equal(entry.label, label)
    assert.equal(entry.base_f, 0)
    assert.equal(entry.selfdrawn_f, 0)
    assert.equal(entry.unit, '分')
  })
}

test('a one-point orphan wait is 未起和 even when a six-point declaration wait exists', () => {
  const hand = [11,19,21,29,31,39,41,42,43,44,45,46,47]
  const details = waits(hand)
  assert.equal(details.find(entry => entry.tile === 11).label, '未起和')
  assert.equal(details.find(entry => entry.tile === 11).selfdrawn_f, 0)
  assert.equal(details.find(entry => entry.tile === 47).base_f, 70)
})

test('mixed waits preserve the three-point tsumo and six-point ron categories', () => {
  const details = waits(mixed)
  assert.deepEqual(details.map(entry => entry.tile), [13,16])
  assert.equal(details[0].base_f, 0)
  assert.equal(details[0].selfdrawn_f, 3)
  assert.equal(details[1].base_f, 6)
})

test('flush plus one dragon uses 49 base points rather than 44 Guobiao fan', () => {
  const hand = [11,12,13,14,15,16,17,18,19,16,17,18,19]
  const entry = waits(hand).find(entry => entry.tile === 19)
  assert.equal(entry.base_f, 49)
  assert.equal(entry.unit, '分')
})

for (const [name, hand, tile, points] of [
  ['ordinary', [...basis,47,47],47,10],
  ['seven pairs', [11,11,12,12,13,13,21,21,22,22,31,31,47,47],47,30],
  ['luxury seven pairs', [19,19,19,19,21,21,22,22,23,23,24,24,29,29],19,49],
  ['thirteen orphans', [11,19,21,29,31,39,41,42,43,44,45,46,47,47],47,70],
]) {
  test(`${name} base points agree with the MIL scoring examples`, () => {
    assert.equal(shanxiBaseScore(hand,[],tile,false),points)
  })
}

test('discard preview counts the proposed cut once and does not mutate the hand', () => {
  const hand = [...mixed,13], original = [...hand]
  const data = buildShanxiWaitData({hand,combinations:[],publicTiles:[]},true)
  assert.equal(data?.type,'waits_all')
  assert.equal(data.details.find(entry=>entry.discard_tile===13).adds.find(entry=>entry.tile===13).remaining_count,3)
  assert.deepEqual(hand, original)
})

test('concealed-discard memory follows the chosen seat and clears on rewind', () => {
  const round = {action_ticks:[['c',13,'T','C'],['d',11],['c',16,'F','C'],['p',11,0],['c',46,'F','C']]}
  assert.deepEqual(shanxiKnownConcealedDiscards(round,0,0),[])
  assert.deepEqual(shanxiKnownConcealedDiscards(round,3,0),[13])
  assert.deepEqual(shanxiKnownConcealedDiscards(round,3,1),[16])
  assert.deepEqual(shanxiKnownConcealedDiscards(round,5,0),[13,46])
  assert.equal(waits(mixed,[13]).find(entry=>entry.tile===13).remaining_count,3)
})

test('non-MIL shapes, illegal melds and a fifth physical copy do not become waits', () => {
  assert.deepEqual(shanxiWaits([11,12,13],[]),[])
  assert.deepEqual(shanxiWaits(mixed,['s12']),[])
  assert.equal(shanxiBaseScore([11,11,11,11,11,21,22,23,31,32,33,47,47,47],[],11,true),0)
})
