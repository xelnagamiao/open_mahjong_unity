import test from 'node:test'
import assert from 'node:assert/strict'
import { SalasasaGameAdapter } from '../src/game2d/salasasa/gameAdapter.ts'

function adapter(step = 5, tactical = false) {
  const value = new SalasasaGameAdapter(101)
  value.accept({ type: 'gamestate/guobiao/game_start', game_info: {
    room_id: 'timing', gamestate_id: 'timing', current_player_index: 0, action_tick: 1,
    max_round: 4, tile_count: 80, current_round: 1, step_time: step, round_time: 20,
    room_type: 'custom', room_rule: 'guobiao', tips: false,
    tactical_call: tactical,
    players_info: [0, 1, 2, 3].map(i => ({
      user_id: 101 + i, username: `p${i}`, hand_tiles_count: 13,
      hand_tiles: i === 0 ? [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24] : undefined,
      discard_tiles: [], combination_tiles: [], combination_mask: [], huapai_list: [],
      player_index: i, original_player_index: i, remaining_time: 20, score: 0,
    })),
  } })
  return value
}

for (const kind of ['broadcast_hand_action', 'ask_other_action']) {
  for (const [roomStep, bank, remainingStep, expected] of [
    [5, 20, undefined, 25000], [5, 20, 5, 25000],
    [5, 20, 2, 22000], [5, 17, 0, 17000],
    [5, 0, 1, 1000], [8, 13, undefined, 21000],
    [8, 13, 0, 13000], [5, 0, 0, 0],
    [0, 20, undefined, 20000], [5, 20, null, 25000],
  ]) {
    test(`${kind}: bank=${bank}, step=${String(remainingStep)}, room=${roomStep}`, () => {
      const value = adapter(roomStep)
      const info = { action_list: kind === 'broadcast_hand_action' ? ['cut'] : ['peng', 'pass'],
        remaining_time: bank, step_remaining: remainingStep, player_index: 0,
        remain_tiles: 80, cut_tile: 11, action_tick: 2 }
      const update = value.accept({ type: `gamestate/guobiao/${kind}`,
        [kind === 'broadcast_hand_action' ? 'ask_hand_action_info' : 'ask_other_action_info']: info })
      assert.equal(update.event.viewer.decision_timer_ms, expected)
    })
  }
}

test('enabling tactical_call alone keeps the ordinary room step plus bank', () => {
  const value = adapter(8, true)
  const update = value.accept({ type: 'gamestate/guobiao/ask_other_action', ask_other_action_info: {
    action_list: ['peng', 'pass'], remaining_time: 13,
    player_index: 0, cut_tile: 11, action_tick: 4,
  } })
  assert.equal(update.event.viewer.decision_timer_ms, 21000)
})

for (const [bank, bankMs, expectedMs] of [[5, undefined, 5000], [3, 2500, 2500]]) {
  test(`actual tactical recheck uses only its ${expectedMs}ms grace`, () => {
    const value = adapter(8, true)
    const update = value.accept({ type: 'gamestate/guobiao/ask_other_action', ask_other_action_info: {
      action_list: ['peng', 'pass'], remaining_time: bank, remaining_time_ms: bankMs,
      step_remaining: 8, step_remaining_ms: 8000, is_tactical_recheck: true,
      player_index: 0, cut_tile: 11, action_tick: 5,
    } })
    assert.equal(update.event.viewer.decision_timer_ms, expectedMs)
  })
}

test('tactical recheck retains its total grace without adding a remaining step', () => {
  const value = adapter(8)
  const update = value.accept({ type: 'gamestate/guobiao/ask_other_action', ask_other_action_info: {
    action_list: ['peng', 'pass'], remaining_time: 2, step_remaining: 5,
    player_index: 0, cut_tile: 11, action_tick: 3, is_tactical_recheck: true,
  } })
  assert.equal(update.event.viewer.decision_timer_ms, 2000)
})

test('Shanghai live play remains outside the Web adapter scope', () => {
  assert.equal(adapter().accept({ type: 'gamestate/shanghai/ask_other_action', ask_other_action_info: {
    action_list: ['peng', 'pass'], remaining_time: 20, step_remaining: 5,
    player_index: 0, cut_tile: 11, action_tick: 3,
  } }), null)
})

for (const kind of ['broadcast_hand_action', 'ask_other_action']) {
  test(`${kind}: optional millisecond fields preserve the actual fractional deadline`, () => {
    const value = adapter(5)
    const info = { action_list: kind === 'broadcast_hand_action' ? ['cut'] : ['peng', 'pass'],
      remaining_time: 20, step_remaining: 1, remaining_time_ms: 19100, step_remaining_ms: 500,
      player_index: 0, remain_tiles: 80, cut_tile: 11, action_tick: 4 }
    const update = value.accept({ type: `gamestate/guobiao/${kind}`,
      [kind === 'broadcast_hand_action' ? 'ask_hand_action_info' : 'ask_other_action_info']: info })
    assert.equal(update.event.viewer.decision_timer_ms, 19600)
  })
}
