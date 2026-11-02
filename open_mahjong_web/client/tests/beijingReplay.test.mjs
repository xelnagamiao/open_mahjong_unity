import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { fileURLToPath } from 'node:url'
import { beijingInfoAt, applyBeijingAction, parseBeijingFan } from '../src/utils/beijingReplay.js'

const output = await build({ entryPoints: [fileURLToPath(new URL('../src/game2d/replay/recordReplay.ts', import.meta.url))], bundle: true, platform: 'node', format: 'esm', write: false, logLevel: 'silent' })
const { RecordReplay, isRecordSilentTick } = await import('data:text/javascript;base64,' + Buffer.from(output.outputFiles[0].text).toString('base64'))
const mm = t => t < 20 ? 0x40 + t % 10 : t < 30 ? 0x60 + t % 10 : t < 40 ? 0xc0 + t % 10 : 0xa0 + t % 10
function replay(ticks, finalScores, extra = {}) {
  const round = { seats: [2, 3, 0, 1], tiles_list: [24, 25, 26, 27], start_player_index: 0,
    p0_tiles: [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 42, 42, 41],
    p1_tiles: [11, 12, 13, 21, 22, 23, 31, 32, 33, 42, 42, 42, 41],
    p2_tiles: [14, 15, 16, 24, 25, 26, 34, 35, 36, 43, 43, 43, 41], p3_tiles: [],
    beijing: { indicator: 45, hun: 47, repeat: 0, tian_ting: [false, false, false, false], start_scores: [0, 0, 0, 0] },
    action_ticks: ticks, ...extra }
  return new RecordReplay({ game_id: 'beijing-replay-test', rule: 'beijing', players: [0, 1, 2, 3].map(i => ({ user_id: 100 + i, username: String(i), original_player_index: i, score: finalScores[round.seats[i]], rank: 1 })), record: { game_title: { rule: 'beijing', sub_rule: 'beijing/chipengti', starting_score: 0 }, game_round: { round_index_1: round } } })
}

test('dingzhuang payments are visible immediately and absolute snapshots do not double-count', () => {
  const changes = [-6, 2, 2, 2]
  const ticks = [['beijing', 'state', {}, [0, 0, 0, 0]], ['beijing', 'payment', { kind: 'dingzhuang', changes }], ['beijing', 'state', { ledger: { changes } }, changes], ['end']]
  const r = replay(ticks, changes)
  for (const node of [4, 0, 2, 3, 2, 4]) assert.deepEqual(r.build(0, node).snapshot.seats.map(p => p.score), node < 2 ? [0, 0, 0, 0] : changes)
})

test('multiple ron winners receive the display tile and river is consumed once', () => {
  const changes = [-8, 4, 4, 0]
  const ticks = [['reset', 0], ['c', 41, true], ['beijing', 'win_source', 1, 0, 'discard', 41, {}], ['hu_first', 1, 4, ['BJ|1|翻|门清'], [-4, 4, 0, 0], 41], ['beijing', 'win_source', 2, 0, 'discard', 41, {}], ['hu_second', 2, 4, ['BJ|1|翻|门清'], [-4, 0, 4, 0], 41], ['beijing', 'state', { ledger: { changes } }, changes], ['end']]
  const r = replay(ticks, changes)
  for (const node of [8, 2, 4, 6, 0, 8]) {
    const state = r.build(0, node).snapshot
    if (node === 2) assert.deepEqual(state.seats[0].discard_pile, [mm(41)])
    if (node >= 4) { assert.deepEqual(state.seats[0].discard_pile, []); assert.equal(state.seats[1].drawn_tile, mm(41)) }
    if (node >= 6) assert.equal(state.seats[2].drawn_tile, mm(41))
    if (node === 8) assert.deepEqual(state.seats.map(p => p.score), changes)
  }
})

test('added-kong declaration does not consume a tile and rob removes only the fourth tile', () => {
  const states = [{ hand: [11, 12, 41], drawn: 19, river: [21], riverDrawn: [false], melds: [{ type: 'triplet', tile: mm(41) }] }]
  const original = structuredClone(states)
  assert.deepEqual(applyBeijingAction(states, { action: 'declare_kan', player: 0, tile: 41 }), { actor: 0, robTile: 41 })
  assert.deepEqual(states, original)
  applyBeijingAction(states, { action: 'rob_kan', player: 0, tile: 41 })
  assert.deepEqual(states[0].hand, [11, 12, 19])
  assert.equal(states[0].drawn, null)
  assert.deepEqual(states[0].river, [21])
  assert.equal(states[0].melds[0].type, 'triplet')
})

test('concealed kongs keep all four faces hidden until final settlement and survive rewind', () => {
  const hand = [11, 11, 11, 11, 21, 22, 23, 31, 32, 33, 41, 41, 42, 42]
  const ticks = [['reset', 0], ['ag', 11], ['gd', 27], ['beijing', 'state', { ledger: { changes: [0, 0, 0, 0] } }, [0, 0, 0, 0]], ['end']]
  const r = replay(ticks, [0, 0, 0, 0], { p0_tiles: hand })
  for (const node of [3, 5, 2, 5]) {
    const state = r.build(0, node).snapshot.seats[0]
    assert.deepEqual(state.melds[0].concealed_face_down, Array(4).fill(node < 4))
    assert.equal(state.hand_tiles.length + Number(state.drawn_tile != null), node >= 3 ? 11 : 10)
  }
})

test('fan labels, tian-ting actions and state metadata preserve source data', () => {
  assert.deepEqual(parseBeijingFan('BJ|-2|翻|混儿减半'), { name: '混儿减半', value: '-2翻' })
  assert.equal(parseBeijingFan('素'), null)
  assert.equal(isRecordSilentTick(['beijing', 'action', { action: 'tianting' }]), false)
  assert.equal(isRecordSilentTick(['beijing', 'waits', 1, {}]), true)
  const round = { beijing: { indicator: 45, hun: 47 }, action_ticks: [['beijing', 'state', { indicator: 41, hun: 42 }]] }
  assert.equal(beijingInfoAt(round, 1).hun, 42)
  assert.equal(beijingInfoAt(round, 0).hun, 47)
})

test('replay marks the active hun and restores the correct face when seeking backwards', () => {
  const r = replay([['beijing', 'state', { indicator: 46, hun: 45 }, [0, 0, 0, 0]], ['end']], [0, 0, 0, 0])
  for (const [node, hun] of [[0, 47], [1, 45], [2, 45], [0, 47]]) {
    assert.deepEqual(r.build(0, node).snapshot.hand_joker_tiles, [mm(hun)])
  }
})
