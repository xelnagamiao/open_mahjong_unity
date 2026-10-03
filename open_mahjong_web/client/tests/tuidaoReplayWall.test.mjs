import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'

// Bundle TypeScript with the project's Vite compiler, without a browser or DOM.
const compiled = await build({
  entryPoints: [new URL('../src/game2d/replay/recordReplay.ts', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')],
  bundle: true, platform: 'node', format: 'esm', write: false, logLevel: 'silent',
})
const { RecordReplay } = await import(`data:text/javascript;base64,${Buffer.from(compiled.outputFiles[0].text).toString('base64')}`)

function replay(rule, ticks, titleRule = rule, wall = [11, 12, 13, 41, 46]) {
  return new RecordReplay({
    game_id: 'wall-contract', created_at: '', rule,
    players: [0, 1, 2, 3].map(i => ({ user_id: i + 100, username: String(i), score: 0, rank: 1, original_player_index: i })),
    record: { game_title: { rule: titleRule }, game_round: { round_index_1: {
      seats: [0, 1, 2, 3], tiles_list: wall, action_ticks: ticks,
    } } },
  })
}

test('MIL推倒和补牌始终取最后一张，余牌和牌墙染色一致', () => {
  const r = replay('guangdong', [['gd', 46], ['bd', 41]])
  for (const node of [1, 2, 0, 1, 2]) {
    const expected = node === 0 ? [0x41, 0x42, 0x43, 0xa1, 0xa6]
      : node === 1 ? [0x41, 0x42, 0x43, 0xa1] : [0x41, 0x42, 0x43]
    assert.deepEqual(r.remainingWallAt(0, node), expected)
    assert.deepEqual(r.wallViewAt(0, node).filter(t => !t.consumed).map(t => t.tile), expected)
  }
})

test('规则可从旧牌谱标题解析，普通摸牌仍从牌头取', () => {
  const r = replay('', [['d', 11], ['gd', 46]], 'guangdong')
  assert.deepEqual(r.remainingWallAt(0, 2), [0x42, 0x43, 0xa1])
  assert.deepEqual(r.wallViewAt(0, 2).map(t => t.consumed), [true, false, false, false, true])
})

test('国标保留既有的尾墩上下张交替顺序', () => {
  const r = replay('guobiao', [['gd', 41], ['bd', 46]])
  assert.deepEqual(r.remainingWallAt(0, 1), [0x41, 0x42, 0x43, 0xa6])
  assert.deepEqual(r.wallViewAt(0, 1).map(t => t.consumed), [false, false, false, true, false])
  assert.deepEqual(r.remainingWallAt(0, 2), [0x41, 0x42, 0x43])
})
