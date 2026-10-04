import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { fileURLToPath } from 'node:url'

const built = await build({
  entryPoints: [fileURLToPath(new URL('../src/game2d/calc/guobiao/waitTips.ts', import.meta.url))],
  bundle: true, write: false, platform: 'node', format: 'esm',
})
const { buildLocalWaitData } = await import('data:text/javascript;base64,' + Buffer.from(built.outputFiles[0].text).toString('base64'))

function context(overrides = {}) {
  return {
    tips: true, hand: [11, 11, 13, 14, 15, 23, 23, 24, 24, 25, 25, 32, 33],
    combinations: [], flowerCount: 0, playerIndex: 0, currentRound: 1,
    hepaiLimit: 5, subRule: 'guobiao/lanshi',
    seatDiscards: [[], [], [], []], seatCombinations: [[], [], [], []],
    ...overrides,
  }
}

test('Lanshi user hand has self-draw-only waits at the five-point floor', () => {
  const result = buildLocalWaitData(context(), { includeDiscards: false })
  assert.deepEqual(result, { type: 'waits', details: [
    { tile: 31, base_f: 0, selfdrawn_f: 5, remaining_count: 4 },
    { tile: 34, base_f: 0, selfdrawn_f: 5, remaining_count: 4 },
  ] })
})

test('public last-copy information changes both the score and remaining count', () => {
  const result = buildLocalWaitData(context({ seatDiscards: [[], [34], [34], [34]] }), { includeDiscards: false })
  assert.deepEqual(result.details.find(row => row.tile === 34), {
    tile: 34, base_f: 6, selfdrawn_f: 7, remaining_count: 1,
  })
})

test('discard previews use the native scorer and count the pending discard', () => {
  const ctx = context()
  ctx.hand.push(34)
  const before = structuredClone(ctx)
  const result = buildLocalWaitData(ctx, { includeDiscards: true })
  assert.equal(result.type, 'waits_all')
  assert.deepEqual(result.details.find(row => row.discard_tile === 34).adds.find(row => row.tile === 34), {
    tile: 34, base_f: 0, selfdrawn_f: 5, remaining_count: 3,
  })
  assert.deepEqual(ctx, before)
})

test('standard combination-dragon waits do not leak into Lanshi', () => {
  const ctx = context({ hand: [11, 14, 17, 22, 25, 28, 33, 36, 39, 41, 41, 41, 42] })
  assert.equal(buildLocalWaitData(ctx, { includeDiscards: false }), null)
  ctx.subRule = 'guobiao/standard'
  assert.ok(buildLocalWaitData(ctx, { includeDiscards: false }).details.some(row => row.tile === 42))
})
