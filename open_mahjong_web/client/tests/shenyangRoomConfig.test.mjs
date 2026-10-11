import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { loadShenyangForm, buildShenyangRoomPayload, SHENYANG_VERSION } from '../src/utils/shenyangRoomConfig.js'
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows, eventRoomSettingsSummary } from '../src/utils/eventRoomSettings.js'
const require = createRequire(import.meta.url)
const { normalizeShenyangRoomConfig } = require('../../server/utils/shenyangRoomSettings.js')
class SettingsError extends Error { constructor(code,message){super(message);this.code=code} }
test('沈阳房间前后端往返保留完整配置',()=>{
  const form=loadShenyangForm({room_rule:'shenyang',room_name:'沈阳规则测试',password:'test'})
  const payload=buildShenyangRoomPayload(form)
  const config=normalizeShenyangRoomConfig(payload.room_config,SettingsError)
  assert.deepEqual(config,payload.room_config)
  assert.equal(config.use_flowers,false);assert.equal(config.tactical_call,false)
  assert.equal(config.detailed_config.rule_version,SHENYANG_VERSION)
  const restored=createEventRoomForm(payload)
  assert.deepEqual(buildEventRoomSettings(restored),payload)
  assert.match(eventRoomSettingsSummary(payload),/MIL 2025/)
  assert.ok(eventRoomSettingsRows(payload).some(row=>row.value==='六番，每家最高64分'))
})
for(const [key,value] of Object.entries({sub_rule:'shenyang/standard',game_round:0,step_timer:'5',round_timer:1001,tips:1,use_flowers:true,tactical_call:true,detailed_config:{rule_version:'future'},unknown:true})) {
  test('沈阳服务端拒绝不支持设置 '+key,()=>assert.throws(()=>normalizeShenyangRoomConfig({[key]:value},SettingsError)))
}
test('沈阳网页拒绝非法圈数、布尔和复式配置',()=>{
  for(const patch of [{game_round:5},{tips:'yes'},{duplicate_key:'dup_test'}])
    assert.throws(()=>buildShenyangRoomPayload({...loadShenyangForm({room_rule:'shenyang'}),...patch}))
})
