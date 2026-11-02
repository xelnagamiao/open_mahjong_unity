import test from 'node:test'
import assert from 'node:assert/strict'
import { Assets, Container, Texture } from 'pixi.js'

import { MeldChoices } from '../src/game2d/game/scene/MeldChoices.ts'
import { SalasasaGameAdapter, salasasaTileToMmcr } from '../src/game2d/salasasa/gameAdapter.ts'

// Keep the real choice containers and pointer handlers; only GPU textures are stubbed.
for (let tile = 1; tile <= 49; tile++) Assets.cache.set(`regular-${tile}`, Texture.WHITE)

function createAdapter(hand, combinations = []) {
  const adapter = new SalasasaGameAdapter(100)
  adapter.accept({
    type: 'gamestate/guobiao/game_start',
    game_info: {
      room_id: 1, gamestate_id: 'kong-choices', current_player_index: 0,
      action_tick: 1, max_round: 16, tile_count: 49, current_round: 1,
      step_time: 5, round_time: 60, room_type: 'custom', room_rule: 'guobiao',
      tips: false,
      players_info: [0, 1, 2, 3].map(seat => ({
        user_id: 100 + seat, username: `p${seat}`,
        hand_tiles_count: seat === 0 ? hand.length : 13,
        hand_tiles: seat === 0 ? hand : undefined,
        discard_tiles: [], combination_tiles: seat === 0 ? combinations : [],
        combination_mask: [], huapai_list: [], remaining_time: 30,
        player_index: seat, original_player_index: seat, score: 0,
      })),
    },
  })
  return adapter
}

function prompt(adapter, actions, tick = 2, seat = 0) {
  return adapter.accept({
    type: 'gamestate/guobiao/broadcast_hand_action',
    ask_hand_action_info: {
      action_list: actions, remaining_time: 30, player_index: seat,
      remain_tiles: 49, action_tick: tick,
    },
  }).event.viewer
}

const twoConcealed = [32, 32, 32, 32, 33, 33, 33, 33, 11, 12, 23, 25, 31, 37]
const cases = [
  { name: 'two concealed kongs', hand: twoConcealed,
    actions: ['angang', 'angang', 'cut'], expected: [['concealed_kong', 32], ['concealed_kong', 33]] },
  { name: 'three concealed kongs', hand: [11, 11, 11, 11, 21, 21, 21, 21, 31, 31, 31, 31, 45, 46],
    actions: ['angang', 'angang', 'angang', 'cut'], expected: [['concealed_kong', 11], ['concealed_kong', 21], ['concealed_kong', 31]] },
  { name: 'two added kongs', hand: [32, 33, 11, 12, 23, 25, 31, 37], combinations: ['k32', 'k33'],
    actions: ['jiagang', 'jiagang', 'cut'], expected: [['added_kong', 32], ['added_kong', 33]] },
  { name: 'concealed and added kongs together', hand: [11, 11, 11, 11, 32, 33, 23, 25], combinations: ['k32', 'k33'],
    actions: ['angang', 'jiagang', 'jiagang', 'cut'], expected: [['concealed_kong', 11], ['added_kong', 32], ['added_kong', 33]] },
  { name: 'one concealed kong', hand: [32, 32, 32, 32, 11, 12, 13, 21, 22, 23, 25, 31, 33, 37],
    actions: ['angang', 'cut'], expected: [['concealed_kong', 32]] },
  { name: 'one added kong', hand: [32, 11, 12, 13, 21, 22, 23, 25, 31, 33, 37], combinations: ['k32'],
    actions: ['jiagang', 'cut'], expected: [['added_kong', 32]] },
  { name: 'a single action word still expands all valid targets', hand: twoConcealed,
    actions: ['angang', 'cut'], expected: [['concealed_kong', 32], ['concealed_kong', 33]] },
]

for (const scenario of cases) {
  test(`${scenario.name}: one visible choice per target, with the correct click payload`, () => {
    const adapter = createAdapter(scenario.hand, scenario.combinations)
    const viewer = prompt(adapter, scenario.actions)
    assert.deepEqual(viewer.available_actions, [
      ...scenario.expected.map(([kind, tile]) => ({ kind, tile: salasasaTileToMmcr(tile) })),
      { kind: 'discard_tile' },
    ])

    const expectedMessages = scenario.expected.map(([kind, targetTile]) => ({
      type: 'gamestate/GB/send_action', gamestate_id: 'kong-choices',
      action: kind === 'concealed_kong' ? 'angang' : 'jiagang',
      targetTile, chiComboIndex: 0, action_tick: 2,
    }))
    const sent = []
    for (let index = 0; index < scenario.expected.length; index++) {
      const parent = new Container()
      let cleared = 0
      const panel = new MeldChoices(parent, 'draw', viewer, null, null, action => {
        sent.push(adapter.encodeSceneInput({ ...action, stage_counter: 2 }))
        return true
      }, () => { cleared++ })
      assert.equal(panel.children.length, scenario.expected.length)
      panel.children[index].emit('pointerdown', { button: 0 })
      assert.equal(cleared, 1)
      assert.equal(parent.children.length, 0)
      panel.destroy({ children: true })
      parent.destroy({ children: true })
    }
    // UI priority puts added kongs before concealed kongs; keep every target/action pair.
    assert.deepEqual(sent.sort((a, b) => a.targetTile - b.targetTile),
      expectedMessages.sort((a, b) => a.targetTile - b.targetTile))
  })
}

test('repeated prompts replace choices, while another seat or a prompt without kong permission has none', () => {
  const adapter = createAdapter(twoConcealed)
  const first = prompt(adapter, ['angang', 'angang', 'cut'])
  const repeated = prompt(adapter, ['angang', 'angang', 'cut'], 3)
  assert.equal(first.available_actions.length, 3)
  assert.deepEqual(repeated.available_actions, first.available_actions)
  assert.deepEqual(prompt(adapter, ['cut'], 4).available_actions, [{ kind: 'discard_tile' }])
  assert.deepEqual(prompt(adapter, [], 5).available_actions, [])
  assert.deepEqual(prompt(adapter, ['angang', 'angang', 'cut'], 6, 1).available_actions, [])
})

test('different chow directions and other claim actions remain separate', () => {
  const adapter = createAdapter(twoConcealed)
  const viewer = adapter.accept({
    type: 'gamestate/guobiao/ask_other_action',
    ask_other_action_info: {
      action_list: ['chi_left', 'chi_mid', 'chi_right', 'peng', 'gang', 'hu_first', 'pass'],
      remaining_time: 8, cut_tile: 25, action_tick: 2, player_index: 0,
    },
  }).event.viewer
  assert.equal(viewer.available_actions.length, 7)
  assert.deepEqual(viewer.available_actions.map(action =>
    adapter.encodeSceneInput({ ...action, stage_counter: 2 }).action),
  ['chi_left', 'chi_mid', 'chi_right', 'peng', 'gang', 'hu_first', 'pass'])
})
