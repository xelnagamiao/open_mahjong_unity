import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { analyzeRecords } from '../src/utils/recordAnalyzer.js'
import { guangdongInfoAt, isGuangdongMilRecord, parseGuangdongFan } from '../src/utils/guangdongReplay.js'
const compiled = await build({ entryPoints: [new URL('../src/game2d/replay/recordReplay.ts', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')], bundle: true, platform: 'node', format: 'esm', write: false, logLevel: 'silent' })
const { RecordReplay, isRecordSilentTick } = await import(`data:text/javascript;base64,${Buffer.from(compiled.outputFiles[0].text).toString('base64')}`)
function fixture(ticks, { sub_rule = 'guangdong/mil2023', wall = [11, 12, 13, 55, 56, 47], hands = [[55, 11, 11, 11, 11], [12], [], []], final = [0, 0, 0, 0] } = {}) {
  return { game_id: 'guangdong-replay', rule: 'guangdong', players: final.map((score, i) => ({ score, original_player_index: i, user_id: i + 100, username: String(i) })), record: { game_title: { rule: 'guangdong', sub_rule }, game_round: { round_index_1: { seats: [0, 1, 2, 3], tiles_list: wall, start_player_index: 0, ...Object.fromEntries(hands.map((hand, i) => [`p${i}_tiles`, hand])), action_ticks: ticks } } } }
}
const tiles = (r, node, seat = 0) => { const s = r.build(0, node).snapshot.seats[seat]; return [...s.hand_tiles, ...(s.drawn_tile == null ? [] : [s.drawn_tile])] }

test('花鬼留手及打鬼不转入花牌区；所有metadata可安全跳过', () => {
  const r = new RecordReplay(fixture([['c', 55, 'F'], ['guangdong', 'ghost_discard', 0, 1], ['guangdong', 'tips', 1, { waits: [{ tile: 55 }] }]]))
  assert.equal(tiles(r, 0).includes(0xe5), true)
  assert.equal(r.build(0, 3).snapshot.seats[0].flower_tiles.length, 0)
  assert.deepEqual(r.build(0, 3).snapshot.seats[0].discard_pile, [0xe5])
  assert.equal(isRecordSilentTick(['guangdong', 'tips']), true)
})

test('花鬼版无死墙，尾补与前翻四马在逐步、跳转及墙染色一致', () => {
  const r = new RecordReplay(fixture([['gd', 47], ['guangdong', 'horses', { tiles: [11, 12, 13, 55], hits: [1, 1, 1, 0], changes: [0, 0, 0, 0] }]]))
  for (const node of [0, 2, 1, 2, 0]) {
    const expected = [6, 5, 1][node]
    assert.equal(r.remainingWallAt(0, node).length, expected)
    assert.equal(r.wallViewAt(0, node).filter(t => !t.consumed).length, expected)
    assert.equal(r.build(0, node).snapshot.state.remaining_tile_count, expected)
  }
  const old = new RecordReplay(fixture([], { sub_rule: undefined, wall: Array(20).fill(11) }))
  // Explicit legacy title (without sub_rule) keeps the existing 13-tile dead wall.
  delete old.detail.record.game_title.sub_rule
  assert.equal(old.build(0, 0).snapshot.state.remaining_tile_count, 7)
})

test('不足四马只扣实际翻开张数，鬼马不改变物理花鬼身份', () => {
  const r = new RecordReplay(fixture([['guangdong', 'horses', { tiles: [55, 12], hits: [0, 1, 0, 0], changes: [2, -2, 0, 0] }]], { wall: [55, 12] }))
  assert.deepEqual(r.remainingWallAt(0, 1), [])
  assert.deepEqual(r.wallViewAt(0, 1).map(t => t.consumed), [true, true])
})

test('杠分即时、流局退杠；马与settlement不重复计分', () => {
  const ticks = [['guangdong', 'kong_score', [3, -1, -1, -1], 'concealed', null, 11], ['guangdong', 'refund_kongs', [-3, 1, 1, 1]], ['liuju']]
  const r = new RecordReplay(fixture(ticks))
  assert.deepEqual(r.roundScoreChangesByOriginal(0), [0, 0, 0, 0])
  assert.deepEqual(r.build(0, 1).snapshot.seats.map(s => s.score), [3, -1, -1, -1])
  assert.deepEqual(r.build(0, 3).snapshot.seats.map(s => s.score), [0, 0, 0, 0])
  const win = new RecordReplay(fixture([['guangdong', 'horses', { tiles: [], changes: [3, -1, -1, -1] }], ['guangdong', 'settlement', { win_changes: [15, -5, -5, -5] }], ['hu_self', 0, 2, [], [15, -5, -5, -5], 55]], { final: [15, -5, -5, -5] }))
  assert.deepEqual(win.roundScoreChangesByOriginal(0), [15, -5, -5, -5])
})

test('抢加杠回放恢复原碰，点和收走牌河', () => {
  const r = new RecordReplay(fixture([['reset', 1], ['c', 11, 'F'], ['p', 11, 0], ['d', 11], ['jg', 11], ['hu_first', 1, 2, ['GD|rob_kong|1|抢杠'], [-12, 12, 0, 0], 11]]))
  assert.equal(r.build(0, 5).snapshot.seats[0].melds[0].type, 'kong')
  assert.equal(r.build(0, 6).snapshot.seats[0].melds[0].type, 'triplet')
  assert.equal(tiles(r, 6, 1).includes(0x41), true)
  const ron = new RecordReplay(fixture([['c', 11, 'F'], ['hu_first', 1, 2, [], [-4, 4, 0, 0], 11]]))
  assert.equal(ron.build(0, 2).snapshot.seats[0].discard_pile.length, 0)
})

test('解释性元数据保持真实马序和tips快照，回退不残留', () => {
  const payload = { hand: [55, 12], waits: [{ tile: 11, fan: 2, score: 4, ron: true, self_draw: true }] }
  const round = { action_ticks: [['guangdong', 'tips', 2, payload], ['guangdong', 'ghost_discard', 2, 1], ['guangdong', 'horses', { tiles: [55, 12], hits: [0, 1, 0, 0] }], ['guangdong', 'settlement', { coefficient: 2 }]] }
  assert.equal(guangdongInfoAt(round, 4).ghost_discard_counts[2], 1)
  assert.deepEqual(guangdongInfoAt(round, 4).horses.tiles, [55, 12])
  assert.equal(guangdongInfoAt(round, 1).horses, null)
  assert.deepEqual(guangdongInfoAt(round, 4).tips[2], payload)
  assert.equal(isGuangdongMilRecord(fixture([])), true)
  assert.equal(parseGuangdongFan('GD|all_pungs|2|碰碰和').value, '2番')
})

test('广东暗杠先公示牌种，回放中间两张正面、两侧背面', () => {
  const r = new RecordReplay(fixture([['ag', 11, 'F']]))
  const meld = r.build(0, 1).snapshot.seats[0].melds[0]
  assert.equal(meld.tile, 0x41)
  assert.deepEqual(meld.concealed_face_down, [true, false, false, true])
})

test('双打双响只收走一张河牌，后续摸牌跳过和家，盘末逐笔结算', () => {
  const ticks = [
    ['c', 11, 'F'], ['hu_first', 1, 2, ['GD|chicken|0|鸡和'], [0,0,0,0], 11, 1, 0, 0],
    ['hu_second', 2, 8, [], [0,0,0,0], 11, 1, 0, 1], ['d', 41],
    ['guangdong', 'penalty', [0,0,0,-32], 3, 'false_win'],
    ['blood', 'reveal_hu', {0:[12],1:[13,11],2:[14,11],3:[15,41]}],
    ['blood', 'settle_hu', 'hu_first', 1, 2, ['鸡和'], [-2,2,0,0], 0, []],
    ['blood', 'settle_hu', 'hu_second', 2, 8, [], [-8,0,8,0], 1, []],
  ]
  const replay = new RecordReplay(fixture(ticks, { sub_rule:'guangdong/mil2023-doubles',
    hands:[[12,11],[13],[14],[15]], wall:[41], final:[-10,2,8,-32] }))
  for (const node of [8,2,3,4,6,7,8]) {
    const snapshot = replay.build(0,node).snapshot
    assert.equal(snapshot.seats[0].discard_pile.length, node === 2 ? 1 : 0)
    if (node < 6) assert.deepEqual(snapshot.seats[1].flower_tiles, [0x41])
    else assert.deepEqual(snapshot.seats[1].flower_tiles, [])
    if (node === 4) assert.equal(snapshot.seats[3].has_drawn_tile, true)
    if (node === 6) assert.deepEqual(snapshot.seats[1].hand_tiles, [0x43,0x41])
    if (node === 7) assert.deepEqual(snapshot.seats.map(s => s.score), [-2,2,0,-32])
  }
  assert.deepEqual(replay.roundScoreChangesByOriginal(0), [-10,2,8,-32])
})

test('双打加杠保留原碰牌人，杠分和延迟和牌分各入账一次', () => {
  const ticks = [
    ['c', 45, 'F'], ['p', 45, 2, 45, 45], ['c', 14, 'F'],
    ['hu_first', 3, 4, [], [0, 0, 0, 0], 14, 0, 2, 1],
    ['d', 12], ['c', 12, 'T'], ['d', 13], ['c', 13, 'T'],
    ['d', 45], ['jg', 45, 'T'], ['gd', 21], ['c', 21, 'T'],
    ['hu_first', 0, 8, [], [0, 0, 0, 0], 21, 0, 2, 1],
    ['guangdong', 'kong_score', [-1, -1, 2, 0], 'board', null, null],
    ['blood', 'settle_hu', 'hu_first', 3, 4, [], [0, 0, -4, 4], 0, []],
    ['blood', 'settle_hu', 'hu_first', 0, 8, [], [8, 0, -8, 0], 1, []], ['end'],
  ]
  const detail = fixture(ticks, { sub_rule: 'guangdong/mil2023-doubles',
    hands: [[45, 22], [15], [45, 45, 14], [11]], wall: [12, 13, 45, 21] })
  const round = detail.record.game_round.round_index_1
  round.seats = [2, 3, 0, 1]
  const finalByOriginal = [-10, 4, 7, -1]
  for (const [i, player] of detail.players.entries()) {
    detail.record.game_title[`p${i}_uid`] = player.user_id
    player.score = finalByOriginal[i]
  }
  const replay = new RecordReplay(detail)
  for (let view = 0; view < 4; view++) {
    assert.equal(replay.eventForStep(0, 9, view).event.actor_seat, 2)
    const melds = node => replay.build(0, node, view).snapshot.seats[2].melds.map(m => m.type)
    assert.deepEqual([melds(10), melds(9), melds(10)], [['kong'], ['triplet'], ['kong']])
    assert.deepEqual(replay.build(0, 10, view).snapshot.seats.map(s => s.score), [0, 0, 0, 0])
    assert.deepEqual(replay.build(0, 14, view).snapshot.seats.map(s => s.score), [-1, -1, 2, 0])
    assert.equal(analyzeRecords([detail.record], detail.players[view].user_id).total_round_score, finalByOriginal[view])
  }
  assert.deepEqual(replay.roundScoreChangesByOriginal(0), finalByOriginal)
})

test('战鸣申请只作表现，跳转不重复移牌；最终碰只认走一次', () => {
  const r=new RecordReplay(fixture([['c',11,'F'],['ca',2,'g',11],['p',11,1]],{hands:[[11,12,13],[11,11,22],[],[]]}))
  const before=r.build(0,1).snapshot
  const afterClaim=r.build(0,2).snapshot
  assert.deepEqual(afterClaim.seats.map(s=>s.hand_tiles),before.seats.map(s=>s.hand_tiles))
  assert.deepEqual(afterClaim.seats.map(s=>s.melds),before.seats.map(s=>s.melds))
  assert.deepEqual(afterClaim.seats[0].discard_pile,before.seats[0].discard_pile)
  for(const node of [3,1,2,3]) {
    const result=r.build(0,node).snapshot
    assert.equal(result.seats[1].melds.length,node===3?1:0)
    assert.equal(result.seats[0].discard_pile.length,node===3?0:1)
  }
})
