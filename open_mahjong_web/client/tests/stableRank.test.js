import test from 'node:test'
import assert from 'node:assert/strict'
import { calculateStableRank, scoringRows, STABLE_RANK_TIERS, STABLE_RANK_FORMATS } from '../src/utils/stableRank.js'

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

test('does not pool rooms for the headline; mixes formats only inside a room', () => {
  const source = [
    ...samples([4, 0, 0, 0], 'beginner', 'dongfeng'),
    ...samples([0, 0, 0, 2], 'advanced', 'quanzhuang'),
    ...samples([1, 0, 0, 0], 'advanced', 'banzhuang'),
  ]
  const result = calculateStableRank(source, { currentRank: '七段' })
  assert.equal(result.defaultKey, 'advanced')
  assert.deepEqual(result.groups.map((group) => group.key), ['beginner', 'advanced'])
  assert.equal(result.formatGroups.length, 3)
  const advanced = result.groups.find((group) => group.key === 'advanced')
  assert.equal(advanced.sampleCount, 3)
  near(advanced.currentExpectation.expectedPt, (2 * -94.5 + 58.8) / 3)
  const beginner = result.groups.find((group) => group.key === 'beginner')
  assert.equal(beginner.sampleCount, 4)
  near(beginner.currentExpectation.expectedPt, 11.76)
})

test('high-dan glance stays on advanced and ignores beginner fourths', () => {
  const source = [
    ...samples([25, 25, 25, 25], 'advanced'),
    ...samples([0, 0, 0, 80], 'beginner'),
    ...samples([20, 20, 20, 20], 'intermediate'),
  ]
  const result = calculateStableRank(source, { currentRank: '八段' })
  assert.equal(result.defaultKey, 'advanced')
  assert.equal(result.overall.estimate.label, '安定五段')
  assert.equal(result.overall.sampleCount, 100)
  const mixed = calculateStableRank(source).overall
  assert.equal(mixed.tier, 'advanced')
})

test('skips rooms the current rank cannot enter when picking the default room', () => {
  const source = [
    ...samples([10, 10, 10, 10], 'beginner'),
    ...samples([10, 10, 10, 10], 'intermediate'),
  ]
  const atEighth = calculateStableRank(source, { currentRank: '八段' })
  assert.equal(atEighth.defaultKey, 'beginner')
  const atFifth = calculateStableRank(source, { currentRank: '五段' })
  assert.equal(atFifth.defaultKey, 'intermediate')
})

test('higher current ranks rescore old lower-room results at each candidate rank', () => {
  const source = samples([40, 20, 20, 20], 'intermediate')
  const lowerCurrent = calculateStableRank(source, { currentRank: '四段' }).overall
  const higherCurrent = calculateStableRank(source.map((entry) => ({ ...entry, historicalPt: 999 })), { currentRank: '八段' }).overall
  assert.deepEqual(lowerCurrent.estimate, higherCurrent.estimate)
  assert.equal(higherCurrent.currentExpectation.expectedPt, null)
  assert.equal(higherCurrent.currentExpectation.admissionBlocked, true)
  assert.ok(higherCurrent.admissionNotes.some((note) => note.includes('不可进入')))
})

test('intermediate theoretical high estimates explicitly carry the admission ceiling', () => {
  const result = calculateStableRank(samples([60, 20, 10, 10], 'intermediate')).overall
  assert.equal(result.estimate.status, 'top')
  assert.equal(result.estimate.label, '安定六段')
  assert.equal(row(result, '六段').admissionBlocked, false)
  assert.equal(row(result, '七段').admissionBlocked, true)
  assert.equal(row(result, '七段').expectedPt, null)
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
  assert.equal(losses.estimate.label, '低于四段')
  assert.equal(losses.estimate.upperRank, '四段')
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
    ['beginner', 'dongfeng', 3, '三段', -9.55],
    ['beginner', 'dongfeng', 4, '1级', -12.00],
    ['beginner', 'dongfeng', 3, '二段', -8.08],
    ['intermediate', 'dongfeng', 1, '六段', 25.48],
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
  assert.equal(result.groups.length, 4)
  assert.equal(result.formatGroups.length, 12)
  assert.equal(result.defaultKey, 'advanced')
  for (const group of result.groups) assert.equal(group.sampleCount, 12)
  for (const group of result.formatGroups) assert.equal(group.sampleCount, 4)
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

test('beginner first place is 24 PT, never the 30-point room base', () => {
  const rows = scoringRows('beginner', '初段')
  const full = rows.find((row) => row.label === '全庄')
  near(full.places[0], 24)
  near(full.places[1], 6)
  near(full.places[2], -13.5)
  near(full.places[3], -31.5)
  const east = rows.find((row) => row.label === '东风')
  near(east.places[0], 11.76)
  const allFirst = calculateStableRank(samples([10, 0, 0, 0], 'beginner'), { currentRank: '初段' })
  near(allFirst.overall.currentExpectation.expectedPt, 24)
})

test('advanced-only results do not score ranks that cannot enter that room', () => {
  const result = calculateStableRank(samples([50, 20, 15, 15]), { currentRank: '七段' }).overall
  assert.equal(result.tier, 'advanced')
  assert.equal(row(result, '初段').expectedPt, null)
  assert.equal(row(result, '三段').expectedPt, null)
  assert.equal(row(result, '初段').admissionBlocked, true)
  assert.ok(Number.isFinite(row(result, '四段').expectedPt))
  assert.ok(row(result, '四段').expectedPt < 84)
  for (const entry of result.expectations) {
    if (entry.expectedPt != null) assert.ok(entry.expectedPt <= 84 + 1e-9, entry.rankName)
  }
})
