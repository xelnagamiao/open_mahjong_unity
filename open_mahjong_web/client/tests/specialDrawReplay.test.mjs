import assert from 'node:assert/strict'
import test from 'node:test'
import { RecordReplay } from '../src/game2d/replay/recordReplay.ts'

for (const [rule, subRule, action, expected] of [
  ['riichi', 'riichi/sanma', 'jiuzhongjiupai', '九种九牌'],
  ['riichi', 'riichi/standard', 'jiuzhongjiupai', '九种九牌'],
  ['classical', 'classical/standard', 'jiuzhongjiupai', '九老峰回'],
  ['riichi', 'riichi/standard', 'four_wind_abort', '四风连打'],
  ['riichi', 'riichi/standard', 'four_kan_abort', '四杠散了'],
  ['riichi', 'riichi/standard', 'four_riichi_abort', '四人立直'],
  ['riichi', 'riichi/standard', 'three_ron_abort', '三家和流局'],
]) {
  test(`${subRule}/${action}: replay step and seek retain the proper draw name`, () => {
    const playerCount = subRule === 'riichi/sanma' ? 3 : 4
    const replay = new RecordReplay({
      game_id: 'draw-caption', rule, players: [],
      record: {
        game_title: { rule, sub_rule: subRule, player_count: playerCount },
        game_round: { round_index_1: { seats: Array.from({ length: playerCount }, (_, i) => i), action_ticks: [[action]] } },
      },
    })
    assert.equal(replay.build(0, 1).actionLabel, expected)
    assert.equal(replay.build(0, 0).actionLabel, '局初')
    assert.equal(replay.build(0, 1).actionLabel, expected)
  })
}
