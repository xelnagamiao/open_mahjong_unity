import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { fileURLToPath } from 'node:url'
import { recordHandJokers, jokerFirstSortKey } from '../src/game2d/lib/handJokers.js'
import { mmcrSettlementSortKey, splitSettlementHand } from '../src/game2d/lib/settlementHand.js'

const compiled = await build({ entryPoints: [fileURLToPath(new URL('../src/game2d/replay/recordReplay.ts', import.meta.url))], bundle: true, platform: 'node', format: 'esm', write: false, logLevel: 'silent' })
const { RecordReplay } = await import('data:text/javascript;base64,' + Buffer.from(compiled.outputFiles[0].text).toString('base64'))
const mm = tile => tile < 20 ? 0x40 + tile % 10 : tile < 30 ? 0x60 + tile % 10 : tile < 40 ? 0xc0 + tile % 10 : tile < 50 ? 0xa0 + tile % 10 : 0xe0 + tile % 10
const cases = [
  ['hangzhou', 'hangzhou/mil2025', {}, [46]],
  ['hongzhong', 'hongzhong/mil2024', {}, [45]],
  ['nanchang', 'nanchang/mil2026', { nanchang: { indicator: 47, secondary: 46 } }, [47, 46]],
  ['wenzhou', 'wenzhou/mil2024', { wenzhou: { caishen: 28, white_natural: 28 } }, [28]],
  ['wuhan', 'wuhan/mil2025', { action_ticks: [['wuhan', 'indicator', 38]] }, [39]],
  ['guangdong', 'guangdong/mil2023', {}, [55, 56, 57, 58]],
]
function fixture(rule, sub_rule, extra = {}) {
  const round = { seats: [0,1,2,3], start_player_index: 0, current_round: 1, tiles_list: [11,12,13,14,15,16], p0_tiles: [11,28,39,45,46,47,55,56,57,58], p1_tiles: [], p2_tiles: [], p3_tiles: [], action_ticks: [], ...extra }
  return { round, detail: { game_id: 'joker-sort', rule, sub_rule, players: [0,1,2,3].map(i => ({ user_id: i+100, username: String(i), original_player_index: i, score: 0 })), record: { game_title: {rule, sub_rule}, game_round: {round_index_1: round} } } }
}

for (const [rule, sub, extra, expected] of cases) test(`${rule}: physical jokers reach replay snapshots and sort left`, () => {
  const { round, detail } = fixture(rule, sub, extra)
  assert.deepEqual(recordHandJokers(detail, round, 1), expected)
  const replay = new RecordReplay(detail)
  const snapshot = replay.build(0, 1).snapshot
  assert.deepEqual(snapshot.hand_joker_tiles, expected.map(mm))
  const hand = snapshot.seats[0].hand_tiles
  const original = [...hand]
  const key = tile => jokerFirstSortKey(tile, snapshot.hand_joker_tiles, mmcrSettlementSortKey)
  const sorted = [...hand].sort((a, b) => key(a) - key(b))
  const jokerCount = hand.filter(t => expected.map(mm).includes(t)).length
  assert.ok(sorted.slice(0, jokerCount).every(t => expected.map(mm).includes(t)))
  assert.ok(sorted.slice(jokerCount).every(t => !expected.map(mm).includes(t)))
  assert.deepEqual(hand, original, 'server/replay hand identity and order remain intact')
  assert.deepEqual(sorted.slice(jokerCount), hand.filter(t => !expected.map(mm).includes(t)).sort((a,b) => mmcrSettlementSortKey(a)-mmcrSettlementSortKey(b)))
  assert.deepEqual(replay.initialHandsAt(0, 1)[0], sorted, 'initial-hand overview uses the current physical jokers too')
})

test('dynamic metadata follows backward and forward seeks without future leakage', () => {
  const {round, detail} = fixture('nanchang', 'nanchang/mil2026', {nanchang: {indicator: 41, secondary: 42}, action_ticks: [['nanchang','state',{indicator:47,secondary:46}]]})
  const replay = new RecordReplay(detail)
  for (const node of [0,1,0,1,0]) assert.deepEqual(replay.build(0,node).snapshot.hand_joker_tiles, (node ? [47,46] : [41,42]).map(mm))
  assert.deepEqual(round.nanchang, {indicator:41,secondary:42})
})

test('Wuhan reveal wraps suits and honors and does not promote skins', () => {
  for (const [indicator,joker] of [[19,11],[29,21],[39,31],[41,42],[44,47],[45,47],[47,46],[46,41]]) {
    const {round,detail} = fixture('wuhan', 'wuhan/mil2025', {action_ticks:[['wuhan','indicator',indicator],['wuhan','state',JSON.stringify({joker,skins:[45]})]]})
    assert.deepEqual(recordHandJokers(detail,round,0), [])
    assert.deepEqual(recordHandJokers(detail,round,1), [joker])
    assert.deepEqual(recordHandJokers(detail,round,2), [joker])
  }
})

test('Wuhan initial-hand overview updates on reveal and rewind without changing record data', () => {
  const {round, detail} = fixture('wuhan', 'wuhan/mil2025', {action_ticks: [['wuhan', 'indicator', 38]]})
  const original = [...round.p0_tiles]
  const replay = new RecordReplay(detail)
  for (const node of [0, 1, 0, 1, 0]) {
    const hand = replay.initialHandsAt(0, node)[0]
    if (node) assert.equal(hand[0], mm(39))
    else assert.deepEqual(hand, original.map(mm))
    assert.deepEqual(round.p0_tiles, original)
  }
})

test('white natural substitution, Tuidao, ordinary rules, missing data and room switches', () => {
  for (const rule of ['guobiao','riichi','changchun','shenyang','guizhou','shanghai','nanjing','guangdong','wenzhou','nanchang','wuhan']) {
    const {round,detail} = fixture(rule, rule === 'guangdong' ? 'guangdong/tuidao' : rule)
    assert.deepEqual(recordHandJokers(detail,round,0), [])
  }
  const {round,detail} = fixture('wenzhou','wenzhou/mil2024',{wenzhou:{caishen:28,white_natural:28}})
  assert.deepEqual(recordHandJokers(detail,round,0),[28])
  round.wenzhou.caishen=46
  assert.deepEqual(recordHandJokers(detail,round,0),[46])
  detail.record.game_title.rule='guobiao'
  assert.deepEqual(recordHandJokers(detail,round,0),[])
})

test('joker winning tile remains in the separate winning slot; duplicates stay in main row', () => {
  const joker=mm(46)
  const tiles=[11,12,13,21,22,23,31,32,33,41,41,46,46,46].map(mm)
  const before=[...tiles]
  const result=splitSettlementHand(tiles,joker,0,t=>jokerFirstSortKey(t,[joker],mmcrSettlementSortKey))
  assert.deepEqual(result.slice(0,2),[joker,joker])
  assert.equal(result.length,13)
  assert.equal(result.filter(t=>t===joker).length,2)
  assert.deepEqual(tiles,before)
})
