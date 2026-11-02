import test from 'node:test'
import assert from 'node:assert/strict'
import { emptyDraft, parseHandText, analyzeHand } from '../src/utils/handAnalysis.ts'
import { ruleOptions } from '../src/utils/calculatorRules.ts'
import { scoreQingque } from '../src/utils/scoring/qingque.ts'

function score(text, melds = [], context = {}) {
  const draft = parseHandText(text, { ...emptyDraft(), melds })
  Object.assign(draft.context, context)
  return scoreQingque(draft, ruleOptions('qingque'))[0]
}

test('MMCR report: triplet and kong count as mirrored melds', () => {
  const best = score('1115666p + 7p', ['s16', 'G11'], { method: 'tsumo', kongWin: true, seat: '南' })
  assert.ok(best.fanNames.includes('镜同'))
  assert.equal(best.points, 322)
})

test('MMCR report: repeated lower mirrored pair scores three mirrored pairs', () => {
  const best = score('111199m111199s44z', [], { method: 'tsumo', seat: '西' })
  assert.ok(best.fanNames.includes('镜同三对'))
  assert.equal(best.points, 1180)
})

test('mirrored pairs are independent of rank ordering and suit selection', () => {
  for (const [a, b] of [['m', 'p'], ['m', 's'], ['p', 's']]) {
    for (let repeated = 1; repeated <= 9; repeated++) {
      for (let single = 1; single <= 9; single++) {
        if (repeated === single) continue
        const part = String(repeated).repeat(4) + String(single).repeat(2)
        const best = score(`${part}${a}${part}${b}44z`, [], { method: 'tsumo', seat: '西' })
        assert.ok(best.fanNames.includes('镜同三对'), `${part}${a}${part}${b}`)
      }
    }
  }
})

test('analysis excludes the impossible fifth tile and retains the legal wait', () => {
  const draft = parseHandText('1111345678999m', emptyDraft())
  assert.deepEqual(analyzeHand(draft, ruleOptions('qingque'), 'qingque').waits.map(row => row.tile), [12])
})
