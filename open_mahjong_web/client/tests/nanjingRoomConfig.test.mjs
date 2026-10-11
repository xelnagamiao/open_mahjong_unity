import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { loadNanjingForm, buildNanjingRoomPayload, NANJING_VERSION } from '../src/utils/nanjingRoomConfig.js'
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows, eventRoomSettingsSummary } from '../src/utils/eventRoomSettings.js'
const require = createRequire(import.meta.url)
const { normalizeNanjingRoomConfig } = require('../../server/utils/nanjingRoomSettings.js')
class SettingsError extends Error { constructor(code,message){super(message);this.code=code} }
test('南京房间前后端往返保留完整配置',()=>{
  const form=loadNanjingForm({room_rule:'nanjing',room_name:'南京规则测试',password:'test'})
  const payload=buildNanjingRoomPayload(form)
  const config=normalizeNanjingRoomConfig(payload.room_config,SettingsError)
  assert.deepEqual(config,payload.room_config)
  assert.equal(config.use_flowers,true);assert.equal(config.tactical_call,false)
  assert.equal(config.detailed_config.rule_version,NANJING_VERSION)
  const restored=createEventRoomForm(payload)
  assert.deepEqual(buildEventRoomSettings(restored),payload)
  assert.match(eventRoomSettingsSummary(payload),/MIL 2024/)
  assert.ok(eventRoomSettingsRows(payload).some(row=>row.value==='120分'))
})
for(const [key,value] of Object.entries({sub_rule:'nanjing/standard',game_round:0,step_timer:'5',round_timer:1001,tips:1,use_flowers:false,tactical_call:true,detailed_config:{rule_version:'future'},unknown:true})) {
  test('南京服务端拒绝不支持设置 '+key,()=>assert.throws(()=>normalizeNanjingRoomConfig({[key]:value},SettingsError)))
}
test('南京网页拒绝非法圈数、布尔和复式配置',()=>{
  for(const patch of [{game_round:5},{tips:'yes'},{duplicate_key:'dup_test'}])
    assert.throws(()=>buildNanjingRoomPayload({...loadNanjingForm({room_rule:'nanjing'}),...patch}))
})
