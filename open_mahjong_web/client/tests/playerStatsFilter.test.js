import test from 'node:test'
import assert from 'node:assert/strict'
import {
  buildPlayerStatsRows,
  canUsePrestoredPlayerStats,
  dateRangeToQueryParams,
  mergePlayerRankStats,
} from '../src/utils/statsDisplay.js'

const lifetime = {
  total_games: 200, total_round_score: 20000, total_rounds: 800,
  win_count: 200, self_draw_count: 100, deal_in_count: 160,
  first_place_count: 50, second_place_count: 50, third_place_count: 50, fourth_place_count: 50,
  fan_stats: { qingyise: 10 },
}
const settlement = (score = 80, games = 2) => ({
  total_games: games, total_round_score: score,
  first_place_count: games ? 1 : 0, second_place_count: 0,
  third_place_count: 0, fourth_place_count: games ? games - 1 : 0,
})
const values = (stats, options) => Object.fromEntries(buildPlayerStatsRows(stats, options).map(row => [row.label, row.value]))

test('positive and negative filtered averages replace the lifetime numerator together with the denominator', () => {
  for (const [score, expected] of [[80, '40.00'], [-80, '-40.00'], [0, '0.00']]) {
    const stats = mergePlayerRankStats(lifetime, settlement(score))
    const display = values(stats)
    assert.equal(display['总对局'], '2')
    assert.equal(display['局均点'], expected)
    assert.equal(display['平均顺位'], '2.50')
  }
  assert.equal(lifetime.total_games, 200)
  assert.equal(lifetime.total_round_score, 20000)
})

test('a date range excludes lifetime details, including while the filtered response is pending', () => {
  const dateRange = ['2026-08-01', '2026-08-31']
  for (const scene of ['rank', 'custom', 'beginner', 'events']) {
    assert.equal(Boolean(canUsePrestoredPlayerStats({ scene, dateRange })), false)
  }
  assert.equal(mergePlayerRankStats(null, null), null)
  const filtered = mergePlayerRankStats(null, settlement())
  const display = values(filtered, { detailed: false })
  assert.equal(filtered.fan_stats, undefined)
  assert.equal(display['局均点'], '40.00')
  for (const label of ['总回合', '和牌率', '自摸率', '放铳率', '副露率', '平均和番', '平均和巡']) {
    assert.equal(display[label], '—', label)
  }
})

test('clearing dates restores lifetime details and a cached selection keeps its own score', () => {
  for (const scene of ['rank', 'custom']) {
    assert.equal(canUsePrestoredPlayerStats({ scene, dateRange: null }), true)
  }
  assert.equal(canUsePrestoredPlayerStats({ scene: 'beginner', dateRange: null }), false)
  assert.equal(canUsePrestoredPlayerStats({ scene: 'custom', recordsOnly: true }), false)
  assert.equal(values(mergePlayerRankStats(lifetime, null))['总回合'], '800')
  const positive = mergePlayerRankStats(null, settlement(80))
  const negative = mergePlayerRankStats(null, settlement(-100))
  assert.equal(values(negative)['局均点'], '-50.00')
  assert.equal(values(positive)['局均点'], '40.00')
})

test('empty selections show zero while missing or unsupported scores never use a lifetime fallback', () => {
  assert.equal(values(mergePlayerRankStats(lifetime, settlement(0, 0)))['局均点'], '0.00')
  for (const score of [null, undefined]) {
    const row = settlement()
    row.total_round_score = score
    assert.equal(values(mergePlayerRankStats(lifetime, row))['局均点'], '—')
  }
})

test('record, rank and scene queries can share whole-day date boundaries', () => {
  assert.deepEqual(dateRangeToQueryParams(['2026-08-31', '2026-08-31']), {
    date_from: '2026-08-31T00:00:00', date_to: '2026-09-01T00:00:00',
  })
  assert.deepEqual(dateRangeToQueryParams(null), {})
})
