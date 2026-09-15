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

function rankBlockedInTiers(tiers, rankIndex) {
  return tiers.some((tierName) => {
    const tier = TIER_MAP.get(tierName)
    if (!tier) return false
    if (tier.maxRankIndex != null && rankIndex >= tier.maxRankIndex) return true
    if (rankIndex < tier.minRankIndex) return true
    return false
  })
}

function admissionForRank(tiers, rankIndex) {
  const notes = []
  const admissionBlocked = rankBlockedInTiers(tiers, rankIndex)
  for (const tierName of tiers) {
    const tier = TIER_MAP.get(tierName)
    if (tier.maxRankIndex != null && rankIndex >= tier.maxRankIndex) {
      notes.push(`${tier.label}不可进入`)
    }
    if (rankIndex < tier.minRankIndex) {
      notes.push(`${tier.label}需${RANK_NAMES[tier.minRankIndex]}`)
    }
    if (tier.qualificationRequired) {
      notes.push('MCRPL 需资格')
    }
  }
  return { admissionLimited: notes.length > 0, admissionBlocked, admissionNotes: notes }
}

function estimateZero(expectations, sampleCount, noLossSamples) {
  const base = { dan: null, lowerRank: null, upperRank: null, rankIndex: null }
  if (!sampleCount) return { ...base, status: 'empty', label: '—' }
  if (noLossSamples) {
    return { ...base, status: 'no_losses', label: '无法估计' }
  }
  const playable = expectations.filter((row) => Number.isFinite(row.expectedPt) && row.rankIndex <= TOP_INDEX)
  if (!playable.length) return { ...base, status: 'empty', label: '—' }
  const top = playable[playable.length - 1]
  if (top.expectedPt > 0) {
    const dan = top.rankIndex < FIRST_DAN_INDEX ? null : top.rankIndex - FIRST_DAN_INDEX + 1
    return { status: 'top', label: top.rankIndex === TOP_INDEX ? '安定九段' : `安定${top.rankName}`,
      dan, rankIndex: top.rankIndex, lowerRank: top.rankName, upperRank: null }
  }
  if (playable[0].expectedPt < 0) {
    return { ...base, status: 'below_dan', label: `低于${playable[0].rankName}`,
      rankIndex: playable[0].rankIndex, upperRank: playable[0].rankName }
  }
  const firstNegative = playable.findIndex((row) => row.expectedPt < 0)
  const lower = playable[firstNegative - 1]
  const upper = playable[firstNegative]
  const fraction = lower.expectedPt === 0 ? 0
    : lower.expectedPt / (lower.expectedPt - upper.expectedPt)
  const rankIndex = lower.rankIndex + fraction * (upper.rankIndex - lower.rankIndex)
  const belowDan = rankIndex < FIRST_DAN_INDEX
  const dan = belowDan ? null : rankIndex - FIRST_DAN_INDEX + 1
  const label = belowDan
    ? '低于初段'
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
    const admission = admissionForRank(tiers, rankIndex)
    const base = { rankName, rankIndex, dan: rankIndex >= FIRST_DAN_INDEX ? rankIndex - FIRST_DAN_INDEX + 1 : null,
      lossPt: RANK_LOSS_PT[rankName], ...admission }
    if (admission.admissionBlocked) {
      return { ...base, expectedPt: null, confidenceLow: null, confidenceHigh: null }
    }
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
    const variance = sampleCount > 1
      ? Math.max(0, (sumSquares - sum * sum / sampleCount) / (sampleCount - 1)) / 10000
      : null
    const margin = variance == null ? null : 1.96 * Math.sqrt(variance / sampleCount)
    return { ...base, expectedPt,
      confidenceLow: margin == null ? null : expectedPt - margin,
      confidenceHigh: margin == null ? null : expectedPt + margin }
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

const DEFAULT_TIER_ORDER = ['advanced', 'intermediate', 'beginner', 'mcrpl']

function roomBlockedAtRank(tierValue, currentRank) {
  const tier = TIER_MAP.get(tierValue)
  const rankIndex = RANK_NAMES.indexOf(currentRank)
  return rankIndex >= 0 && tier?.maxRankIndex != null && rankIndex >= tier.maxRankIndex
}

export function scoringRows(tier, rankName) {
  const rankIndex = RANK_NAMES.includes(rankName) ? RANK_NAMES.indexOf(rankName) : RANK_NAMES.indexOf('初段')
  return STABLE_RANK_FORMATS.map((format) => ({
    key: `${tier}:${format.value}`,
    label: format.label,
    places: (PT_CENTS.get(`${tier}:${format.value}`)?.[rankIndex] || []).map((cents) => cents / 100),
  })).filter((row) => row.places.length === 4)
}

export function defaultStableRankKey(groups, currentRank = null) {
  if (!groups?.length) return ''
  for (const tier of DEFAULT_TIER_ORDER) {
    const group = groups.find((item) => item.tier === tier)
    if (group && !roomBlockedAtRank(tier, currentRank)) return group.key
  }
  return groups[0].key
}

/**
 * Headline groups are per room. Formats inside a room keep their own PT weights.
 * Different rooms are never pooled into one 安定段位.
 */
export function calculateStableRank(samples, { currentRank = null } = {}) {
  const input = Array.isArray(samples) ? samples : []
  const valid = input.filter((sample) => sample && TIER_MAP.has(sample.tier)
    && FORMAT_MAP.has(sample.gameType) && Number.isInteger(sample.rank)
    && sample.rank >= 1 && sample.rank <= 4 && sampleTieCount(sample) != null)
    .map((sample) => ({ ...sample, tieCount: sampleTieCount(sample) }))
  const resolvedCurrentRank = RANK_NAMES.includes(currentRank) ? currentRank : null
  const formatGroups = []
  for (const tier of STABLE_RANK_TIERS) {
    for (const format of STABLE_RANK_FORMATS) {
      const grouped = valid.filter((sample) => sample.tier === tier.value && sample.gameType === format.value)
      if (!grouped.length) continue
      formatGroups.push(analyzeSamples(grouped, { key: `${tier.value}:${format.value}`,
        tier: tier.value, gameType: format.value, label: `${tier.label} · ${format.label}` }, resolvedCurrentRank))
    }
  }
  const groups = []
  for (const tier of STABLE_RANK_TIERS) {
    const grouped = valid.filter((sample) => sample.tier === tier.value)
    if (!grouped.length) continue
    groups.push(analyzeSamples(grouped, {
      key: tier.value, tier: tier.value, label: tier.label,
    }, resolvedCurrentRank))
  }
  const defaultKey = defaultStableRankKey(groups, resolvedCurrentRank)
  const overall = groups.find((group) => group.key === defaultKey)
    || analyzeSamples([], { key: 'overall', label: '' }, resolvedCurrentRank)
  return { sampleCount: valid.length, excludedCount: input.length - valid.length,
    currentRank: resolvedCurrentRank, defaultKey, groups, formatGroups, overall }
}
