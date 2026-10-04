import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
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
