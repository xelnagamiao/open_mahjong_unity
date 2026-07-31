import test from 'node:test'
import assert from 'node:assert/strict'

import { hepaiCheck } from '../src/game2d/calc/guobiao/gbHepai.ts'

test('big three dragons wait scores instead of falling back to zero fan', () => {
  const result = hepaiCheck(
    [31, 32, 33, 38, 38, 45, 45, 45, 46, 46, 46, 47, 47, 47],
    [],
    ['点和'],
    38,
    false,
  )

  assert.ok(result.fan >= 88)
  assert.ok(result.fanNames.some((name) => name.startsWith('大三元')))
})
