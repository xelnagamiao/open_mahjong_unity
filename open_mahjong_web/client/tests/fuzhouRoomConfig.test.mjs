import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { buildFuzhouRoomPayload, loadFuzhouForm, FUZHOU_VERSION } from '../src/utils/fuzhouRoomConfig.js'
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'
const require = createRequire(import.meta.url)
const { normalizeFuzhouRoomConfig } = require('../../server/utils/fuzhouRoomSettings.js')
const { readRoomSettings, applyRoomSettingsChange, resolveRoomSelection, SettingsError } = require('../../server/utils/eventRoomSettings.js')
test('all 32 switch combinations, three durations, and three base scores survive client/Node/preset round trips', () => {
  const keys = ['tips','count_tips','pointer_tips','tourist_limit','allow_spectator']
  for (let mask=0;mask<32;mask++) for (const rounds of [1,2,4]) for (const base of [10,15,20]) {
    const form=loadFuzhouForm({room_rule:'fuzhou',room_name:'福州测试'})
    keys.forEach((key,i)=>{form[key]=Boolean(mask&(1<<i))});form.game_round=rounds;form.fuzhou_base_score=base
    const built=buildFuzhouRoomPayload(form)
    assert.deepEqual(buildEventRoomSettings(form),built)
    const normalized=normalizeFuzhouRoomConfig(built.room_config,SettingsError)
    const roundtrip=buildEventRoomSettings(createEventRoomForm({room_rule:'fuzhou',room_config:normalized}))
    assert.deepEqual(roundtrip,built)
    const doc=readRoomSettings({})
    const result=applyRoomSettingsChange(doc,{kind:'preset-create',userId:10001,eventStatus:'active',body:{revision:doc.revision,name:'福州',...built}},{id:'fz'})
    const stored=readRoomSettings(JSON.stringify(result.settings))
    const selected=resolveRoomSelection(stored,{revision:stored.revision,preset_id:'fz'})
    assert.deepEqual(selected.room_config,normalized)
    assert.deepEqual(normalized.detailed_config,{rule_version:FUZHOU_VERSION,base_score:base})
    assert.equal(eventRoomSettingsRows(built).find(r=>r.label==='底分')?.value,`${base}分`)
    assert.equal(eventRoomSettingsRows(built).find(r=>r.label==='局数')?.value,`${rounds*4}局`)
  }
})
for (const [key,value] of [['game_round',0],['game_round',3],['game_round',5],['round_timer',-1],['step_timer',101],['tips',1],['sub_rule','fuzhou/other'],['duplicate_key','x'],['fuzhou_base_score',12],['fuzhou_base_score','15']])
  test(`client rejects invalid ${key}=${value}`,()=>assert.throws(()=>buildFuzhouRoomPayload({...loadFuzhouForm({}),[key]:value})))
for (const raw of [null,[],true,{unknown:1},{tips:1},{tactical_call:true},{use_flowers:false},{detailed_config:[]},{detailed_config:{base_score:12}},{detailed_config:{base_score:'15'}},{detailed_config:{rule_version:'wrong'}},{detailed_config:{unknown:1}},{room_name:1},{duplicate_key:'x'},{game_round:1.5},{game_round:3},{step_timer:null}])
  test(`Node rejects ${JSON.stringify(raw)}`,()=>assert.throws(()=>normalizeFuzhouRoomConfig(raw,SettingsError)))
test('custom and zero timers remain exact; optional config defaults are independent',()=>{
  for(const bank of [0,20,47,1000])for(const step of [0,3,5,9,100]){
    const payload=buildFuzhouRoomPayload({...loadFuzhouForm({}),round_timer:bank,step_timer:step})
    const value=normalizeFuzhouRoomConfig(payload.room_config,SettingsError)
    assert.equal(value.round_timer,bank);assert.equal(value.step_timer,step)
  }
  const a=normalizeFuzhouRoomConfig({},SettingsError);a.detailed_config.base_score=20
  assert.equal(normalizeFuzhouRoomConfig({},SettingsError).detailed_config.base_score,15)
  assert.equal(loadFuzhouForm({}).fuzhou_base_score,15)
})
