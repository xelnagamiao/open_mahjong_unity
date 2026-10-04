import test from 'node:test'
import assert from 'node:assert/strict'

import { SalasasaGameAdapter } from '../src/game2d/salasasa/gameAdapter.ts'
import { MahjongScene } from '../src/game2d/game/scene/MahjongScene.ts'
import { RecordReplay } from '../src/game2d/replay/recordReplay.ts'

const originals = [2, 0, 3, 1]
const countsOf = (seats) => seats.map((seat) => seat.duplicate_remaining_tile_count)

function start(adapter, counts, order = originals, isDuplicate = counts !== undefined) {
  return adapter.accept({
    type: 'gamestate/guobiao/game_start',
    game_info: {
      room_id: 77, gamestate_id: 'duplicate-counts', room_rule: 'guobiao',
      room_type: 'friend', current_player_index: 0, action_tick: 1, max_round: 1,
      tile_count: 91, current_round: 1, step_time: 5, round_time: 60,
      is_duplicate: isDuplicate,
      ...(counts === undefined ? {} : { duplicate_remaining_tiles: counts }),
      players_info: order.map((original, seat) => ({
        player_index: seat, original_player_index: original, user_id: 100 + seat,
        username: `player-${seat}`, score: 0, hand_tiles_count: 13,
        ...(seat === 2 ? { hand_tiles: [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24] } : {}),
      })),
    },
  }).snapshot
}

function action(adapter, counts, extras = {}) {
  return adapter.accept({
    type: 'gamestate/guobiao/do_action',
    do_action_info: {
      action_list: ['deal_tile'], action_player: 0, action_tick: 2, deal_tile: 0,
      ...(counts === undefined ? {} : { duplicate_remaining_tiles: counts }),
      ...extras,
    },
  }).events
}

test('初始和重连余牌按原始座位映射，保留 0，普通局重新初始化后清空', () => {
  const adapter = new SalasasaGameAdapter(102)
  const counts = [22, 23, 0, 21]
  assert.deepEqual(countsOf(start(adapter, counts).seats), [0, 22, 21, 23])
  counts[0] = 99
  assert.equal(adapter.snapshot.seats[1].duplicate_remaining_tile_count, 22)
  assert.deepEqual(countsOf(start(adapter, [1, 2, 3, 4], [0, 1, 2, 3]).seats), [1, 2, 3, 4])
  assert.deepEqual(countsOf(start(adapter, undefined).seats), [undefined, undefined, undefined, undefined])
  assert.deepEqual(countsOf(action(adapter, undefined)[0].seat_status), [undefined, undefined, undefined, undefined])
  // Normal rooms cannot accidentally display a stray count array.
  assert.deepEqual(countsOf(start(adapter, [1, 2, 3, 4], originals, false).seats), [undefined, undefined, undefined, undefined])
  assert.deepEqual(countsOf(action(adapter, [1, 2, 3, 4])[0].seat_status), [undefined, undefined, undefined, undefined])
})

test('普通、补花、补杠和合并动作均使用服务器余牌，不根据摸牌自行扣减或依赖牌值', () => {
  const adapter = new SalasasaGameAdapter(102)
  start(adapter, [22, 23, 21, 23])
  for (const draw of ['deal_tile', 'deal_buhua_tile', 'deal_gang_tile']) {
    const events = action(adapter, [7, 8, 9, 0], {
      action_list: [draw], deal_tiles: [0, 0],
    })
    assert.equal(events.length, 2)
    for (const event of events) {
      assert.deepEqual(countsOf(event.seat_status), [9, 7, 0, 8])
      assert.equal(event.event.tile, 0)
      assert.ok(event.seat_status.every((seat) => !('hand_tiles' in seat) && !('duplicate_walls' in seat)))
    }
  }
  assert.deepEqual(countsOf(action(adapter, undefined)[0].seat_status), [9, 7, 0, 8])
  const combined = action(adapter, [1, 2, 0, 4], {
    action_list: ['buhua', 'deal_buhua_tile'], action_player: 0, buhua_tile: 51,
  })
  assert.ok(combined.every((event) => countsOf(event.seat_status).join(',') === '0,1,4,2'))
})

test('终局使用权威计数，损坏的计数字段不会显示虚构的余牌', () => {
  const adapter = new SalasasaGameAdapter(102)
  start(adapter, [20, 20, 20, 20])
  const ended = adapter.accept({
    type: 'gamestate/guobiao/game_end',
    game_end_info: { player_final_data: {}, duplicate_remaining_tiles: [0, 8, 6, 4] },
  })
  assert.equal(ended.event.state.ended, true)
  assert.deepEqual(countsOf(ended.event.seat_status), [6, 0, 4, 8])
  for (const invalid of [[1, 2, 3], [1, -1, 3, 4], [1, 2.5, 3, 4], [1, '2', 3, 4], [1, NaN, 3, 4]]) {
    assert.deepEqual(countsOf(start(adapter, invalid).seats), [undefined, undefined, undefined, undefined])
  }
})

test('实时场景按观看座位把四个计数送到对应玩家面板，普通场景事件清空', () => {
  const adapter = new SalasasaGameAdapter(102)
  start(adapter, [22, 23, 21, 23])
  const scene = new MahjongScene(() => {})
  scene.mounted = true
  scene.selfDir = 2
  scene.playSound = () => {}
  scene.applyTileCoverPalette = () => {}
  scene.countdown = { stop() {}, visible: false }
  scene.waitDisplay = { setData() {}, reset() {} }
  scene.stateDisplay = { setScore() {}, setCurrent() {}, setRound() {}, setRemaining() {} }
  scene.hands = Array.from({ length: 4 }, () => ({ unwaitDiscard() {}, rightList: [], leftList: [] }))
  const rendered = []
  scene.rivers = Array.from({ length: 4 }, (_, local) => ({
    setPlayerInfo(_rank, _name, _offline, count) { rendered[local] = count },
  }))
  scene.handleEvent(action(adapter, [10, 20, 0, 30], { action_list: [] })[0])
  assert.deepEqual(rendered, [30, 20, 0, 10])
  scene.applyCenterScores()
  assert.deepEqual(rendered, [30, 20, 0, 10])
  start(adapter, undefined)
  scene.handleEvent(action(adapter, undefined, { action_list: [] })[0])
  assert.deepEqual(rendered, [undefined, undefined, undefined, undefined])
})

test('复式回放按四份牌山、补摸和原始座位恢复余牌，普通牌谱不显示', () => {
  const record = {
    game_id: 'duplicate-replay', created_at: '', rule: 'guobiao', players: [],
    record: { game_round: { 0: {
      seats: [1, 3, 0, 2], start_player_index: 0,
      duplicate_walls: [[11, 12], [21], [31, 32, 33], [41, 42]],
      action_ticks: [['d', 31], ['c', 31], ['bd', 11, 1], ['gd', 12]],
    } } },
  }
  const replay = new RecordReplay(record)
  assert.deepEqual(countsOf(replay.build(0, 0).snapshot.seats), [3, 2, 2, 1])
  assert.deepEqual(countsOf(replay.build(0, 4).snapshot.seats), [2, 0, 2, 1])
  assert.deepEqual(countsOf(replay.eventForStep(0, 3).seat_status), [2, 0, 2, 1])
  delete record.record.game_round[0].duplicate_walls
  assert.deepEqual(countsOf(new RecordReplay(record).build(0, 4).snapshot.seats), [undefined, undefined, undefined, undefined])
})

test('复式回放读取牌谱版本，v2 尾部补牌高亮与剩余牌山一致且空山不自行终止回放', () => {
  const record = { game_id: 'v2', created_at: '', rule: 'guobiao', players: [], record: {
    game_title: { duplicate_rules_version: 2 }, game_round: { 0: {
      seats: [0, 1, 2, 3], duplicate_walls: [[11, 12, 13], [21], [], []],
      action_ticks: [['bd', 12, 0], ['gd', 13], ['d', 11], ['c', 11]],
    } },
  } }
  const replay = new RecordReplay(record)
  assert.deepEqual(replay.remainingWallAt(0, 1), [0x41, 0x43, 0x61])
  assert.deepEqual(replay.wallViewAt(0, 1).map(tile => tile.consumed), [false, true, false, false])
  assert.deepEqual(countsOf(replay.build(0, 3).snapshot.seats), [0, 1, 0, 0])
  assert.equal(replay.build(0, 3).snapshot.state.ended, undefined)
  delete record.record.game_title.duplicate_rules_version
  assert.deepEqual(new RecordReplay(record).remainingWallAt(0, 1), [0x42, 0x43, 0x61])
})
