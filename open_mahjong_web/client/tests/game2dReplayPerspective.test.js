import test from 'node:test'
import assert from 'node:assert/strict'
import { RecordReplay } from '../src/game2d/replay/recordReplay.ts'
import { parseRecordShareInput, replaySharePath } from '../src/utils/recordShareLink.js'

function record(title = {}) {
  return {
    game_id: 'perspective', created_at: '', rule: 'guobiao',
    // API 排名顺序与原始座位顺序不同。
    players: [2, 0, 3, 1].map(original => ({
      user_id: 100 + original, username: `player-${original}`,
      score: 0, rank: 1, original_player_index: original,
    })),
    record: {
      game_title: { p0_uid: 100, p1_uid: 101, p2_uid: 102, p3_uid: 103, ...title },
      game_round: Object.fromEntries([[2, 0, 3, 1], [1, 3, 0, 2]].map((seats, index) => [
        `round_index_${index + 1}`,
        { seats, start_player_index: 0, action_ticks: [['d', 11], ['c', 11]] },
      ])),
    },
  }
}

test('默认视角按登录 UID 匹配原始玩家，不使用排名或当前座位', () => {
  const replay = new RecordReplay(record())
  for (let original = 0; original < 4; original++) {
    assert.equal(replay.defaultViewerOriginal(String(100 + original)), original)
  }
})

test('2D / 3D 分享优先恢复所选玩家，跨局换座仍跟随本人', () => {
  const replay = new RecordReplay(record())
  for (const kind of ['2d', '3d', '2d-node', '3d-node']) {
    const path = replaySharePath(kind, 'perspective', 2, 1, 102, 2)
    const parsed = parseRecordShareInput(path)
    const viewer = replay.sharedViewerOriginal(parsed.player, parsed.playerIndex, replay.defaultViewerOriginal(101))
    assert.equal(viewer, 2)
    for (const round of [0, 1]) {
      assert.equal(replay.build(round, 1, viewer).snapshot.viewer.seat_index, replay.rounds[round].seats[2])
    }
    assert.equal(parsed.round, kind.endsWith('-node') ? 2 : undefined)
    assert.equal(parsed.node, kind.endsWith('-node') ? 1 : undefined)
  }
})

test('重复机器人 ID、外部牌谱、三人牌谱与无效参数', () => {
  const detail = record({p1_uid: 0, p2_uid: 0, is_external: true})
  const replay = new RecordReplay(detail)
  assert.equal(replay.sharedViewerOriginal(0, 1, 0), 1)
  assert.equal(replay.sharedViewerOriginal(0, 2, 0), 2)
  assert.equal(replay.sharedViewerOriginal(0, undefined, 3), 3)
  assert.equal(replay.sharedViewerOriginal(999, 2, 3), 3)
  assert.equal(replay.sharedViewerOriginal(undefined, undefined, 3), 3)
  detail.rule = 'malaysia'
  assert.equal(new RecordReplay(detail).sharedViewerOriginal(undefined, 3, 1), 1)
  for (const query of ['', '?player=&playerIndex=', '?player=-1&playerIndex=4', '?player=NaN&playerIndex=1.5']) {
    const parsed = parseRecordShareInput(`/2d/record/perspective${query}`)
    assert.equal(parsed.player, undefined)
    assert.equal(parsed.playerIndex, undefined)
    assert.equal(parsed.node, undefined)
  }
})

test('自己的视角跨局、步进、回退保持同一玩家，手动切换也保持', () => {
  const replay = new RecordReplay(record())
  const own = replay.defaultViewerOriginal(102)
  for (const viewer of [own, 3]) {
    for (const round of [0, 1, 0]) {
      for (const node of [0, 1, 2, 0]) {
        const snapshot = replay.build(round, node, viewer).snapshot
        assert.equal(snapshot.viewer.seat_index, replay.rounds[round].seats[viewer])
        assert.equal(replay.playerForSeat(replay.rounds[round], snapshot.viewer.seat_index).user_id, 100 + viewer)
      }
    }
  }
})

test('匿名、非参赛账号及外部牌谱安全回退到首位', () => {
  const replay = new RecordReplay(record())
  for (const uid of [null, undefined, '', 0, -1, 'invalid', 999]) {
    assert.equal(replay.defaultViewerOriginal(uid), 0)
  }
  for (const title of [{ is_external: true }, { source_format: 'mjai' }, { source_format: 'tziakcha' }, { source_format: 'botzone' }]) {
    assert.equal(new RecordReplay(record(title)).defaultViewerOriginal(102), 0)
  }
})

test('缺少标题 UID 时可按玩家的原始位置匹配，无效位置不按排名猜测', () => {
  const detail = record()
  detail.record.game_title = {}
  assert.equal(new RecordReplay(detail).defaultViewerOriginal(103), 3)
  for (const invalid of [null, undefined, '', -1, 4, 1.5]) {
    detail.players[2].original_player_index = invalid
    assert.equal(new RecordReplay(detail).defaultViewerOriginal(103), 0)
  }
})
