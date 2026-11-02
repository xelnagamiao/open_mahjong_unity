import test from 'node:test'
import assert from 'node:assert/strict'
import { SettlementSync } from '../src/game2d/salasasa/settlementSync.js'
import { SalasasaGameAdapter } from '../src/game2d/salasasa/gameAdapter.ts'

const sync = (extra = {}) => ({ gamestate_id: 'settlement', round_id: 'settlement:1',
  result_id: 'settlement:1:1', sequence: 1, revision: 2, phase: 'waiting_ready',
  restore: false, remaining_ms: 8000, server_time_ms: 5000, deadline_ms: 13000,
  player_to_ready: { 0: true, 1: false, 2: true, 3: false }, ...extra })
const result = { hepai_player_index: 0, hu_class: 'hu_self', hu_score: 8,
  hu_fan: ['门前清'], player_to_score: { 0: 24, 1: -8, 2: -8, 3: -8 },
  score_changes: { 0: 24, 1: -8, 2: -8, 3: -8 }, next_status: 'round_end_by_ready' }
function start(metadata = sync(), restoring = true) {
  return { type: 'gamestate/guobiao/game_start', settlement_sync: metadata,
    game_info: { room_id: 1, gamestate_id: 'settlement', current_player_index: 0,
      action_tick: 20, max_round: 4, tile_count: 70, current_round: 1,
      step_time: 5, round_time: 20, room_type: 'custom', room_rule: 'guobiao',
      players_info: [0, 1, 2, 3].map(i => ({ user_id: 101 + i, username: `p${i}`,
        hand_tiles_count: 13, hand_tiles: i === 0 ? [11,11,11,12,13,14,21,22,23,31,32,33,41] : undefined,
        discard_tiles: [], combination_tiles: [], combination_mask: [], huapai_list: [],
        player_index: i, original_player_index: i, remaining_time: 8,
        score: result.player_to_score[i], score_history: [String(result.score_changes[i])], round_number_history: [1] })) },
    settlement_restore: restoring ? [{ type: 'gamestate/guobiao/show_result',
      show_result_info: result, settlement_sync: { ...metadata, restore: true } }] : undefined }
}

test('reconnect restores immediately, preserves absolute scores/history and uses result-scoped ready', () => {
  const adapter = new SalasasaGameAdapter(101)
  const update = adapter.accept(start(sync({ remaining_ms: 2250 })))
  assert.equal(update.restored, true)
  assert.deepEqual(update.result, result)
  assert.equal(update.event, undefined)
  assert.equal(adapter.snapshot.seats[0].score, 24)
  assert.deepEqual(adapter.gameInfo.players_info[0].score_history, ['24'])
  assert.equal(adapter.readyMessage().settlement_id, 'settlement:1:1')
  assert.equal(adapter.settlement.remainingSeconds(), 3)
  assert.equal(update.sync.player_to_ready[0], true)
  // Existing rule-specific result resend following game_start is harmless.
  assert.equal(adapter.accept({ type: 'gamestate/guobiao/show_result',
    show_result_info: result, settlement_sync: sync() }), null)
  assert.equal(adapter.snapshot.seats[0].score, 24)
})

test('live duplicates are suppressed, a later reconnect may restore the same panel', () => {
  const adapter = new SalasasaGameAdapter(101)
  adapter.accept(start(sync({ result_id: '', sequence: 0, revision: 0 }), false))
  const packet = { type: 'gamestate/guobiao/show_result', show_result_info: result, settlement_sync: sync() }
  assert.ok(adapter.accept(packet).event)
  assert.equal(adapter.accept(packet), null)
  assert.equal(adapter.accept(start()).restored, true)
  assert.equal(adapter.accept(start()).restored, true)
})

test('monotonic time survives background throttling and updates cannot extend the deadline', () => {
  let now = 0
  const clock = new SettlementSync(() => now)
  clock.accept(sync())
  now = 3500
  assert.equal(clock.remainingSeconds(), 5)
  clock.accept(sync({ revision: 3, remaining_ms: 5000 }))
  assert.equal(clock.remainingSeconds(), 5)
  now = 7900
  assert.equal(clock.remainingSeconds(), 1)
  now = 12000
  assert.equal(clock.remainingSeconds(), 0)
})

test('late ready and result packets cannot replace a newer step or next round', () => {
  const clock = new SettlementSync(() => 0)
  clock.accept(sync())
  clock.accept(sync({ sequence: 2, result_id: 'step2' }))
  assert.equal(clock.accept(sync({ revision: 90 })), false)
  clock.accept(sync({ round_id: 'settlement:2', result_id: '', sequence: 2, revision: 0 }), true)
  assert.equal(clock.accept(sync({ sequence: 2, result_id: 'step2', revision: 90 })), false)
  assert.equal(clock.accept(sync({ gamestate_id: 'old-game', sequence: 100 })), false)
})


test('time spent in the client delivery queue is deducted from the server budget', () => {
  let now = 4000
  const clock = new SettlementSync(() => now)
  clock.accept(sync({ remaining_ms: 5000 }), true, 1000)
  assert.equal(clock.remainingSeconds(), 2)
  now = 6500
  assert.equal(clock.remainingSeconds(), 0)
})
