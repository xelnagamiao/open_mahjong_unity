import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

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

test('all annotated Python scoring examples stay in parity', () => {
  const pythonReference = fileURLToPath(
    new URL(
      '../../../open_mahjong_server/server/game_calculation/guobiao_hepai_check.py',
      import.meta.url,
    ),
  )
  const source = readFileSync(pythonReference, 'utf8')
  const examplePattern = /test_save\s*=\s*(\[\[.*?\]\])\s*(?:#\s*)?(\d+)\s*(?:#.*)?$/
  const examples = source.split(/\r?\n/).flatMap((line) => {
    const match = line.match(examplePattern)
    return match ? [{ input: JSON.parse(match[1]), expectedFan: Number(match[2]) }] : []
  })

  assert.equal(examples.length, 234, 'Python reference example count changed')

  const failures = []
  for (const { input, expectedFan } of examples) {
    const [combinations, hand, getTile, wayToHepai] = input
    const result = hepaiCheck(hand, combinations, wayToHepai, getTile)
    if (result.fan !== expectedFan) {
      failures.push({ input, expectedFan, actualFan: result.fan, fanNames: result.fanNames })
    }
  }

  assert.deepEqual(failures, [])
})
