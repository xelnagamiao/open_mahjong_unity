import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { hongzhongInfoAt, hongzhongKongEventsAt, hongzhongHintsAt, hongzhongTileName, hongzhongSubstitutionName, parseHongzhongFan } from '../src/utils/hongzhongReplay.js'
const compiled = await build({ entryPoints: [new URL('../src/game2d/replay/recordReplay.ts', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')], bundle: true, platform: 'node', format: 'esm', write: false, logLevel: 'silent' })
const { RecordReplay, isRecordSilentTick } = await import(`data:text/javascript;base64,${Buffer.from(compiled.outputFiles[0].text).toString('base64')}`)
const round = (ticks, wall = [11, 12, 13, 14, 15, 16]) => ({ seats: [0, 1, 2, 3], start_player_index: 0, tiles_list: wall, p0_tiles: [11, 11, 11, 11, 45], p1_tiles: [], p2_tiles: [], p3_tiles: [], action_ticks: ticks })
function fixture(rounds, final = [0, 0, 0, 0]) {
  return { game_id: 'hz-replay', rule: 'hongzhong', players: final.map((score, i) => ({ score, original_player_index: i, user_id: i + 100, username: String(i) })), record: { game_title: { rule: 'hongzhong', sub_rule: 'hongzhong/mil2024' }, game_round: Object.fromEntries(rounds.map((r, i) => [`round_index_${i+1}`, { ...r, round_index: i+1 }])) } }
}

test('末墩上再下补杠、前取鸟在逐步跳转和牌墙染色一致', () => {
  const r = new RecordReplay(fixture([round([['gd', 15], ['gd', 16], ['hongzhong', 'birds', { tiles: [11, 12], hits: 1, detail: {} }]])]))
  for (const node of [0, 3, 1, 2, 3, 0]) {
    const expected = [6, 5, 4, 2][node]
    assert.equal(r.remainingWallAt(0, node).length, expected)
    assert.equal(r.wallViewAt(0, node).filter(t => !t.consumed).length, expected)
    assert.equal(r.build(0, node).snapshot.state.remaining_tile_count, expected)
  }
  assert.deepEqual(r.wallViewAt(0, 1).map(t => t.consumed), [false, false, false, false, true, false])
  assert.deepEqual(r.remainingWallAt(0, 3), [0x43, 0x44])
})

test('扎鸟不足两张只扣真实张数，实体红中一直保持红中', () => {
  for (const birds of [[], [45], [45, 15]]) {
    const r = new RecordReplay(fixture([round([['hongzhong', 'birds', { tiles: birds, hits: birds.length }]], birds)]))
    assert.deepEqual(r.remainingWallAt(0, 1), [])
    assert.equal(r.build(0, 1).snapshot.seats[0].flower_tiles.length, 0)
    assert.equal(r.build(0, 1).snapshot.seats[0].drawn_tile, 0xa5)
  }
})

test('暗杠仅两端扣放，事件动画与快照相符，其他规则不受影响', () => {
  const r = new RecordReplay(fixture([round([['ag', 11]])]))
  assert.deepEqual(r.build(0, 1).snapshot.seats[0].melds[0].concealed_face_down, [true, false, false, true])
  assert.deepEqual(r.eventForStep(0, 0).event.concealed_face_down, [true, false, false, true])
  const old = fixture([round([['ag', 11]])]); old.rule = old.record.game_title.rule = 'guobiao'
  assert.equal(new RecordReplay(old).build(0, 1).snapshot.seats[0].melds[0].concealed_face_down, undefined)
})

test('即时杠分、流局退还、含鸟和牌分只计一次；跨局换位分数正确', () => {
  const first = round([['hongzhong', 'kong_score', [6, -2, -2, -2], 'concealed'], ['hongzhong', 'kong_refund', [-6, 2, 2, 2]], ['liuju']])
  const second = { ...round([['hongzhong', 'kong_score', [3, -3, 0, 0], 'direct'], ['hongzhong', 'birds', { tiles: [45, 15], hits: 2, detail: {} }], ['hu_self', 0, 2, [], [18, -6, -6, -6], 45]]), seats: [1, 2, 3, 0] }
  const r = new RecordReplay(fixture([first, second], [-6, -6, -6, 18].map((x, i) => x + [-3, 0, 0, 3][i])))
  assert.deepEqual(r.roundScoreChangesByOriginal(0), [0, 0, 0, 0])
  assert.deepEqual(r.roundScoreChangesByOriginal(1), [-9, -6, -6, 21])
  assert.deepEqual(r.build(0, 1).snapshot.seats.map(s => s.score), [6, -2, -2, -2])
  assert.deepEqual(r.build(0, 3).snapshot.seats.map(s => s.score), [0, 0, 0, 0])
  assert.deepEqual(r.build(1, 3).snapshot.seats.map(s => s.score), [21, -9, -6, -6])
  assert.equal(isRecordSilentTick(['hongzhong', 'birds']), true)
  assert.equal(r.eventForStep(1, 1), null)
})

test('旧手牌暗杠及加杠立即收起其他摸牌槽，补牌后只显示新的摸牌', () => {
  const concealed = { ...round([['ag', 11, 'F'], ['hongzhong', 'kong_score', [6, -2, -2, -2], 'concealed'], ['gd', 15]], [15, 16]),
    p0_tiles: [11, 11, 11, 11, 12, 13, 14, 21, 22, 23, 34, 35, 36, 39] }
  const added = { ...round([['c', 39, 'T'], ['d', 11], ['c', 11, 'T'], ['p', 11, 0], ['c', 28, 'F'], ['reset', 0], ['d', 39], ['jg', 11, 'F'], ['hongzhong', 'kong_score', [0, 0, 0, 0], 'added'], ['gd', 15]], [11, 39, 15, 16]),
    p0_tiles: [11, 11, 11, 12, 13, 14, 21, 22, 23, 28, 34, 35, 36, 39] }
  for (const [header, kongNode] of [[concealed, 1], [added, 8]]) {
    const r = new RecordReplay(fixture([header]))
    for (const node of [kongNode, kongNode + 1, kongNode + 2, kongNode]) {
      const seat = r.build(0, node).snapshot.seats[0]
      assert.equal(seat.has_drawn_tile, node === kongNode + 2)
      assert.equal(seat.drawn_tile, node === kongNode + 2 ? 0x45 : null)
      assert.equal(seat.hand_tiles.filter(tile => tile === 0xc9).length, 1)
      assert.equal(seat.melds[0].type, 'kong')
    }
  }
})

test('解释元数据只按回放节点显示，回退不会泄露未来结算', () => {
  const info = { tiles: [45], hits: 1, detail: { logical_hand: [11, 11], winning_logical_tile: 11, joker_substitutions: [[45, 11]] } }
  const current = round([['hongzhong', 'kong_score', [3, -1, -1, -1]], ['hongzhong', 'birds', info]])
  assert.equal(hongzhongInfoAt(current, 1), null)
  assert.deepEqual(hongzhongInfoAt(current, 2), info)
  assert.equal(hongzhongInfoAt(current, 0), null)
  assert.equal(hongzhongInfoAt(null, 10), null)
  assert.equal(hongzhongInfoAt(round([['hongzhong', 'birds', []]]), 1), null)
  assert.deepEqual(parseHongzhongFan('HZ|seven_pairs|2|七对子'), { name: '七对子', value: '2番' })
  assert.equal(parseHongzhongFan('七对子'), null)
  assert.equal(parseHongzhongFan(), null)
  assert.equal(parseHongzhongFan('HZ|invalid|NaN|七对子'), null)
  assert.deepEqual([45, 11, 29, 35, 0].map(hongzhongTileName), ['红中', '1万', '9筒', '5条', '0'])
})

test('流局杠账说明逐事件保留、回退不残留且不重复计分', () => {
  const current = round([['hongzhong', 'kong_score', [6, -2, -2, -2], 'concealed'], ['hongzhong', 'kong_refund', [-6, 2, 2, 2]], ['hongzhong', 'kong_score', []]])
  assert.deepEqual(hongzhongKongEventsAt(current, 0), [])
  assert.deepEqual(hongzhongKongEventsAt(current, 1), [{ kind: '暗杠', changes: [6, -2, -2, -2] }])
  assert.equal(hongzhongKongEventsAt(current, 3).at(-1).kind, '流局退杠')
  assert.deepEqual(hongzhongKongEventsAt(null, 1), [])
  assert.equal(hongzhongKongEventsAt(round([['hongzhong', 'kong_score', [0, 0, 0, 0], 'old-kind']]), 1)[0].kind, '杠分')
})

test('红中替代三元组不把手牌序号当牌值，保留旧二元组与对象说明', () => {
  assert.equal(hongzhongSubstitutionName([0, 45, 11]), '红中→1万')
  // Index 11 is also a legal tile id: it must not become an extra 1万.
  assert.equal(hongzhongSubstitutionName([11, 45, 29]), '红中→9筒')
  assert.equal(hongzhongSubstitutionName([13, 45, 35]), '红中→5条')
  assert.equal(hongzhongSubstitutionName([45, 11]), '红中→1万')
  assert.equal(hongzhongSubstitutionName({ physical_tile: 45, logical_tile: 12 }), '红中→2万')
  assert.equal(hongzhongSubstitutionName({ physical: 45, logical: 23 }), '红中→3筒')
  assert.equal(hongzhongSubstitutionName({ tile: 34 }), '红中→4条')
  assert.equal(hongzhongSubstitutionName(19), '红中→9万')
  for (const empty of [null, undefined, [], [45], [0, 1, 45, 11]]) {
    assert.equal(hongzhongSubstitutionName(empty), '')
  }
})

test('权威听牌按四席、实体红中与副露精确匹配；打牌和回退时旧提示失效', () => {
  const a = { source_hand_tiles: [11, 11, 11, 11, 45], source_melds: [], waiting_tiles: [12, 45], waiting_by_discard: { 45: [11] } }
  const b = { source_hand_tiles: [45], source_melds: ['G11'], waiting_tiles: [45], waiting_by_discard: {} }
  const current = round([['hongzhong', 'hints', 0, a], ['ag', 11], ['hongzhong', 'hints', 0, b], ['c', 45, 'T'], ['hongzhong', 'hints', 1, { ...a, source_hand_tiles: [] }]])
  const r = new RecordReplay(fixture([current, round([])]))
  assert.equal(r.hongzhongHintsAt(0, 0, 0), null)
  assert.deepEqual(r.hongzhongHintsAt(0, 1, 0), a)
  assert.equal(r.hongzhongHintsAt(0, 2, 0), null)
  assert.deepEqual(r.hongzhongHintsAt(0, 3, 0), b)
  assert.equal(r.hongzhongHintsAt(0, 4, 0), null)
  assert.deepEqual(r.hongzhongHintsAt(0, 1, 0), a)
  assert.equal(r.hongzhongHintsAt(0, 3, 1), null)
  assert.equal(r.hongzhongHintsAt(1, 0, 0), null)
  assert.equal(r.hongzhongHintsAt(0, 1, 4), null)
  assert.equal(hongzhongHintsAt(current, 3, 0, [45], ['g11']), null)
  assert.equal(hongzhongHintsAt(current, 1, 0, [11, 11, 11, 11, 11], []), null)
  assert.equal(hongzhongHintsAt(current, 1, 0, [45, 11, 11, 11, 11], []), a)
  assert.equal(hongzhongHintsAt(null, 10, 0, [], []), null)
  assert.equal(hongzhongHintsAt(current, 3, -1, [], []), null)
  assert.equal(hongzhongHintsAt(current, 3, 0, null, []), null)
  const other = fixture([current]); other.rule = other.record.game_title.rule = 'guobiao'
  assert.equal(new RecordReplay(other).hongzhongHintsAt(0, 1, 0), null)
})
