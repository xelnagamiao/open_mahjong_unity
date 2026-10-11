import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { formatSubRule } from '../src/utils/gameMeta.js'
import { createEventRoomForm, buildEventRoomSettings } from '../src/utils/eventRoomSettings.js'
import { sichuanDoublesSummary, isSichuanBloodFlow, isSichuanBloodBattle, nextSichuanPlayer, sichuanScoreChanges } from '../src/utils/sichuanReplay.js'
const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange } = require('../../server/utils/eventRoomSettings.js')

test('blood-flow doubles preset survives client/server round trip', () => {
  const form=createEventRoomForm({room_rule:'sichuan',room_config:{sub_rule:'sichuan/xueliu_doubles',game_round:1}})
  const payload=buildEventRoomSettings(form)
  const doc=readRoomSettings({})
  const result=applyRoomSettingsChange(doc,{kind:'manual',userId:101,eventStatus:'active',body:{revision:doc.revision,...payload}})
  assert.equal(result.settings.manual.room_config.sub_rule,'sichuan/xueliu_doubles')
  assert.equal(formatSubRule('sichuan/xueliu_doubles'),'血流双打·2V2')
})

test('team blood flow keeps winners active and reports the team outcome', () => {
  const result={scores:{0:36,1:-36},winner_team_id:0,reason:'total_score',extra_boards:1,match_points:{0:1,1:0}}
  const detail={rule:'sichuan',record:{game_title:{rule:'sichuan',sub_rule:'sichuan/xueliu_doubles',team_by_player_index:[0,1,0,1]},doubles_result:result}}
  assert.ok(isSichuanBloodFlow(detail))
  assert.equal(isSichuanBloodBattle(detail),false)
  assert.equal(nextSichuanPlayer(0,new Set()),1)
  assert.deepEqual(sichuanDoublesSummary(detail).result,result)
  assert.deepEqual(sichuanDoublesSummary(detail).teams.map(t=>t.players.length),[2,2])
  assert.deepEqual(sichuanScoreChanges(['ag',11,'gs',4,-2,0,-2]),[4,-2,0,-2])
  assert.deepEqual(sichuanScoreChanges(['liuju','chajiao',0,'hua_zhu','[]','[-32,32,0,0]']),[-32,32,0,0])
})
