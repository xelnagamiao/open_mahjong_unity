import test from 'node:test'
import assert from 'node:assert/strict'
import { collectStableRankSamples } from '../src/utils/stableRankRecords.js'

function game(id = 'g1', title = {}, item = {}) {
  return {
    game_id: id,
    settlement_rank: 2,
    settlement_tie_count: 1,
    settlement_user_id: 100,
    record: {
      game_title: {
        p0_uid: 100,
        p1_uid: 200,
        p2_uid: 300,
        p3_uid: 400,
        rule: 'guobiao',
        room_type: 'match',
        match_queue_type: 'advanced_quanzhuang',
        match_tier: 'advanced',
        max_round: 4,
        ...title,
      },
    },
    ...item,
  }
}

test('samples preserve each actual room and format in a mixed selection', () => {
  const items = [
    game('a'),
    game('b', { match_queue_type: 'beginner_dongfeng', match_tier: 'beginner', max_round: 1 }),
    game('c', { match_queue_type: 'intermediate_banzhuang', match_tier: 'intermediate', max_round: 2 }),
    game('d', { match_queue_type: 'mcrpl_quanzhuang', match_tier: 'mcrpl' }),
  ]
  assert.deepEqual(collectStableRankSamples(items, '100'), {
    samples: [
      { tier: 'advanced', gameType: 'quanzhuang', rank: 2 },
      { tier: 'beginner', gameType: 'dongfeng', rank: 2 },
      { tier: 'intermediate', gameType: 'banzhuang', rank: 2 },
      { tier: 'mcrpl', gameType: 'quanzhuang', rank: 2 },
    ],
    excludedCount: 0,
    exclusions: [],
  })
})

test('historical title gaps use per-record database metadata, including JSON string records', () => {
  const row = game('old', { room_type: null, match_tier: null, match_queue_type: null, max_round: null, rule: null }, {
    room_type: 'match', match_tier: 'beginner', match_type: '2/4_rank', rule: 'guobiao', settlement_rank: '4',
  })
  row.record = JSON.stringify(row.record)
  assert.deepEqual(collectStableRankSamples([row], 100).samples, [{ tier: 'beginner', gameType: 'banzhuang', rank: 4 }])
})

test('title scene fields take priority over metadata and explicit non-ladder rooms are excluded', () => {
  const rows = [
    game('actual', {}, { match_tier: 'beginner', room_type: 'custom', rule: 'riichi' }),
    game('custom', { room_type: 'custom' }, { room_type: 'match' }),
    game('event', { room_type: 'events' }),
    game('riichi', { rule: 'riichi' }),
  ]
  const result = collectStableRankSamples(rows, 100)
  assert.deepEqual(result.samples, [{ tier: 'advanced', gameType: 'quanzhuang', rank: 2 }])
  assert.deepEqual(result.exclusions.map(({ reason, count }) => [reason, count]), [['non_ladder', 2], ['unsupported_rule', 1]])
})

test('unknown or conflicting metadata is not guessed from a known selection', () => {
  const rows = [
    game('room', { room_type: null, match_queue_type: null }),
    game('tier', { match_tier: null, match_queue_type: null }),
    game('format', { max_round: null, match_queue_type: null }),
    game('unsupported', { max_round: 3, match_queue_type: null }),
    game('conflict-tier', { match_tier: 'beginner' }),
    game('conflict-format', { max_round: 2 }),
    game('bad-queue', { match_queue_type: 'advanced_unknown' }),
    game('bad-round-value', { max_round: true, match_queue_type: null }),
  ]
  const result = collectStableRankSamples(rows, 100)
  assert.equal(result.samples.length, 0)
  assert.equal(result.excludedCount, rows.length)
  assert.equal(result.exclusions.find((row) => row.reason === 'conflicting_metadata').count, 2)
})

test('target-bound settlement wins over another player cached rank and absent settlements are excluded', () => {
  const rows = [
    game('target', {}, { rank: 1, settlement_rank: 4 }),
    game('cached-only', {}, { rank: 1, settlement_rank: undefined }),
    game('incomplete', {}, { rank: 1, settlement_rank: null }),
    game('invalid', {}, { rank: 1, settlement_rank: true }),
    game('other-player', {}, { settlement_user_id: 200 }),
  ]
  const result = collectStableRankSamples(rows, 100)
  assert.deepEqual(result.samples, [{ tier: 'advanced', gameType: 'quanzhuang', rank: 4 }])
  assert.deepEqual(result.exclusions.map(({ reason, count }) => [reason, count]), [['missing_settlement', 4]])
})

test('tied ranks average the occupied placements without multiplying the sample count', () => {
  const result = collectStableRankSamples([
    game('two', {}, { settlement_rank: 2, settlement_tie_count: 2 }),
    game('all', {}, { settlement_rank: 1, settlement_tie_count: '4' }),
    game('three', {}, { settlement_rank: 2, settlement_tie_count: 3 }),
  ], 100)
  assert.deepEqual(result.samples, [
    { tier: 'advanced', gameType: 'quanzhuang', rank: 2, rankWeights: [0, 0.5, 0.5, 0] },
    { tier: 'advanced', gameType: 'quanzhuang', rank: 1, rankWeights: [0.25, 0.25, 0.25, 0.25] },
    { tier: 'advanced', gameType: 'quanzhuang', rank: 2, rankWeights: [0, 1 / 3, 1 / 3, 1 / 3] },
  ])
  assert.equal(result.excludedCount, 0)
})

test('missing or impossible tie counts cannot silently treat a tied rank as an outright placement', () => {
  const result = collectStableRankSamples([
    game('old-api', {}, { settlement_tie_count: undefined }),
    game('partial-settlement', {}, { settlement_tie_count: null }),
    game('overflow', {}, { settlement_rank: 4, settlement_tie_count: 2 }),
    game('boolean', {}, { settlement_tie_count: true }),
  ], 100)
  assert.equal(result.samples.length, 0)
  assert.deepEqual(result.exclusions.map(({ reason, count }) => [reason, count]), [['missing_tie_settlement', 4]])
})

test('duplicates, absent players and malformed JSON are reported without inflating sample size', () => {
  const row = game()
  const result = collectStableRankSamples([row, structuredClone(row), game('absent', { p0_uid: 999 }), { record: '{' }], 100)
  assert.equal(result.samples.length, 1)
  assert.equal(result.excludedCount, 3)
  assert.deepEqual(result.exclusions.map(({ reason, count }) => [reason, count]), [['duplicate', 1], ['missing_player', 1], ['invalid_record', 1]])
  assert.equal(collectStableRankSamples([row], null).samples.length, 0)
})
