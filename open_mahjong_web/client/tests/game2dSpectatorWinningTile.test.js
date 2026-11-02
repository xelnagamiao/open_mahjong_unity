import test from 'node:test'
import assert from 'node:assert/strict'
import { Assets, Container, Texture } from 'pixi.js'
import { RecordReplay } from '../src/game2d/replay/recordReplay.ts'
import { SalasasaGameAdapter, salasasaTileToMmcr } from '../src/game2d/salasasa/gameAdapter.ts'
import { Hand } from '../src/game2d/game/scene/Hand.ts'
import { Tile } from '../src/game2d/game/scene/Tile.ts'
import { MahjongScene } from '../src/game2d/game/scene/MahjongScene.ts'

const waiting = [11, 12, 13, 14, 15, 16, 21, 22, 23, 31, 32, 33, 17]
const sorted = tiles => [...tiles].sort((a, b) => a - b)
const held = seat => [...seat.hand_tiles, ...(seat.drawn_tile == null ? [] : [seat.drawn_tile])]
const win = (kind, seat) => [kind, seat, 8, ['门前清'], [0, 0, 0, 0], 17]
function replay(ticks, hands = [[24, 25, 26, 27, 28, 29, 34, 35, 36, 37, 38, 39, 41, 17], waiting, [], []], rule = 'guobiao') {
  return new RecordReplay({ game_id: 'spectator-hand-regression', rule,
    players: [0, 1, 2, 3].map(i => ({ user_id: 100 + i, original_player_index: i, score: 0, username: `p${i}` })),
    record: { game_title: { rule, sub_rule: `${rule}/standard` }, game_round: { round_index_1: {
      seats: [0, 1, 2, 3], start_player_index: 0, tiles_list: Array(80).fill(19),
      ...Object.fromEntries(hands.map((hand, i) => [`p${i}_tiles`, hand])), action_ticks: ticks,
    } } } })
}

for (const view of [0, 1, 2, 3]) {
  test(`国标观战点和七万：视角 ${view} 的牌桌与事件保留 13 张原手牌及真实和牌张`, () => {
    const r = replay([['reset', 0], ['c', 17, 'F'], win('hu_first', 1)])
    const before = r.build(0, 2, view).snapshot.seats[1]
    const after = r.build(0, 3, view).snapshot.seats[1]
    const event = r.eventForStep(0, 2, view).event
    assert.equal(event.tile, salasasaTileToMmcr(17), 'the win event must carry the recorded 7m')
    assert.deepEqual(sorted(held(before)), sorted(waiting.map(salasasaTileToMmcr)))
    assert.deepEqual(sorted(held(after)), sorted([...waiting, 17].map(salasasaTileToMmcr)))
    assert.equal(after.drawn_tile, salasasaTileToMmcr(17))
    assert.deepEqual(sorted(event.revealed_hand_tiles), sorted(held(after)))
    r.build(0, 0, (view + 1) % 4)
    assert.deepEqual(r.build(0, 3, view).snapshot.seats[1], after, 'rewind and perspective changes must not consume another tile')
  })
}

test('国标连续补花后自摸七万，花牌只出现于花区，一万不会重复进入手牌', () => {
  const hand = waiting.slice(1).concat(51)
  const r = replay([['reset', 0], ['c', 17, 'F'], ['d', 11], ['bh', 51, 1, 'F'],
    ['bd', 58, 1], ['bh', 58, 1, 'T'], ['bd', 17, 1], win('hu_self', 1)],
  [[], hand, [], []])
  const expected = [...hand]
  for (const [node, remove, add] of [[3, null, 11], [4, 51, null], [5, null, 58], [6, 58, null], [7, null, 17]]) {
    if (remove != null) expected.splice(expected.indexOf(remove), 1)
    if (add != null) expected.push(add)
    assert.deepEqual(sorted(held(r.build(0, node).snapshot.seats[1])), sorted(expected.map(salasasaTileToMmcr)))
  }
  const event = r.eventForStep(0, 7).event
  assert.equal(event.tile, salasasaTileToMmcr(17))
  assert.equal(event.revealed_hand_tiles.length, 14)
  assert.equal(event.revealed_hand_tiles.filter(tile => tile === salasasaTileToMmcr(11)).length, 1)
  assert.deepEqual(r.build(0, 8).snapshot.seats[1].flower_tiles, [51, 58].map(salasasaTileToMmcr))
})

test('补花后的点和不依赖暗手张数推测和牌张，重复结算不重复加牌', () => {
  const r = replay([['reset', 0], ['c', 17, 'F'], win('hu_first', 1), win('hu_first', 1)],
    [[], [12, 14, 16], [], []])
  for (const node of [3, 4, 2, 3]) {
    assert.deepEqual(sorted(held(r.build(0, node).snapshot.seats[1])),
      sorted((node === 2 ? [12, 14, 16] : [12, 14, 16, 17]).map(salasasaTileToMmcr)))
  }
})

test('旧牌谱缺少和牌张字段时，从刚发生的弃牌恢复七万', () => {
  const tick = win('hu_first', 1).slice(0, 5)
  const r = replay([['c', 17, 'F'], tick])
  assert.equal(r.eventForStep(0, 1).event.tile, salasasaTileToMmcr(17))
  assert.equal(r.build(0, 2).snapshot.seats[1].drawn_tile, salasasaTileToMmcr(17))
})

function start(view = 0) {
  return { type: 'gamestate/guobiao/game_start', game_info: {
    room_id: 1, gamestate_id: 'spectator-win', room_rule: 'guobiao', sub_rule: 'guobiao/standard',
    current_round: 1, current_player_index: 0, action_tick: 10, tile_count: 60,
    players_info: [0, 1, 2, 3].map(i => ({ user_id: 100 + i, username: `p${i}`,
      player_index: i, original_player_index: i, score: 0, hand_tiles_count: 13,
      hand_tiles: i === view ? waiting : undefined,
      discard_tiles: i === 0 ? [17] : [], combination_tiles: [], combination_mask: [], huapai_list: [] })),
  } }
}

const result = { hepai_player_index: 1, hu_class: 'hu_first', hu_score: 8,
  hu_fan: ['门前清'], hepai_player_hand: [...waiting, 17], hepai_player_huapai: [51, 58] }

for (const view of [0, 1, 2, 3]) {
  test(`国标实时消息：视角 ${view} 在弃牌后加入，结算仍取服务器手牌末项七万`, () => {
    const adapter = new SalasasaGameAdapter(100 + view)
    adapter.accept(start(view))
    const update = adapter.accept({ type: 'gamestate/guobiao/show_result', show_result_info: result })
    assert.equal(update.event.event.tile, salasasaTileToMmcr(17))
    assert.equal(update.result.hepai_tile, 17)
    assert.deepEqual(update.event.event.revealed_hand_tiles, result.hepai_player_hand.map(salasasaTileToMmcr))
  })
}

test('国标重连结算：直接恢复的面板也带上权威和牌张', () => {
  const adapter = new SalasasaGameAdapter(101)
  const update = adapter.accept({ ...start(1), settlement_restore: [
    { type: 'gamestate/guobiao/show_result', show_result_info: result },
  ] })
  assert.equal(update.restored, true)
  assert.equal(update.event, undefined)
  assert.equal(update.result.hepai_tile, 17)
})

test('国标结算权威七万不会被上一局遗留的一万弃牌覆盖', () => {
  const adapter = new SalasasaGameAdapter(100)
  adapter.accept(start())
  adapter.accept({ type: 'gamestate/guobiao/do_action', do_action_info: {
    action_list: ['cut'], action_player: 0, action_tick: 11, cut_tile: 11, cut_class: false,
  } })
  adapter.accept(start())
  const update = adapter.accept({ type: 'gamestate/guobiao/show_result', show_result_info: result })
  assert.equal(update.event.event.tile, salasasaTileToMmcr(17))
})

for (const view of [0, 1, 2, 3]) {
  test(`国标真实牌桌对象：视角 ${view} 点和后为 13 张暗手加七万，重复展示不增牌`, t => {
    // Keep actual Pixi tiles, hand ownership, win dispatch and display layout;
    // replace only elapsed-time movement and unrelated sound/countdown UI.
    for (let tile = 1; tile <= 49; tile++) Assets.cache.set(`regular-${tile}`, Texture.WHITE)
    t.mock.method(Tile.prototype, 'generalMove', function (parent, x, y, rotation) {
      parent.addChild(this); this.position.set(x, y); this.rotation = rotation
      return Promise.resolve()
    })
    const noop = () => {}
    const scene = new MahjongScene(noop)
    scene.mounted = true; scene.selfDir = view
    scene.playSound = noop; scene.playCallSound = noop
    scene.countdown = { stop: noop }
    scene.waitDisplay = { reset: noop, setData: noop }
    scene.stateDisplay = { setScore: noop, setCurrent: noop, setRound: noop, setRemaining: noop }
    scene.rivers = [0, 1, 2, 3].map(() => ({ tileList: [], flowerList: [], setPlayerInfo: noop }))
    scene.hands = [0, 1, 2, 3].map(direction => new Hand(direction, new Container()))
    const direction = (1 - view + 4) % 4
    const hand = scene.hands[direction]
    hand.revealHand(waiting.map(salasasaTileToMmcr))
    const r = replay([['reset', 0], ['c', 17, 'F'], win('hu_first', 1)])
    const payload = r.eventForStep(0, 2, view)
    payload.event.silent = true
    const live = new SalasasaGameAdapter(100 + view)
    live.accept(start(view))
    const livePayload = live.accept({ type: 'gamestate/guobiao/show_result', show_result_info: { ...result, silent: true } }).event
    for (const transition of [payload, payload, livePayload, livePayload]) {
      scene.handleEvent(transition)
      assert.equal(hand.drawnTile?.tid, salasasaTileToMmcr(17))
      assert.deepEqual(sorted(hand.rightList.map(tile => tile.tid)), sorted(waiting.map(salasasaTileToMmcr)))
      assert.equal(hand.children.filter(child => child instanceof Tile).length, 14)
      assert.ok(hand.children.filter(child => child instanceof Tile).every(tile => tile.shown))
    }
    scene.clearPendingChoicesTimeout(); scene.clearAutoActionTimeout()
    for (const row of scene.hands) row.destroy({ children: true })
  })
}

test('古典和牌张的扩展字段不能误读为国标字段', () => {
  const r = replay([['c', 17, 'F'], ['hu_first', 1, 8, [], 0, 3, [0, 0, 0, 0], 17]], undefined, 'classical')
  assert.equal(r.eventForStep(0, 1).event.tile, salasasaTileToMmcr(17))
  assert.equal(r.build(0, 2).snapshot.seats[1].drawn_tile, salasasaTileToMmcr(17))
})

test('立直和牌的番种字段不会被误当成牌值，点和仍使用刚发生的弃牌', () => {
  const r = replay([['c', 17, 'F'], ['hu_riichi', 1, 'hu_first', 30, 1, ['平和'], [0, 0, 0, 0]]], undefined, 'riichi')
  assert.equal(r.eventForStep(0, 1).event.tile, salasasaTileToMmcr(17))
  assert.equal(held(r.build(0, 2).snapshot.seats[1]).length, 14)
})

test('旧实时结果没有揭示手牌时保留弃牌兜底，自摸和明确和牌张保持不变', () => {
  for (const [extra, expected] of [
    [{ hepai_player_hand: undefined }, 11],
    [{ hu_class: 'hu_self' }, 17],
    [{ hepai_player_hand: undefined, hepai_tile: 17 }, 17],
  ]) {
    const adapter = new SalasasaGameAdapter(100)
    adapter.accept(start())
    adapter.accept({ type: 'gamestate/guobiao/do_action', do_action_info: {
      action_list: ['cut'], action_player: 0, action_tick: 11, cut_tile: 11, cut_class: false,
    } })
    const update = adapter.accept({ type: 'gamestate/guobiao/show_result', show_result_info: { ...result, ...extra } })
    assert.equal(update.result.hepai_tile, expected)
    assert.equal(update.event.event.tile, salasasaTileToMmcr(expected))
  }
})

test('错和及独立花胡不会给普通暗手追加七万或花牌', () => {
  for (const tick of [
    ['hu_first', 1, 0, ['错和'], [0, 0, 0, 0], 17],
    ['hu_first', 1, 8, ['花胡'], [0, 0, 0, 0], 58],
  ]) {
    const r = replay([['reset', 0], ['c', 17, 'F'], tick])
    assert.deepEqual(sorted(held(r.build(0, 3).snapshot.seats[1])), sorted(waiting.map(salasasaTileToMmcr)))
  }
})
