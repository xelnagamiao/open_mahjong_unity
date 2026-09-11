import { RANK_NAMES, TOP_RANK_NAME } from '../constants/rankTable.js'

// Keep these rules in sync with open_mahjong_server/server/match/rank_calculator.py:
// TIER_BASE_SCORE, GAME_TYPE_MULTIPLIER, RANK_AVG_LOSS_PT and tier admission limits.
// A stable rank is the zero of expected PT under an unchanged result distribution.
// Historical PT must never be averaged: each result is rescored at each target rank.
export const STABLE_RANK_TIERS = [
  { value: 'beginner', label: '初级场', basePt: 30, minRankIndex: 0 },
  { value: 'intermediate', label: '中级场', basePt: 65, minRankIndex: 8, maxRankIndex: 16 },
  { value: 'advanced', label: '高级场', basePt: 105, minRankIndex: 13 },
  { value: 'mcrpl', label: 'MCRPL', basePt: 135, minRankIndex: 0, qualificationRequired: true },
]

export const STABLE_RANK_FORMATS = [
  { value: 'dongfeng', label: '东风', multiplier: 0.49 },
  { value: 'banzhuang', label: '半庄', multiplier: 0.7 },
  { value: 'quanzhuang', label: '全庄', multiplier: 1 },
]

export const RANK_LOSS_PT = {
  '10级': 0, '9级': 0, '8级': 0, '7级': 0, '6级': 0, '5级': 0, '4级': 0, '3级': 0,
  '2级': 15, '1级': 35, '初段': 45, '二段': 55, '三段': 65, '四段': 95,
  '五段': 105, '六段': 120, '七段': 135, '八段': 165, '九段': 180, '十段': 0,
}

const TIER_MAP = new Map(STABLE_RANK_TIERS.map((tier) => [tier.value, tier]))
const FORMAT_MAP = new Map(STABLE_RANK_FORMATS.map((format) => [format.value, format]))
const COEFFICIENTS = [0.8, 0.2, 0.3, 0.7]
const FIRST_DAN_INDEX = RANK_NAMES.indexOf('初段')
// 十段固定 PT，其零收益不参与有升降段的安定零点估计。
const TOP_INDEX = RANK_NAMES.indexOf(TOP_RANK_NAME) - 1
const SMALL_SAMPLE_SIZE = 100

// Python round(float, 2) rounds the exact binary float, with ties to even. Ordinary
// Math.round(value * 100) changes some east-only settlement values (e.g. -2.205).
// Decode the float before scaling so both languages produce identical PT cents.
function settlementCents(value) {
  if (value === 0) return 0
  const view = new DataView(new ArrayBuffer(8))
  view.setFloat64(0, Math.abs(value))
  const bits = view.getBigUint64(0)
  const exponent = Number((bits >> 52n) & 0x7ffn) - 1023 - 52
  const significand = (bits & ((1n << 52n) - 1n)) | (1n << 52n)
  const numerator = significand * 100n
  const denominator = 1n << BigInt(-exponent)
  let cents = numerator / denominator
  const remainder = numerator % denominator
  if (remainder * 2n > denominator || (remainder * 2n === denominator && cents % 2n === 1n)) {
    cents += 1n
  }
  return Math.sign(value) * Number(cents)
}

// The server requires Python >= 3.12 (open_mahjong_server/pyproject.toml), whose
// sum(float_values) uses compensated summation. This matters for tied half-cents.
function sumSettlementValues(values) {
  let sum = 0
  let correction = 0
  for (const value of values) {
    const next = sum + value
    correction += Math.abs(sum) >= Math.abs(value) ? (sum - next) + value : (value - next) + sum
    sum = next
  }
  return sum + correction
}

// Integer cents also prevent floating-point cancellation from moving a true zero.
const PT_CENTS = new Map()
const TIED_PT_CENTS = new Map()
for (const tier of STABLE_RANK_TIERS) {
  for (const format of STABLE_RANK_FORMATS) {
    const key = `${tier.value}:${format.value}`
    const values = RANK_NAMES.map((rankName) =>
      COEFFICIENTS.map((coefficient, position) => settlementCents(rankName === TOP_RANK_NAME ? 0 : position < 2
        ? tier.basePt * format.multiplier * coefficient
        : -RANK_LOSS_PT[rankName] * coefficient * format.multiplier)),
    )
    PT_CENTS.set(key, values)
    for (let rank = 1; rank <= 4; rank += 1) {
      for (let tieCount = 2; tieCount <= 5 - rank; tieCount += 1) {
        // GuobiaoGameState.py settles ties by summing already rounded place PT,
        // dividing by occupied places, then rounding that binary float again.
        TIED_PT_CENTS.set(`${key}:${rank}:${tieCount}`, values.map((places) => {
          const sumPt = sumSettlementValues(places.slice(rank - 1, rank - 1 + tieCount).map((cents) => cents / 100))
          return settlementCents(sumPt / tieCount)
        }))
      }
    }
  }
}

function sampleTieCount(sample) {
  if (sample.rankWeights == null) return 1
  const weights = sample.rankWeights
  if (!Array.isArray(weights) || weights.length !== 4
    || weights.some((weight) => !Number.isFinite(weight) || weight < 0)) return null
  const tieCount = weights.filter((weight) => weight > 0).length
  if (!tieCount || sample.rank + tieCount > 5) return null
  const valid = weights.every((weight, index) => Math.abs(weight -
    (index >= sample.rank - 1 && index < sample.rank - 1 + tieCount ? 1 / tieCount : 0)) < 1e-12)
  return valid ? tieCount : null
}

function admissionForRank(tiers, rankIndex) {
  const notes = []
  for (const tierName of tiers) {
    const tier = TIER_MAP.get(tierName)
    if (tier.maxRankIndex != null && rankIndex >= tier.maxRankIndex) {
      notes.push(`${tier.label}七段及以上不可进入；此处仅为沿用该场成绩的理论推算`)
    }
    if (rankIndex < tier.minRankIndex) {
      notes.push(`${tier.label}通常需${RANK_NAMES[tier.minRankIndex]}或特许资格`)
    }
    if (tier.qualificationRequired) {
      notes.push('MCRPL 需要参赛资格，不能仅凭段位判断准入')
    }
  }
  return { admissionLimited: notes.length > 0, admissionNotes: notes }
}

function estimateZero(expectations, sampleCount, noLossSamples) {
  const base = { dan: null, lowerRank: null, upperRank: null, rankIndex: null }
  if (!sampleCount) return { ...base, status: 'empty', label: '暂无有效样本' }
  if (noLossSamples) {
    return { ...base, status: 'no_losses', label: '未观测到三、四位，无法估计安定零点' }
  }
  const top = expectations[TOP_INDEX]
  if (top.expectedPt > 0) {
    return { status: 'top', label: '九段（计分区间上限仍为正收益）', dan: 9, rankIndex: TOP_INDEX,
      lowerRank: top.rankName, upperRank: null }
  }
  const firstNegativeIndex = expectations.findIndex((row) => row.expectedPt < 0)
  const lowerIndex = firstNegativeIndex < 0 ? TOP_INDEX : firstNegativeIndex - 1
  const lower = expectations[lowerIndex]
  const upper = expectations[Math.min(lowerIndex + 1, TOP_INDEX)]
  const fraction = lower.expectedPt === 0 ? 0
    : lower.expectedPt / (lower.expectedPt - upper.expectedPt)
  const rankIndex = lowerIndex + fraction
  const belowDan = rankIndex < FIRST_DAN_INDEX
  const dan = belowDan ? null : rankIndex - FIRST_DAN_INDEX + 1
  const label = belowDan
    ? `低于初段（${lower.rankName}${fraction ? `～${upper.rankName}` : ''}）`
    : fraction ? `安定 ${dan.toFixed(2)} 段` : `安定${lower.rankName}`
  return { status: belowDan ? 'below_dan' : 'balanced', label, dan, rankIndex,
    lowerRank: lower.rankName, upperRank: fraction ? upper.rankName : lower.rankName }
}

function analyzeSamples(samples, { key, tier = null, gameType = null, label }, currentRank) {
  const sampleCount = samples.length
  const rankCounts = [0, 0, 0, 0]
  const tiers = [...new Set(samples.map((sample) => sample.tier))]
  for (const sample of samples) rankCounts[sample.rank - 1] += 1
  const noLossSamples = sampleCount > 0 && samples.every((sample) => sample.rank + sample.tieCount - 1 < 3)
  const expectations = RANK_NAMES.map((rankName, rankIndex) => {
    let sum = 0
    let sumSquares = 0
    for (const sample of samples) {
      const key = `${sample.tier}:${sample.gameType}`
      const cents = sample.tieCount === 1 ? PT_CENTS.get(key)[rankIndex][sample.rank - 1]
        : TIED_PT_CENTS.get(`${key}:${sample.rank}:${sample.tieCount}`)[rankIndex]
      sum += cents
      sumSquares += cents * cents
    }
    const expectedPt = sampleCount ? sum / sampleCount / 100 : null
    // Approximate 95% confidence interval for the mean of independent games.
    // It is not a prediction interval for future rank and does not model changing
    // opponents, strength, correlated sessions, or PT resets on promotion/demotion.
    const variance = sampleCount > 1
      ? Math.max(0, (sumSquares - sum * sum / sampleCount) / (sampleCount - 1)) / 10000
      : null
    const margin = variance == null ? null : 1.96 * Math.sqrt(variance / sampleCount)
    return { rankName, rankIndex, dan: rankIndex >= FIRST_DAN_INDEX ? rankIndex - FIRST_DAN_INDEX + 1 : null,
      lossPt: RANK_LOSS_PT[rankName], expectedPt,
      confidenceLow: margin == null ? null : expectedPt - margin,
      confidenceHigh: margin == null ? null : expectedPt + margin,
      ...admissionForRank(tiers, rankIndex) }
  })
  const estimate = estimateZero(expectations, sampleCount, noLossSamples)
  const currentExpectation = sampleCount ? expectations.find((row) => row.rankName === currentRank) ?? null : null
  const relevantIndices = new Set([
    ...(estimate.rankIndex == null ? [] : [Math.floor(estimate.rankIndex), Math.ceil(estimate.rankIndex)]),
    ...(currentExpectation ? [currentExpectation.rankIndex] : []),
    ...(noLossSamples ? [TOP_INDEX] : []),
  ])
  const admissionNotes = [...new Set([...relevantIndices].flatMap((rankIndex) =>
    admissionForRank(tiers, rankIndex).admissionNotes))]
  return { key, tier, gameType, label, sampleCount, rankCounts,
    rankRates: rankCounts.map((count) => sampleCount ? count / sampleCount : 0),
    smallSample: sampleCount > 0 && sampleCount < SMALL_SAMPLE_SIZE,
    noLossSamples, expectations, currentExpectation, estimate,
    admissionLimited: admissionNotes.length > 0, admissionNotes }
}

/**
 * Samples must be one completed ranked game each, with its actual settled place:
 * { tier: 'advanced', gameType: 'quanzhuang', rank: 1 }. Ties carry rankWeights,
 * e.g. rank: 2, rankWeights: [0, 0.5, 0.5, 0] occupies places 2 and 3. Each game
 * remains one observation; displayed rates use its actual competition rank.
 * Validate replay completion and resolve tied scores upstream. Unknown/custom rooms and invalid places are
 * excluded here; never silently reinterpret them as beginner ranked games.
 *
 * Groups keep room and format separate. Overall retains each game's own rewards
 * and format multiplier, assuming the same mix is played in future. Every row is
 * a theoretical rescore at that rank; admission notes limit practical use.
 */
export function calculateStableRank(samples, { currentRank = null } = {}) {
  const input = Array.isArray(samples) ? samples : []
  const valid = input.filter((sample) => sample && TIER_MAP.has(sample.tier)
    && FORMAT_MAP.has(sample.gameType) && Number.isInteger(sample.rank)
    && sample.rank >= 1 && sample.rank <= 4 && sampleTieCount(sample) != null)
    .map((sample) => ({ ...sample, tieCount: sampleTieCount(sample) }))
  const resolvedCurrentRank = RANK_NAMES.includes(currentRank) ? currentRank : null
  const groups = []
  for (const tier of STABLE_RANK_TIERS) {
    for (const format of STABLE_RANK_FORMATS) {
      const grouped = valid.filter((sample) => sample.tier === tier.value && sample.gameType === format.value)
      if (!grouped.length) continue
      groups.push(analyzeSamples(grouped, { key: `${tier.value}:${format.value}`,
        tier: tier.value, gameType: format.value, label: `${tier.label} · ${format.label}` }, resolvedCurrentRank))
    }
  }
  return { sampleCount: valid.length, excludedCount: input.length - valid.length,
    currentRank: resolvedCurrentRank, groups,
    overall: analyzeSamples(valid, { key: 'overall', label: '按当前场次比例综合' }, resolvedCurrentRank) }
}
