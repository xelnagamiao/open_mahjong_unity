import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { changchunInfoAt, changchunWallAt, applyChangchunPhysical } from '../src/utils/changchunReplay.js'
const compiled = await build({ entryPoints: [new URL('../src/game2d/replay/recordReplay.ts', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')], bundle: true, platform: 'node', format: 'esm', write: false, logLevel: 'silent' })
const { RecordReplay } = await import(`data:text/javascript;base64,${Buffer.from(compiled.outputFiles[0].text).toString('base64')}`)
const cc = data => ['cc', data]
const round = ticks => ({ round_index: 1, current_round: 1, seats: [0, 1, 2, 3], start_player_index: 0, p0_tiles: [41, 42, 31, 11, 12, 13, 22, 23, 24, 35, 36, 37, 45, 45], p1_tiles: [], p2_tiles: [], p3_tiles: [], tiles_list: Array.from({ length: 28 }, (_, i) => 11 + i % 9), action_ticks: ticks })
const fixture = (r, scores = [0, 0, 0, 0]) => ({ game_id: 'cc-test', rule: 'changchun', players: scores.map((score, i) => ({ score, original_player_index: i, user_id: i + 100, username: String(i) })), record: { game_title: { rule: 'changchun', sub_rule: 'changchun/mil2024' }, game_round: { round_index_1: r } } })

test('passed final tiles remain separate and private until the round reveal, including rewind', () => {
  const r = round([cc({ kind: 'tail_pass', player: 0, tile: 11 }), cc({ kind: 'tail_pass', player: 1, tile: 22 }), cc({ kind: 'round_reveal', tail_tiles: [{ player: 0, tile: 11 }, { player: 1, tile: 22 }] })])
  assert.deepEqual(changchunInfoAt(r, 2, 0).tailTiles, [{ player: 0, tile: 11 }, { player: 1, tile: 0 }])
  assert.deepEqual(changchunInfoAt(r, 2, 1).tailTiles, [{ player: 0, tile: 0 }, { player: 1, tile: 22 }])
  assert.deepEqual(changchunInfoAt(r, 3, 2).tailTiles, [{ player: 0, tile: 11 }, { player: 1, tile: 22 }])
  assert.deepEqual(changchunInfoAt(r, 1, 2).tailTiles, [{ player: 0, tile: 0 }])
  assert.deepEqual(changchunInfoAt(r, 0, 0).tailTiles, [])
})

test('bao visibility follows seat and revision, including reverse seeking', () => {
  const r = round([cc({ kind: 'bao_reveal', player: 0, revision: 1, slot: 26, tile: 19 }), cc({ kind: 'bao_seen', player: 1, revision: 1 }), cc({ kind: 'bao_change', player: 0, revision: 2, slot: 24, tile: 17 }), cc({ kind: 'round_reveal', bao_tile: 17 })])
  for (const [node, seat, tile] of [[1, 0, 19], [1, 1, null], [2, 1, 19], [3, 1, null], [4, 3, 17], [0, 0, null], [1, 0, 19]]) assert.equal(changchunInfoAt(r, node, seat).tile, tile)
})

test('wall restores old indicator, consumes exact front/tail slots and final four', () => {
  const r = round([cc({ kind: 'bao_reveal', player: 0, revision: 1, slot: 26, tile: 19 }), ['gd', 19], cc({ kind: 'bao_change', player: 0, revision: 2, slot: 24, tile: 17 }), cc({ kind: 'bao_exhausted' }), ...Array.from({ length: 13 }, () => ['d', 11])])
  const consumed = n => changchunWallAt(r, n).wall.map((t, i) => t.consumed ? i : -1).filter(i => i >= 0)
  assert.deepEqual(consumed(1), [26]); assert.deepEqual(consumed(3), [24, 27]); assert.equal(changchunWallAt(r, 4).remaining.length, 27)
  const replay = new RecordReplay(fixture(r))
  for (const node of [0, 4, 1, 3, 2, 17, 0]) {
    const expected = changchunWallAt(r, node)
    assert.equal(replay.remainingWallAt(0, node).length, expected.remaining.length)
    assert.equal(replay.wallViewAt(0, node).filter(t => !t.consumed).length, expected.remaining.length)
    assert.equal(replay.build(0, node).snapshot.state.remaining_tile_count, expected.playable)
  }
})

test('special physical/logical identities, robbery, scoring and rewind', () => {
  const code = 'Cwind:41,42,31:41,42,43'
  const r = round([cc({ kind: 'special', player: 0, code, physical: [41, 42, 31] }), cc({ kind: 'kong_score', player: 0, delta: { 0: 3, 1: -1, 2: -1, 3: -1 } }), ['d', 31], cc({ kind: 'added_offer', player: 0, tile: 31 }), cc({ kind: 'added_robbed', player: 0, tile: 31, special: true }), cc({ kind: 'rob_claim', player: 0, tile: 31 })])
  const replay = new RecordReplay(fixture(r, [3, -1, -1, -1])), s = replay.build(0, 1).snapshot.seats[0]
  assert.equal(s.hand_tile_count + Number(s.has_drawn_tile), 11)
  assert.equal(s.melds[0].physical_tiles.length, 3)
  assert.notDeepEqual(s.melds[0].physical_tiles, s.melds[0].logical_tiles)
  assert.equal(replay.build(0, 6).snapshot.seats[0].discard_pile.length, 1)
  assert.equal(replay.build(0, 5).snapshot.seats[0].melds[0].physical_tiles.length, 3)
  assert.deepEqual(replay.build(0, 2).snapshot.seats.map(s => s.score), [3, -1, -1, -1])
  assert.deepEqual(replay.build(0, 0).snapshot.seats.map(s => s.score), [0, 0, 0, 0])
})

test('ordinary robbery reverts matching kong; indicator win, reveal and tail pass', () => {
  const states = Array.from({ length: 4 }, () => ({ hand: [11], drawn: 31, river: [], riverDrawn: [], melds: [] }))
  states[0].melds = [{ type: 'kong', tile: 11, meld_from_rel: 5 }, { type: 'kong', tile: 22, meld_from_rel: 6 }, { type: 'kong', tile: 33, concealed: true }]
  const apply = e => applyChangchunPhysical(states, e, t => t)
  apply({ kind: 'added_robbed', player: 0, tile: 22, special: false })
  assert.deepEqual(states[0].melds.map(m => m.type), ['kong', 'triplet', 'kong'])
  apply({ kind: 'settlement', source: 'bao_indicator', winner: 2, tile: 19 }); assert.equal(states[2].drawn, 19)
  apply({ kind: 'round_reveal' }); assert.deepEqual(states[0].melds[2].concealed_face_down, [false, false, false, false])
  apply({ kind: 'tail_pass', player: 0, tile: 31 }); assert.equal(states[0].drawn, null); assert.deepEqual(states[0].river, [])
  apply({ kind: 'added_commit', player: 0, position: 0, code: 'Cwind:41,42,31,31:41,42,43,44' })
  assert.deepEqual(states[0].melds[0].physical_tiles, [41, 42, 31, 31])
})

test('active delayed spectators retain wall counts without secret tile identities', () => {
  const r = round([cc({ kind: 'bao_reveal', player: 0, revision: 1, slot: 26, tile: 0 }), ['d', 18], cc({ kind: 'bao_change', player: 0, revision: 2, slot: 24, tile: 0 }), ['gd', 17], cc({ kind: 'bao_exhausted' })])
  r.tiles_list.fill(0)
  const replay = new RecordReplay(fixture(r))
  for (const [node, count] of [[0, 28], [1, 27], [2, 26], [3, 26], [4, 25], [5, 26]]) {
    assert.equal(replay.remainingWallAt(0, node).length, count)
    assert.equal(replay.build(0, node).snapshot.state.remaining_tile_count, Math.max(0, count - 14 - count % 2))
    assert.ok(!changchunInfoAt(r, node, 0).tile)
  }
})
