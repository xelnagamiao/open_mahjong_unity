import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { fileURLToPath } from 'node:url'
import { shouldPlayResultGong } from '../src/game2d/lib/resultGong.js'

const bundle = await build({
  stdin: {
    contents: "export * from './replay/recordReplay'",
    resolveDir: fileURLToPath(new URL('../src/game2d', import.meta.url)),
    loader: 'ts',
  },
  bundle: true, write: false, platform: 'node', format: 'esm',
})
const { RecordReplay } = await import('data:text/javascript;base64,' + Buffer.from(bundle.outputFiles[0].text).toString('base64'))

test('Qingque gong uses the precise total and is strictly above 18.5 fan', () => {
  for (const rule of ['qingque', 'qingque/standard']) {
    for (const fans of [[], ['门前清', '一般高'], ['十三幺']]) {
      for (const [fan, expected] of [[1, false], [18.49999, false], [18.5, false], [18.50001, true], [32, true]]) {
        assert.equal(shouldPlayResultGong(fans, fan, rule), expected, `${rule}: ${fan}`)
      }
    }
  }
  assert.equal(shouldPlayResultGong(['清一色', '混幺九'], 64, 'guobiao'), true)
  assert.equal(shouldPlayResultGong(['花牌*1'], 64, 'guobiao'), false)
})

test('Qingque concealed kongs show back-face-face-back in replay seeks and steps', () => {
  for (const explicitIds of [false, true]) {
    for (const actor of [0, 1, 2, 3]) {
      const tick = explicitIds ? ['ag', 35, false, 35, 35, 35, 35] : ['ag', 35, false]
      const round = {
        seats: [0, 1, 2, 3], start_player_index: actor,
        p0_tiles: [], p1_tiles: [], p2_tiles: [], p3_tiles: [],
        [`p${actor}_tiles`]: [11, 12, 13, 35, 35, 35, 35],
        action_ticks: [tick],
      }
      const replay = new RecordReplay({
        game_id: 'qingque-presentation-test', created_at: '', rule: 'qingque',
        players: [0, 1, 2, 3].map(i => ({ user_id: 101 + i, username: `P${i}`, score: 0, rank: i + 1, original_player_index: i })),
        record: { game_title: { rule: 'qingque', sub_rule: 'qingque/standard' }, game_round: { round_index_1: round } },
      })
      for (const node of [1, 0, 1]) {
        const melds = replay.build(0, node).snapshot.seats[actor].melds
        assert.equal(melds.length, node)
        if (node) assert.deepEqual(melds[0].concealed_face_down, [true, false, false, true])
      }
      assert.deepEqual(replay.eventForStep(0, 0).event.concealed_face_down, [true, false, false, true])
    }
  }
})
