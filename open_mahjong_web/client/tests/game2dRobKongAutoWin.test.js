import test from 'node:test'
import assert from 'node:assert/strict'

import { SalasasaGameAdapter, salasasaTileToMmcr } from '../src/game2d/salasasa/gameAdapter.ts'

const SELF_USER_ID = 100

function player(seat, extras = {}) {
  return {
    user_id: seat === 0 ? SELF_USER_ID : 200 + seat,
    username: `p${seat}`,
    hand_tiles_count: 13,
    hand_tiles: seat === 0 ? [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24] : undefined,
    discard_tiles: [],
    combination_tiles: [],
    combination_mask: [],
    huapai_list: [],
    remaining_time: 30,
    player_index: seat,
    original_player_index: seat,
    score: 0,
    ...extras,
  }
}

function startAdapter() {
  const adapter = new SalasasaGameAdapter(SELF_USER_ID)
  const accepted = adapter.accept({
    type: 'gamestate/guobiao/game_start',
    game_info: {
      room_id: 1,
      gamestate_id: 'g-rob-kong',
      current_player_index: 0,
      action_tick: 1,
      max_round: 16,
      tile_count: 80,
      current_round: 1,
      step_time: 5,
      round_time: 60,
      room_type: 'match',
      room_rule: 'guobiao',
      players_info: [0, 1, 2, 3].map((seat) => player(seat)),
    },
  })
  assert.ok(accepted?.snapshot)
  return adapter
}

function doAction(adapter, info) {
  return adapter.accept({
    type: 'gamestate/guobiao/do_action',
    do_action_info: {
      action_tick: 2,
      ...info,
    },
  })
}

test('抢杠询问映射为 rob_added_kong_win，回传保留服务端 hu_* 而不是按上一手弃牌重算', () => {
  const adapter = startAdapter()

  doAction(adapter, {
    action_list: ['cut'],
    action_player: 3,
    cut_tile: 19,
    cut_class: true,
  })
  doAction(adapter, {
    action_list: ['jiagang'],
    action_player: 1,
    combination_target: 'k15',
  })

  const asked = adapter.accept({
    type: 'gamestate/guobiao/ask_other_action',
    ask_other_action_info: {
      action_list: ['hu_third', 'pass'],
      remaining_time: 8,
      cut_tile: 15,
      action_tick: 9,
      player_index: 0,
    },
  })

  const actions = asked?.event?.viewer?.available_actions ?? []
  const rob = actions.find((action) => action.kind === 'rob_added_kong_win')
  const ron = actions.find((action) => action.kind === 'discard_win')
  assert.ok(rob, `expected rob_added_kong_win, got ${JSON.stringify(actions)}`)
  assert.equal(ron, undefined)
  assert.equal(rob.server_action, 'hu_third')
  assert.equal(rob.tile, salasasaTileToMmcr(15))
  assert.equal(asked.event.event.actor_seat, 1)

  const encoded = adapter.encodeSceneInput({
    kind: 'rob_added_kong_win',
    tile: rob.tile,
    server_action: rob.server_action,
    stage_counter: 9,
  })
  assert.equal(encoded?.action, 'hu_third')

  const encodedWithoutServerAction = adapter.encodeSceneInput({
    kind: 'rob_added_kong_win',
    tile: rob.tile,
    stage_counter: 9,
  })
  assert.equal(encodedWithoutServerAction?.action, 'hu_third')
})

test('普通荣和仍映射 discard_win，且按弃牌者编码 hu_*', () => {
  const adapter = startAdapter()
  doAction(adapter, {
    action_list: ['cut'],
    action_player: 3,
    cut_tile: 19,
    cut_class: true,
  })

  const asked = adapter.accept({
    type: 'gamestate/guobiao/ask_other_action',
    ask_other_action_info: {
      action_list: ['hu_first', 'pass'],
      remaining_time: 8,
      cut_tile: 19,
      action_tick: 4,
      player_index: 0,
    },
  })
  const actions = asked?.event?.viewer?.available_actions ?? []
  const ron = actions.find((action) => action.kind === 'discard_win')
  assert.ok(ron, `expected discard_win, got ${JSON.stringify(actions)}`)
  assert.equal(ron.server_action, 'hu_first')
  assert.equal(actions.some((action) => action.kind === 'rob_added_kong_win'), false)

  const encoded = adapter.encodeSceneInput({
    kind: 'discard_win',
    tile: ron.tile,
    server_action: ron.server_action,
    stage_counter: 4,
  })
  assert.equal(encoded?.action, 'hu_first')
})

test('无人抢杠后摸岭上牌会关闭抢杠窗口，后续点和不再当成抢杠', () => {
  const adapter = startAdapter()
  doAction(adapter, {
    action_list: ['jiagang'],
    action_player: 1,
    combination_target: 'k15',
  })
  doAction(adapter, {
    action_list: ['deal_gang_tile'],
    action_player: 1,
    deal_tile: 22,
  })
  doAction(adapter, {
    action_list: ['cut'],
    action_player: 1,
    cut_tile: 22,
    cut_class: true,
  })

  const asked = adapter.accept({
    type: 'gamestate/guobiao/ask_other_action',
    ask_other_action_info: {
      action_list: ['hu_third', 'pass'],
      remaining_time: 8,
      cut_tile: 22,
      action_tick: 12,
      player_index: 0,
    },
  })
  const actions = asked?.event?.viewer?.available_actions ?? []
  assert.equal(actions.some((action) => action.kind === 'rob_added_kong_win'), false)
  assert.ok(actions.some((action) => action.kind === 'discard_win'))
})
