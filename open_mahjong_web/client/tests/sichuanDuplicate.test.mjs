import test from 'node:test'
import assert from 'node:assert/strict'
import { groupEventRoomCards } from '../src/utils/sichuanDuplicate.js'
import { buildEventRoomCreation, createEventRoomCreationForm } from '../src/utils/eventRoomCreation.js'
import { isSichuanBloodFlow, sichuanDoublesSummary } from '../src/utils/sichuanReplay.js'

test('paired creation emits only event pair config and selected board count',()=>{
  for(const sub_rule of ['sichuan/doubles_duplicate','sichuan/xueliu_doubles_duplicate']) {
    const form={...createEventRoomCreationForm(),room_rule:'sichuan',sub_rule,duplicate_round_count:8,room_count:3}
    const body=buildEventRoomCreation(form)
    assert.equal(body.room_config.sub_rule,sub_rule)
    assert.equal(body.room_config.duplicate_round_count,8)
    assert.equal(body.room_config.allow_spectator,false)
    assert.equal(body.room_count,3)
    assert.equal('duplicate_enabled' in body,false)
  }
})

test('groups use authoritative pair id, preserve independent rooms and put open before closed',()=>{
  const card=(id,pair,table)=>({room:{room_id:id,room_name:'相同名字',duplicate_match_id:pair,duplicate_table:table}})
  const groups=groupEventRoomCards([card('1','x','closed'),card('2',null),card('3','y','open'),card('4','x','open'),card('5','y','closed')])
  assert.deepEqual(groups.map(g=>g.cards.map(c=>c.room.room_id)),[['4','1'],['2'],['3','5']])
  assert.deepEqual(groups.map(g=>g.paired),[true,false,true])
})

test('new bloodflow record stays continuous-win and team-aware',()=>{
  const record={rule:'sichuan',record:{game_title:{rule:'sichuan',sub_rule:'sichuan/xueliu_doubles_duplicate',team_by_player_index:[1,0,1,0]}}}
  assert.ok(isSichuanBloodFlow(record)); assert.equal(sichuanDoublesSummary(record).teams.length,2)
  assert.deepEqual(sichuanDoublesSummary(record).teams.map(team => team.name), ['蓝队', '红队'])
})

test('paired replay preserves both score totals and the selected winner', () => {
  const result = { scores: { 0: 80, 1: 60 }, small_scores: { 0: 80, 1: 60 }, imp_scores: { 0: 4, 1: 6 },
    winner_team_id: 0, reason: 'raw_difference', match_points: { 0: 1, 1: 0 } }
  const detail = { record: { game_title: { sub_rule: 'sichuan/xueliu_doubles_duplicate', team_by_player_index: [0, 1, 0, 1] }, doubles_result: result } }
  assert.deepEqual(sichuanDoublesSummary(detail).result, result)
})
