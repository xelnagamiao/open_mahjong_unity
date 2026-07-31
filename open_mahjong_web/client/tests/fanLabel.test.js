import test from 'node:test'
import assert from 'node:assert/strict'

import { parseFanMultiplier, resolveFanLabel } from '../src/constants/guessFanCatalog.js'

test('repeated fan labels resolve their base name and multiplied value', () => {
  assert.deepEqual(parseFanMultiplier('幺九刻*1'), {
    label: '幺九刻*1',
    baseName: '幺九刻',
    multiplier: 1,
  })
  assert.equal(resolveFanLabel('幺九刻*1', ['guobiao']).totalValue, 1)
  assert.equal(resolveFanLabel('一般高×2', ['guobiao']).totalValue, 2)
  assert.equal(resolveFanLabel('四归一 x 3', ['guobiao']).totalValue, 6)
  assert.equal(resolveFanLabel('花牌X8', ['guobiao']).totalValue, 8)
})

test('plain and unknown fan labels remain safe', () => {
  const plain = resolveFanLabel('清一色', ['guobiao'])
  assert.equal(plain.baseName, '清一色')
  assert.equal(plain.multiplier, 1)
  assert.equal(plain.totalValue, 24)

  const unknown = resolveFanLabel('不存在*2', ['guobiao'])
  assert.equal(unknown.definition, null)
  assert.equal(unknown.totalValue, null)
})
