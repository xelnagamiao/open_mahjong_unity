import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import {
  isSichuanBloodBattle, isSichuanBloodFlow,
} from '../src/utils/sichuanReplay.js'

const compiled = await build({ entryPoints: [new URL('../src/game2d/replay/recordReplay.ts', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')],
  bundle: true, platform: 'node', format: 'esm', write: false, logLevel: 'silent' })
const { RecordReplay } = await import(`data:text/javascript;base64,${Buffer.from(compiled.outputFiles[0].text).toString('base64')}`)
const round = (wall, ticks) => ({ tiles_list: wall, action_ticks: ticks })
const mm = tile => ({ 1: 0x40, 2: 0x60, 3: 0xc0 })[Math.floor(tile / 10)] + tile % 10
function detail(data, title = {}, rule = 'sichuan') {
  return { game_id: 'sichuan-contract', created_at: '', rule, players: [], record: {
    game_title: { rule, sub_rule: 'sichuan/standard', ...title }, game_round: { round_index_1: {
      seats: [0, 1, 2, 3], start_player_index: 0, ...data,
    } },
  } }
}
test('all Sichuan subrules supplement from the front through exhaustion and reverse seeks', () => {
  for (const sub_rule of ['sichuan/standard', 'sichuan/xueliu', 'sichuan/xueliu_exchange']) {
    const d = detail(round([11, 22, 33, 19], [['gd', 11], ['d', 22], ['gd', 33], ['gd', 19]]), { sub_rule })
    const before = structuredClone(d), r = new RecordReplay(d)
    for (const node of [0, 1, 2, 3, 4, 2, 1, 0, 4]) {
      const wall = [11, 22, 33, 19].slice(node)
      assert.deepEqual(r.remainingWallAt(0, node), wall.map(mm))
      assert.deepEqual(r.wallViewAt(0, node).filter(t => !t.consumed).map(t => t.tile), wall.map(mm))
      assert.deepEqual(r.wallViewAt(0, node).map(t => t.consumed), [0, 1, 2, 3].map(index => index < node))
      assert.equal(r.build(0, node).snapshot.state.remaining_tile_count, wall.length)
    }
    assert.deepEqual(d, before)
  }
})

test('appended draws, another round and title-only family identity preserve front positions', () => {
  const data = round([11, 22, 11], [['gd', 11]])
  const d = detail(data), r = new RecordReplay(d)
  assert.deepEqual(r.wallViewAt(0, 1).map(t => t.consumed), [true, false, false])
  data.action_ticks.push(['d', 22])
  assert.deepEqual(r.wallViewAt(0, 1).map(t => t.consumed), [true, false, false])
  assert.deepEqual(r.remainingWallAt(0, 2), [mm(11)])
  d.record.game_round.round_index_2 = round([33, 11, 22], [['gd', 33]])
  d.rule = ''
  const both = new RecordReplay(d)
  assert.deepEqual(both.remainingWallAt(1, 1), [mm(11), mm(22)])
  assert.deepEqual(both.remainingWallAt(0, 1), [mm(22), mm(11)])
  assert.deepEqual(new RecordReplay(detail(round([], []))).remainingWallAt(0, 0), [])
})

test('existing duplicate v2 wall retains its own physical layout', () => {
  const d = detail({ ...round([39], [['gd', 12], ['d', 11]]), duplicate_walls: [[11, 12, 13], [], [], []] },
    { duplicate_key: 'duplicate-wall-isolation-test', duplicate_rules_version: 2 }, 'guobiao')
  const r = new RecordReplay(d)
  assert.deepEqual(r.wallViewAt(0, 1).map(t => t.consumed), [false, true, false])
  assert.deepEqual(r.remainingWallAt(0, 1), [mm(11), mm(13)])
  assert.deepEqual(r.remainingWallAt(0, 2), [mm(13)])
})

test('other rules retain their existing replacement direction', () => {
  for (const rule of ['guobiao', 'riichi', 'hongzhong']) {
    const r = new RecordReplay(detail(round([11, 22, 33, 19], [['gd', 33], ['gd', 19]]), {}, rule))
    assert.deepEqual(r.remainingWallAt(0, 1), [11, 22, 19].map(mm))
    assert.deepEqual(r.remainingWallAt(0, 2), [11, 22].map(mm))
  }
  for (const rule of ['guangdong', 'hangzhou', 'yixing', 'wenzhou']) {
    const r = new RecordReplay(detail(round([11, 22, 33, 19], [['gd', 19]]), {}, rule))
    assert.deepEqual(r.remainingWallAt(0, 1), [11, 22, 33].map(mm))
  }
})

function hu(kind, winner, tile = 24, multi = 1, source = 0, recycle = 0, fan = []) {
  return [kind, winner, 1, fan, [0, 0, 0, 0], tile, multi, source, recycle]
}
const multiRound = () => ({ ...round([17, 28], [
  ['c', 24, 'F'], hu('hu_first', 2), hu('hu_second', 3, 24, 1, 0, 1),
  ['d', 17], ['c', 17, 'T'], ['d', 28],
]), p0_tiles: [11, 24], p1_tiles: [21], p2_tiles: [22], p3_tiles: [23] })

test('same discard stays until final winner, then last winner next active seat draws', () => {
  const r = new RecordReplay(detail(multiRound()))
  assert.deepEqual(r.build(0, 2).snapshot.seats[0].discard_pile, [mm(24)])
  assert.deepEqual(r.build(0, 3).snapshot.seats[0].discard_pile, [])
  assert.equal(r.build(0, 3).snapshot.state.current_player, 0)
  assert.equal(r.build(0, 4).snapshot.seats[0].drawn_tile, mm(17))
  assert.equal(r.eventForStep(0, 3).event.actor_seat, 0)
  assert.equal(r.build(0, 5).snapshot.state.current_player, 1)
  assert.equal(r.build(0, 6).snapshot.seats[1].drawn_tile, mm(28))
  assert.deepEqual(r.xunmuNodes(0, 0), [0, 4])
  assert.deepEqual(r.xunmuNodes(0, 2), [0])
})

test('blood battle skips prior winners, but blood-flow and disabled retirement retain every player', () => {
  const data = { ...round([11, 22], [hu('hu_self', 1), ['reset', 0], ['c', 24, 'F'], ['d', 11]]),
    p0_tiles: [24, 31], p1_tiles: [13, 14] }
  const battle = new RecordReplay(detail(data))
  assert.equal(battle.build(0, 4).snapshot.seats[2].drawn_tile, mm(11))
  for (const title of [{ blood_battle: false }, { blood_battle: 'false' }, { sub_rule: 'sichuan/xueliu' }, { sub_rule: 'sichuan/xueliu_exchange' }]) {
    const d = detail(data, title), r = new RecordReplay(d)
    assert.equal(isSichuanBloodBattle(d), false)
    assert.equal(r.build(0, 4).snapshot.seats[1].drawn_tile, mm(11))
  }
})

test('blood-flow self-win returns to fixed waiting hand and places won tile in public win area', () => {
  const d = detail({ ...round([17], [hu('hu_self', 0, 24, 0, -1), ['reset', 0], ['d', 17]]), p0_tiles: [11, 24] }, { sub_rule: 'sichuan/xueliu' })
  assert.equal(isSichuanBloodFlow(d), true)
  const r = new RecordReplay(d), seat = r.build(0, 3).snapshot.seats[0]
  assert.deepEqual(seat.hand_tiles, [mm(11)])
  assert.equal(seat.drawn_tile, mm(17))
  assert.deepEqual(seat.flower_tiles, [mm(24)])
})

test('explicit source seat owns the final river; mismatching tile and robbed kong never pop an unrelated river', () => {
  const d = detail({ ...round([], [
    ['reset', 1], ['c', 24, 'F'], ['reset', 0], ['c', 33, 'F'], hu('hu_first', 2, 24, 0, 1, 1),
  ]), p0_tiles: [33], p1_tiles: [24], p2_tiles: [22] })
  const seats = new RecordReplay(d).build(0, 5).snapshot.seats
  assert.deepEqual(seats[1].discard_pile, []); assert.deepEqual(seats[0].discard_pile, [mm(33)])
  d.record.game_round.round_index_1.action_ticks[4][5] = 29
  assert.deepEqual(new RecordReplay(d).build(0, 5).snapshot.seats[1].discard_pile, [mm(24)])
  const rob = detail({ ...round([], [
    ['c', 11, 'F'], ['p', 11, 1, 11, 11], ['c', 23, 'F'], ['reset', 1], ['d', 11], ['jg', 11, 'T'],
    hu('hu_first', 2, 11, 1, 1, 0, ['抢杠']), hu('hu_second', 3, 11, 1, 1, 1, ['抢杠']),
  ]), p0_tiles: [11], p1_tiles: [11, 11, 23] })
  const r = new RecordReplay(rob)
  for (const n of [7, 8]) {
    const source = r.build(0, n).snapshot.seats[1]
    assert.equal(source.melds[0].type, 'triplet')
    assert.deepEqual(source.discard_pile, [mm(23)])
  }
})

test('ordinary point-win defaults recycling, false win and other rule turn order remain isolated', () => {
  const data = { ...round([11], [['c', 24, 'F'], ['hu_first', 2, 1, [], [0, 0, 0, 0], 24], ['d', 11]]), p0_tiles: [24] }
  assert.deepEqual(new RecordReplay(detail(data)).build(0, 2).snapshot.seats[0].discard_pile, [])
  assert.equal(new RecordReplay(detail(data)).build(0, 3).snapshot.seats[3].drawn_tile, mm(11))
  data.action_ticks[1][3] = ['错和']
  assert.equal(new RecordReplay(detail(data)).build(0, 3).snapshot.seats[1].drawn_tile, mm(11))
  assert.deepEqual(new RecordReplay(detail(data)).build(0, 2).snapshot.seats[0].discard_pile, [mm(24)])
  const other = new RecordReplay(detail(multiRound(), {}, 'guobiao'))
  assert.equal(other.build(0, 4).snapshot.seats[1].drawn_tile, mm(17))
})
