import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { loadHangzhouForm, buildHangzhouRoomPayload, HANGZHOU_VERSION } from '../src/utils/hangzhouRoomConfig.js'
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'
const require = createRequire(import.meta.url)
const { normalizeHangzhouRoomConfig } = require('../../server/utils/hangzhouRoomSettings.js')
const { readRoomSettings, applyRoomSettingsChange } = require('../../server/utils/eventRoomSettings.js')
class SettingsError extends Error { constructor(statusCode, message) { super(message); this.statusCode = statusCode } }
const switches=['tips','count_tips','pointer_tips','tourist_limit','allow_spectator']
test('杭州32开关组合、4种局长、3组计时在普通馆室和预设完整往返', () => {
  for(let bits=0;bits<32;bits++) for(const rounds of [1,2,3,4]) for(const timers of [[0,0],[20,5],[1000,100]]) {
    const form=loadHangzhouForm({room_rule:'hangzhou',room_name:' 杭州测试 '})
    switches.forEach((key,i)=>{form[key]=Boolean(bits&(1<<i))});form.game_round=rounds;[form.round_timer,form.step_timer]=timers
    const payload=buildHangzhouRoomPayload(form),stored=normalizeHangzhouRoomConfig(payload.room_config,SettingsError)
    const restored=createEventRoomForm({room_rule:'hangzhou',room_config:stored})
    assert.deepEqual(buildEventRoomSettings(restored).room_config,stored)
    for(const key of [...switches,'game_round','round_timer','step_timer']) assert.equal(restored[key],form[key],key)
    for(const kind of ['manual','preset-create']) {
      const doc=readRoomSettings({})
      const result=applyRoomSettingsChange(doc,{kind,userId:10001,eventStatus:'active',body:{revision:doc.revision,room_rule:'hangzhou',room_config:stored,name:'杭州'}}).settings
      const entry=kind==='manual'?result.manual:result.presets[0]
      assert.equal(entry.room_config.detailed_config.rule_version,HANGZHOU_VERSION)
      assert.deepEqual(readRoomSettings(result),result)
    }
  }
})
test('杭州固定政策和类型校验拒绝跨规则、未知设置及不合法输入',()=>{
  const inputs=[null,[],{sub_rule:'hangzhou/other'},{duplicate_key:'abc'},{duplicate_key:0},{game_round:true},{game_round:0},{game_round:5},{game_round:1.5},{round_timer:-1},{round_timer:1001},{step_timer:101},{room_name:'x'.repeat(129)},{room_name:12},{detailed_config:null},{detailed_config:{rule_version:'wrong'}},{detailed_config:{fan_cap:5}},{extra:true}]
  for(const key of switches) inputs.push({[key]:'false'})
  for(const key of ['use_flowers','open_cuohe','tactical_call','claim_protection','tian_di_ren_he']) inputs.push({[key]:true},{[key]:0})
  for(const value of inputs) assert.throws(()=>normalizeHangzhouRoomConfig(value,SettingsError))
  for(const patch of [{game_round:0},{step_timer:101},{tips:'false'},{sub_rule:'other'},{duplicate_key:'abc'},{room_name:'x'.repeat(129)}]) assert.throws(()=>buildHangzhouRoomPayload({...loadHangzhouForm({}),...patch}))
  const rows=eventRoomSettingsRows({room_rule:'hangzhou',room_config:{}},{hangzhou:'杭州麻将'})
  assert.ok(rows.some(row=>row.label==='局数'&&row.value==='16局'))
  assert.ok(rows.some(row=>row.label==='财神'&&row.value==='白板'))
})
