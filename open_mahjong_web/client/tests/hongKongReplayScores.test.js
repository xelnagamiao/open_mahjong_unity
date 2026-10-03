import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { fileURLToPath } from 'node:url'

const compiled = await build({
  entryPoints: [fileURLToPath(new URL('../src/game2d/replay/recordReplay.ts', import.meta.url))],
  bundle: true, platform: 'node', format: 'esm', write: false, logLevel: 'silent',
})
const { RecordReplay } = await import(`data:text/javascript;base64,${Buffer.from(compiled.outputFiles[0].text).toString('base64')}`)

function detail(startingScore) {
  return {
    game_id: 'remix', rule: 'hongkong', players: [],
    record: {
      game_title: { rule: 'hongkong', sub_rule: 'hongkong/qingzhang_lianhuise',
        ...(startingScore == null ? {} : { starting_score: startingScore }) },
      game_round: {
        round_index_1: { seats: [0, 1, 2, 3], action_ticks: [['hu_self', 0, 30, [], [90, -30, -30, -30]], ['end']] },
        round_index_2: { seats: [3, 0, 1, 2], action_ticks: [] },
      },
    },
  }
}

test('新版香港魔改牌谱从明确的 1200 起分，换庄与回退保持固定玩家的得分', () => {
  const replay = new RecordReplay(detail(1200))
  for (const [round, node, expected] of [
    [0, 0, [1200, 1200, 1200, 1200]],
    [0, 2, [1290, 1170, 1170, 1170]],
    [1, 0, [1170, 1170, 1170, 1290]],
    [0, 0, [1200, 1200, 1200, 1200]],
  ]) {
    assert.deepEqual(replay.build(round, node, 0).snapshot.seats.map(p => p.score), expected)
  }
})

test('旧香港牌谱继续使用实际起分，不按新规则补加 1200', () => {
  const original = detail()
  original.players = [90, -30, -30, -30].map((score, original_player_index) => ({ score, original_player_index }))
  assert.deepEqual(new RecordReplay(original).build(0, 0, 0).snapshot.seats.map(p => p.score), [0, 0, 0, 0])
  assert.deepEqual(new RecordReplay(detail(0)).build(0, 0, 0).snapshot.seats.map(p => p.score), [0, 0, 0, 0])
})
