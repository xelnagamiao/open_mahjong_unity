import test from 'node:test'
import assert from 'node:assert/strict'
import { Assets, Container, Text, Texture } from 'pixi.js'

import { MahjongScene } from '../src/game2d/game/scene/MahjongScene.ts'
import { MeldChoices } from '../src/game2d/game/scene/MeldChoices.ts'
import { SalasasaGameAdapter } from '../src/game2d/salasasa/gameAdapter.ts'

const SELF_USER_ID = 100
const noop = () => {}

// Exercise the real Pixi containers, button handlers and scene decision code.
// Rendering, audio, tile animation and wall-clock countdowns are outside these tests.
for (let tile = 1; tile <= 49; tile += 1) {
  Assets.cache.set(`regular-${tile}`, Texture.WHITE)
}

function createTable(t, onInput = noop) {
  const adapter = new SalasasaGameAdapter(SELF_USER_ID)
  const start = adapter.accept({
    type: 'gamestate/guobiao/game_start',
    game_info: {
      room_id: 1,
      gamestate_id: 'tactical-claim-regression',
      current_player_index: 3,
      action_tick: 1,
      max_round: 16,
      tile_count: 80,
      current_round: 1,
      step_time: 5,
      round_time: 60,
      room_type: 'match',
      room_rule: 'guobiao',
      players_info: [0, 1, 2, 3].map((seat) => ({
        user_id: seat === 0 ? SELF_USER_ID : 200 + seat,
        username: `p${seat}`,
        hand_tiles_count: 13,
        hand_tiles: seat === 0 ? [11, 12, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24, 25] : undefined,
        discard_tiles: [],
        combination_tiles: [],
        combination_mask: [],
        huapai_list: [],
        remaining_time: 30,
        player_index: seat,
        original_player_index: seat,
        score: 0,
      })),
    },
  })
  assert.ok(start?.snapshot)

  const sent = []
  const melds = []
  const scene = new MahjongScene((type, payload) => {
    if (type === 'game.input') {
      sent.push({ payload, encoded: adapter.encodeSceneInput(payload) })
      onInput(payload)
    }
  })
  scene.mounted = true
  scene.playSound = noop
  scene.playCallSound = noop
  scene.countdown = {
    running: false,
    visible: false,
    onExpire: null,
    setTimeMillis(ms, stage) { this.running = true; this.ms = ms; this.stage = stage },
    stop() { this.running = false; this.onExpire = null },
  }
  scene.waitDisplay = { setData: noop, reset: noop, visible: false }
  scene.stateDisplay = { setScore: noop, setCurrent: noop, setRound: noop, setRemaining: noop }
  scene.rivers = [0, 1, 2, 3].map(() => ({
    tileList: [], flowerList: [], setPlayerInfo: noop,
  }))
  scene.hands = [0, 1, 2, 3].map((seat) => ({
    leftList: [], rightList: [], drawnTile: null,
    unwaitDiscard: noop,
    discardTile: noop,
    chowFromRiver(...args) { melds.push({ seat, kind: 'chow', args }) },
    pungFromRiver(...args) { melds.push({ seat, kind: 'pung', args }) },
  }))
  t.after(() => {
    scene.clearPendingChoicesTimeout()
    scene.clearAutoActionTimeout()
    scene.clearMeldChoices()
    // Claim labels dismiss themselves after one second; leave their containers
    // alive until then so this harness does not double-destroy Pixi children.
  })

  function accept(message) {
    const accepted = adapter.accept(message)
    assert.ok(accepted, `unhandled message: ${message.type}`)
    for (const event of accepted.events ?? (accepted.event ? [accepted.event] : [])) {
      scene.handleEvent(event)
    }
    return accepted
  }
  function action(action, seat, tick, extras = {}) {
    return accept({
      type: 'gamestate/guobiao/do_action',
      do_action_info: {
        action_list: [action], action_player: seat, action_tick: tick,
        cut_tile: 13, cut_from_player: 3, ...extras,
      },
    })
  }
  function ask(tick, actions = ['chi_left', 'pass'], extras = {}) {
    return accept({
      type: 'gamestate/guobiao/ask_other_action',
      ask_other_action_info: {
        action_list: actions, remaining_time: 8, cut_tile: 13,
        action_tick: tick, player_index: 0, ...extras,
      },
    })
  }
  action('cut', 3, 2)
  return { scene, adapter, sent, melds, action, ask }
}

function getButton(scene, label) {
  const panel = scene.meldChoicesPanel
  assert.ok(panel instanceof MeldChoices, 'expected a real MeldChoices panel')
  assert.ok(panel.parent instanceof Container)
  const button = panel.children.find((child) => child.children.some((part) => (
    part instanceof Text && part.text === label
  )))
  assert.ok(button, `missing ${label} button`)
  return button
}

function click(scene, label) {
  const button = getButton(scene, label)
  button.emit('pointerdown', { button: 0 })
  return button
}

function assertDecisionClosed(scene) {
  assert.ok(scene.meldChoicesPanel === null, 'all action buttons must be removed immediately')
  assert.equal(scene.center.children.some((child) => child instanceof MeldChoices), false)
  assert.deepEqual(scene.currentViewerActions, [], 'submitted actions must not remain available')
  assert.equal(scene.inputEnabled, false, 'input must be disabled while waiting for the server')
  assert.equal(scene.canAct(), false)
  assert.equal(scene.countdown.running, false)
}

test('取消立即关闭面板，不依赖 Salasasa 不会发送的 pass_ack，并拒绝重复回调', (t) => {
  const { scene, sent, ask } = createTable(t)
  ask(3)
  const passButton = getButton(scene, '取消')
  const stalePointerDown = passButton.listeners('pointerdown')[0]
  click(scene, '取消')
  assert.equal(sent.length, 1)
  assert.equal(sent[0].encoded.action, 'pass')
  assert.equal(sent[0].payload.stage_counter, 3)
  assertDecisionClosed(scene)
  stalePointerDown({ button: 0 })
  assert.equal(scene.tryShortcutPass(), false)
  assert.equal(sent.length, 1, 'a consumed decision must not submit twice')
})

test('选择吃后清空待选动作和定时任务，快捷取消与旧按钮回调不能再提交', (t) => {
  const { scene, sent, ask } = createTable(t)
  ask(3)
  const staleChowPointerDown = getButton(scene, '吃').listeners('pointerdown')[0]
  scene.pendingChoicesTimeout = setTimeout(() => assert.fail('stale pending choices ran'), 60_000)
  scene.autoActionTimeout = setTimeout(() => assert.fail('stale automatic action ran'), 60_000)
  click(scene, '吃')
  assert.equal(sent[0].encoded.action, 'chi_left')
  assertDecisionClosed(scene)
  assert.equal(scene.pendingChoicesTimeout, null)
  assert.equal(scene.autoActionTimeout, null)
  assert.equal(scene.tryShortcutPass(), false)
  staleChowPointerDown({ button: 0 })
  assert.equal(sent.length, 1)
})

test('自己吃申请被他家碰申请覆盖，只有最终碰落地时才改变副露', (t) => {
  const { scene, adapter, melds, action, ask } = createTable(t)
  ask(3)
  click(scene, '吃')
  action('chi_left', 0, 3, { is_claim: true, combination_target: 's11' })
  assertDecisionClosed(scene)
  assert.deepEqual(melds, [])
  assert.deepEqual(adapter.seatCombinations, [[], [], [], []])
  assert.deepEqual(adapter.seatDiscards[3], [13])
  // The tick-4 recheck targets the opponent, so this viewer receives no prompt.
  assert.equal(scene.currentStageCounter, 3)
  action('peng', 1, 4, { is_claim: true, combination_target: 'k13' })
  assertDecisionClosed(scene)
  assert.deepEqual(melds, [])
  assert.deepEqual(adapter.seatCombinations, [[], [], [], []])
  action('peng', 1, 5, { combination_target: 'k13' })
  assertDecisionClosed(scene)
  assert.equal(melds.length, 1)
  assert.equal(melds[0].kind, 'pung')
  assert.equal(melds[0].seat, 1)
  assert.deepEqual(adapter.seatCombinations, [[], ['k13'], [], []])
  assert.deepEqual(adapter.seatDiscards[3], [])
})

test('普通取消后他家申请不会恢复按钮，新战术询问可立即重新取消', (t) => {
  const { scene, sent, action, ask } = createTable(t)
  ask(3)
  click(scene, '取消')
  assertDecisionClosed(scene)
  action('peng', 1, 3, { is_claim: true, combination_target: 'k13' })
  assertDecisionClosed(scene)
  ask(4, ['hu_first', 'pass'], { is_tactical_recheck: true, remaining_time: 2 })
  assert.equal(scene.countdown.ms, 2000, 'recheck duration already contains its grace window')
  assert.ok(getButton(scene, '和'))
  click(scene, '取消')
  assertDecisionClosed(scene)
  ask(5, ['hu_first', 'pass'], { is_tactical_recheck: true, remaining_time: 2 })
  assert.equal(scene.inputEnabled, true)
  click(scene, '取消')
  assertDecisionClosed(scene)
  assert.deepEqual(sent.map(({ payload }) => payload.stage_counter), [3, 4, 5])
})

test('force_pass 开启时单独发送放弃，关闭时隐藏且不替代普通快捷取消', (t) => {
  const { scene, sent, ask } = createTable(t)
  scene.appearance = { ...scene.appearance, forcePassEnabled: true }
  ask(3, ['chi_left', 'pass', 'force_pass'])
  assert.ok(getButton(scene, '取消'))
  click(scene, '放弃')
  assert.equal(sent[0].encoded.action, 'force_pass')
  assertDecisionClosed(scene)

  ask(4, ['chi_left', 'pass', 'force_pass'])
  assert.equal(scene.tryShortcutPass(), true)
  assert.equal(sent[1].encoded.action, 'pass')
  assertDecisionClosed(scene)

  scene.appearance = { ...scene.appearance, forcePassEnabled: false }
  ask(5, ['chi_left', 'pass', 'force_pass'])
  assert.equal(scene.currentViewerActions.some((action) => action.kind === 'force_pass'), false)
  assert.equal(scene.meldChoicesPanel.children.some((button) => button.children.some((child) => (
    child instanceof Text && child.text === '放弃'
  ))), false)
})

test('倒计时过期清空动作，旧按钮与快捷取消均不可提交', (t) => {
  const { scene, sent, ask } = createTable(t)
  ask(3)
  const stalePointerDown = getButton(scene, '吃').listeners('pointerdown')[0]
  scene.countdown.onExpire()
  assertDecisionClosed(scene)
  stalePointerDown({ button: 0 })
  assert.equal(scene.tryShortcutPass(), false)
  assert.deepEqual(sent, [])
})

test('提交期间若同步收到新询问，旧按钮清理不会移除新面板', (t) => {
  let table
  table = createTable(t, () => {
    table.ask(4, ['hu_first', 'pass'], { is_tactical_recheck: true, remaining_time: 2 })
  })
  const { scene, sent, ask } = table
  ask(3)
  const oldPanel = scene.meldChoicesPanel
  click(scene, '吃')
  assert.equal(sent.length, 1)
  assert.ok(scene.meldChoicesPanel !== oldPanel)
  assert.ok(getButton(scene, '和'))
  assert.equal(scene.currentStageCounter, 4)
  assert.equal(scene.inputEnabled, true)
  assert.equal(scene.countdown.running, true)
})
