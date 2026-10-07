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

test('complete server metrics display all nine filtered details without replay analysis', () => {
  const row = {
    ...settlement(), details_available: true, analyzed_games: 2,
    total_rounds: 12, win_count: 4, self_draw_count: 2, deal_in_count: 2,
    total_fan_score: 200, total_win_turn: 36, total_fangchong_score: 48,
    fulu_round_count: 5, cuohe_count: 1,
  }
  for (const base of [null, lifetime]) {
    const stats = mergePlayerRankStats(base, row)
    assert.equal(stats.details_available, true)
    const display = values(stats, { detailed: stats.details_available })
    assert.deepEqual(Object.fromEntries([
      '总回合', '和牌率', '自摸率', '放铳率', '错和率', '副露率', '平均和番', '平均和巡', '平均铳番',
    ].map(label => [label, display[label]])), {
      总回合: '12', 和牌率: '33.33%', 自摸率: '50.00%', 放铳率: '16.67%',
      错和率: '8.33%', 副露率: '41.67%', 平均和番: '50.00', 平均和巡: '9.00', 平均铳番: '24.00',
    })
    assert.equal(display['局均点'], '40.00')
  }
  // A date-filtered response has no fan breakdown; never borrow the lifetime fan counts.
  assert.equal(mergePlayerRankStats(null, row).fan_stats, undefined)
})

test('incomplete metrics stay unavailable for filtered selections while valid lifetime details remain usable', () => {
  const row = { ...settlement(), details_available: false, analyzed_games: 1, total_rounds: null, win_count: null }
  const filtered = mergePlayerRankStats(null, row)
  assert.equal(filtered.details_available, false)
  const display = values(filtered, { detailed: filtered.details_available })
  assert.equal(display['总回合'], '—')
  assert.equal(display['和牌率'], '—')
  assert.equal(display['局均点'], '40.00')
  const allTime = mergePlayerRankStats(lifetime, row)
  assert.equal(allTime.details_available, true)
  assert.equal(allTime.total_rounds, lifetime.total_rounds)
  assert.equal(allTime.win_count, lifetime.win_count)
  assert.deepEqual(allTime.fan_stats, lifetime.fan_stats)
})

test('an empty server selection has complete zero details without NaN or unavailable placeholders', () => {
  const row = { ...settlement(0, 0), details_available: true, analyzed_games: 0,
    total_rounds: 0, win_count: 0, self_draw_count: 0, deal_in_count: 0,
    total_fan_score: 0, total_win_turn: 0, total_fangchong_score: 0, fulu_round_count: 0, cuohe_count: 0 }
  const stats = mergePlayerRankStats(null, row)
  assert.equal(stats.details_available, true)
  assert.ok(Object.values(values(stats, { detailed: stats.details_available })).every(value => /^0(?:\.00%?)?$/.test(value)))
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
