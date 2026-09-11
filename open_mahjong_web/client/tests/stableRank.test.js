import test from 'node:test'
import assert from 'node:assert/strict'
import { calculateStableRank, STABLE_RANK_TIERS, STABLE_RANK_FORMATS } from '../src/utils/stableRank.js'

function samples(counts, tier = 'advanced', gameType = 'quanzhuang') {
  return counts.flatMap((count, index) => Array.from({ length: count }, () => ({ tier, gameType, rank: index + 1 })))
}

function near(actual, expected, tolerance = 1e-10) {
  assert.ok(Math.abs(actual - expected) < tolerance, `expected ${actual} ≈ ${expected}`)
}

function row(analysis, rankName) {
  return analysis.expectations.find((entry) => entry.rankName === rankName)
}

test('200 advanced full games at 40/20/20/20 remain positive even at the ninth dan', () => {
  const result = calculateStableRank(samples([80, 40, 40, 40]), { currentRank: '七段' })
  assert.equal(result.sampleCount, 200)
  assert.deepEqual(result.overall.rankRates, [0.4, 0.2, 0.2, 0.2])
  near(row(result.overall, '七段').expectedPt, 10.8)
  near(row(result.overall, '八段').expectedPt, 4.8)
  near(row(result.overall, '九段').expectedPt, 1.8)
  assert.equal(result.overall.currentExpectation.rankName, '七段')
  assert.equal(result.overall.estimate.status, 'top')
  assert.equal(result.overall.estimate.dan, 9)
  assert.equal(result.overall.smallSample, false)
})

test('tenth dan earns zero PT without turning fixed points into an estimated stable rank', () => {
  const result = calculateStableRank(samples([80, 40, 40, 40]), { currentRank: '十段' })
  assert.equal(result.currentRank, '十段')
  assert.equal(result.overall.currentExpectation.expectedPt, 0)
  assert.equal(result.overall.currentExpectation.lossPt, 0)
  assert.equal(result.overall.currentExpectation.confidenceLow, 0)
  assert.equal(result.overall.currentExpectation.confidenceHigh, 0)
  assert.equal(result.overall.estimate.status, 'top')
  assert.equal(result.overall.estimate.dan, 9)
  const balanced = calculateStableRank(samples([25, 25, 25, 25]), { currentRank: '十段' })
  assert.equal(balanced.overall.estimate.dan, 5)
  assert.equal(balanced.overall.currentExpectation.expectedPt, 0)
})

test('interpolates the adjacent PT zero and reports an exact zero without extrapolation', () => {
  const balanced = calculateStableRank(samples([30, 20, 20, 30])).overall
  near(row(balanced, '五段').expectedPt, 1.05)
  near(row(balanced, '六段').expectedPt, -3)
  near(balanced.estimate.dan, 5 + 1.05 / 4.05)
  assert.equal(balanced.estimate.lowerRank, '五段')
  assert.equal(balanced.estimate.upperRank, '六段')
  const exact = calculateStableRank(samples([25, 25, 25, 25])).overall
  assert.equal(exact.estimate.label, '安定五段')
  assert.equal(exact.estimate.dan, 5)
  assert.equal(row(exact, '五段').expectedPt, 0)
})

test('mixed rooms and lengths preserve the observed PT weights, not pooled place rates', () => {
  const source = [
    ...samples([4, 0, 0, 0], 'beginner', 'dongfeng'),
    ...samples([0, 0, 0, 2], 'advanced', 'quanzhuang'),
    ...samples([1, 0, 0, 0], 'advanced', 'banzhuang'),
  ]
  const result = calculateStableRank(source, { currentRank: '七段' })
  assert.equal(result.groups.length, 3)
  near(result.overall.currentExpectation.expectedPt, (4 * 11.76 - 2 * 94.5 + 58.8) / 7)
  const weighted = result.groups.reduce((sum, group) => sum + group.currentExpectation.expectedPt * group.sampleCount, 0) / 7
  near(result.overall.currentExpectation.expectedPt, weighted)
  assert.ok(result.overall.currentExpectation.expectedPt < 0)
})

test('higher current ranks rescore old lower-room results at each candidate rank', () => {
  const source = samples([40, 20, 20, 20], 'intermediate')
  const lowerCurrent = calculateStableRank(source, { currentRank: '四段' }).overall
  const higherCurrent = calculateStableRank(source.map((entry) => ({ ...entry, historicalPt: 999 })), { currentRank: '八段' }).overall
  assert.deepEqual(lowerCurrent.estimate, higherCurrent.estimate)
  near(higherCurrent.currentExpectation.expectedPt, -9.6)
  assert.equal(higherCurrent.currentExpectation.admissionLimited, true)
  assert.ok(higherCurrent.admissionNotes.some((note) => note.includes('七段及以上不可进入')))
})

test('intermediate theoretical high estimates explicitly carry the admission ceiling', () => {
  const result = calculateStableRank(samples([60, 20, 10, 10], 'intermediate')).overall
  assert.equal(result.estimate.status, 'top')
  assert.equal(result.admissionLimited, true)
  assert.equal(row(result, '六段').admissionLimited, false)
  assert.equal(row(result, '七段').admissionLimited, true)
})

test('empty, invalid, all winning, all losing, and small samples remain distinguishable', () => {
  const empty = calculateStableRank([{ tier: 'custom', gameType: 'quanzhuang', rank: 1 },
    { tier: 'advanced', gameType: 'unknown', rank: 1 },
    { tier: 'advanced', gameType: 'quanzhuang', rank: 1.5 }, null])
  assert.equal(empty.excludedCount, 4)
  assert.equal(empty.overall.estimate.status, 'empty')
  assert.equal(empty.overall.currentExpectation, null)
  assert.equal(row(empty.overall, '九段').expectedPt, null)
  const noLoss = calculateStableRank(samples([10, 10, 0, 0])).overall
  assert.equal(noLoss.estimate.status, 'no_losses')
  assert.equal(noLoss.estimate.dan, null)
  assert.equal(noLoss.smallSample, true)
  const losses = calculateStableRank(samples([0, 0, 5, 5])).overall
  assert.equal(losses.estimate.status, 'below_dan')
  assert.equal(losses.estimate.lowerRank, '3级')
  const single = calculateStableRank(samples([0, 0, 0, 1])).overall
  assert.equal(row(single, '五段').confidenceLow, null)
})

test('normal confidence interval estimates uncertainty in per-game PT mean', () => {
  const result = calculateStableRank(samples([1, 0, 0, 1])).overall
  const fifth = row(result, '五段')
  near(fifth.expectedPt, 5.25)
  // Two observations, +84 and -73.5: sample SD / sqrt(n) = 78.75.
  near(fifth.confidenceLow, 5.25 - 1.96 * 78.75)
  near(fifth.confidenceHigh, 5.25 + 1.96 * 78.75)
})

test('settlement rounding matches authoritative Python round before averaging', () => {
  const cases = [
    ['beginner', 'dongfeng', 3, '2级', -2.21],
    ['advanced', 'dongfeng', 3, '三段', -9.55],
    ['advanced', 'dongfeng', 4, '1级', -12.00],
    ['advanced', 'dongfeng', 3, '二段', -8.08],
    ['intermediate', 'dongfeng', 1, '七段', 25.48],
    ['mcrpl', 'banzhuang', 1, '九段', 75.6],
  ]
  for (const [tier, gameType, rank, rankName, pt] of cases) {
    const result = calculateStableRank([{ tier, gameType, rank }]).overall
    assert.equal(row(result, rankName).expectedPt, pt, `${tier}/${gameType}/${rank}/${rankName}`)
  }
})

test('every supported room and format stays separate and MCRPL shows qualification requirements', () => {
  const source = STABLE_RANK_TIERS.flatMap((tier) => STABLE_RANK_FORMATS.flatMap((format) =>
    samples([1, 1, 1, 1], tier.value, format.value)))
  const result = calculateStableRank(source)
  assert.equal(result.sampleCount, 48)
  assert.equal(result.groups.length, 12)
  for (const group of result.groups) assert.equal(group.sampleCount, 4)
  assert.ok(result.groups.filter((group) => group.tier === 'mcrpl').every((group) => group.admissionLimited))
})

test('tied places average already rounded PT and remain one observation with the real competition rank', () => {
  const result = calculateStableRank([
    { tier: 'advanced', gameType: 'quanzhuang', rank: 2, rankWeights: [0, 0.5, 0.5, 0] },
  ]).overall
  assert.equal(result.sampleCount, 1)
  assert.deepEqual(result.rankCounts, [0, 1, 0, 0])
  assert.equal(result.noLossSamples, false)
  assert.equal(row(result, '九段').expectedPt, (21 - 54) / 2)
  assert.equal(row(result, '九段').confidenceLow, null)
  const fourWay = calculateStableRank([
    { tier: 'advanced', gameType: 'quanzhuang', rank: 1, rankWeights: [0.25, 0.25, 0.25, 0.25] },
  ]).overall
  assert.equal(fourWay.estimate.dan, 5)
  assert.equal(fourWay.noLossSamples, false)
  const roundedTie = calculateStableRank([
    { tier: 'beginner', gameType: 'banzhuang', rank: 1, rankWeights: [0.25, 0.25, 0.25, 0.25] },
  ]).overall
  // Python >= 3.12 sum([16.8, 4.2, -19.95, -46.55]) / 4 rounds to -11.37.
  assert.equal(row(roundedTie, '四段').expectedPt, -11.37)
  const malformed = calculateStableRank([
    { tier: 'advanced', gameType: 'quanzhuang', rank: 2, rankWeights: [0, 0.4, 0.6, 0] },
    { tier: 'advanced', gameType: 'quanzhuang', rank: 2, rankWeights: [0.5, 0.5, 0, 0] },
  ])
  assert.equal(malformed.sampleCount, 0)
  assert.equal(malformed.excludedCount, 2)
})
