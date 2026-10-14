import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { loadSingaporeForm, buildSingaporeRoomPayload, SINGAPORE_CONFIG } from '../src/utils/singaporeRoomConfig.js'
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows, eventRoomSettingsSummary } from '../src/utils/eventRoomSettings.js'
import catalog from '../../shared/statisticsRules.json' with { type: 'json' }
const require=createRequire(import.meta.url)
const { normalizeSingaporeRoomConfig }=require('../../server/utils/singaporeRoomSettings.js')
const { readRoomSettings, applyRoomSettingsChange }=require('../../server/utils/eventRoomSettings.js')
class SettingsError extends Error { constructor(code,message) {super(message);this.statusCode=code} }

test('Singapore defaults and four wind lengths survive frontend/server round trip',()=>{
  for (const game_round of [1,2,3,4]) {
    const form=loadSingaporeForm({ room_rule:'singapore',room_name:'SG',password:'' },{game_round})
    const payload=buildSingaporeRoomPayload(form)
    const normalized=normalizeSingaporeRoomConfig(payload.room_config,SettingsError)
    assert.equal(normalized.sub_rule,'singapore/spgg2024')
    assert.equal(normalized.game_round,game_round)
    assert.deepEqual(normalized.detailed_config,SINGAPORE_CONFIG)
    assert.deepEqual(buildEventRoomSettings(createEventRoomForm(payload)).room_config,payload.room_config)
    assert.match(eventRoomSettingsSummary(form),/SPGG 2024/)
    assert.ok(eventRoomSettingsRows(form).some(row=>row.value==='1台 / 5台'))
  }
})
test('Singapore rejects wrong variants, tile pools, fan caps and coerced inputs',()=>{
  const base=normalizeSingaporeRoomConfig({},SettingsError)
  for (const bad of [{sub_rule:'singapore/unknown'},{game_round:0},{game_round:true},{tips:1},{use_flowers:false},{open_cuohe:true},{hepai_limit:0},{detailed_config:{...SINGAPORE_CONFIG,fan_cap:10}}])
    assert.throws(()=>normalizeSingaporeRoomConfig({...base,...bad},SettingsError))
  const form=loadSingaporeForm({room_rule:'singapore'})
  assert.throws(()=>buildSingaporeRoomPayload({...form,step_timer:'5'}))
})
test('Singapore statistics are isolated and do not expose fan accumulation',()=>{
  const rows=catalog.rules.filter(row=>row.rule==='singapore')
  assert.equal(rows.length,1)
  assert.equal(rows[0].sub_rule,'singapore/spgg2024')
  assert.equal(rows[0].player_count,4)
  assert.equal(rows[0].basic_stats,true)
  assert.equal(rows[0].fan_stats,false)
})
