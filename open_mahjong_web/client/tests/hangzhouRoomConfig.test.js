import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { loadHangzhouForm, buildHangzhouRoomPayload, HANGZHOU_VERSION } from '../src/utils/hangzhouRoomConfig.js'
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'
const require = createRequire(import.meta.url)
const { normalizeHangzhouRoomConfig } = require('../../server/utils/hangzhouRoomSettings.js')
const { readRoomSettings, applyRoomSettingsChange } = require('../../server/utils/eventRoomSettings.js')
class SettingsError extends Error { constructor(statusCode, message) { super(message); this.statusCode = statusCode } }
const switches=['tips','count_tips','pointer_tips','tourist_limit','allow_spectator','tactical_call']

test('杭州真人房默认开启战鸣，明确关闭值在重载、摘要和预设中保留', () => {
  assert.equal(loadHangzhouForm({}).tactical_call, true)
  assert.equal(normalizeHangzhouRoomConfig({}, SettingsError).tactical_call, true)
  for (const enabled of [false, true]) {
    const config = normalizeHangzhouRoomConfig({ tactical_call: enabled }, SettingsError)
    const form = createEventRoomForm({ room_rule: 'hangzhou', room_config: config })
    assert.equal(form.tactical_call, enabled)
    assert.equal(buildEventRoomSettings(form).room_config.tactical_call, enabled)
    const rows = eventRoomSettingsRows({ room_rule: 'hangzhou', room_config: config }, { hangzhou: '杭州麻将' })
    assert.ok(rows.some(row => row.label === '战术鸣牌' && row.value === (enabled ? '开启' : '关闭')))
    assert.equal(config.claim_protection, false)
  }
})

test('杭州建房默认20+5，保存和重载保留自定义与零时间', () => {
  const defaults = loadHangzhouForm({})
  assert.deepEqual([defaults.round_timer, defaults.step_timer], [20, 5])
  assert.deepEqual([normalizeHangzhouRoomConfig({}, SettingsError).round_timer,
    normalizeHangzhouRoomConfig({}, SettingsError).step_timer], [20, 5])
  for (const [bank, step] of [[40, 8], [0, 5], [20, 0], [0, 0]]) {
    const form = loadHangzhouForm({}, { round_timer: bank, step_timer: step })
    const stored = normalizeHangzhouRoomConfig(buildHangzhouRoomPayload(form).room_config, SettingsError)
    const restored = loadHangzhouForm({}, stored)
    assert.deepEqual([restored.round_timer, restored.step_timer], [bank, step])
    const rows = eventRoomSettingsRows({ room_rule: 'hangzhou', room_config: stored })
    assert.ok(rows.some(row => row.label === '局时储备' && row.value === `${bank} 秒`))
    assert.ok(rows.some(row => row.label === '步时' && row.value === `${step} 秒`))
    assert.ok(!rows.some(row => row.value === '不限时'))
  }
})
test('杭州64开关组合、4种局长、3组计时在普通馆室和预设完整往返', () => {
  for(let bits=0;bits<64;bits++) for(const rounds of [1,2,3,4]) for(const timers of [[0,0],[20,5],[1000,100]]) {
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
  for(const key of ['use_flowers','open_cuohe','claim_protection','tian_di_ren_he']) inputs.push({[key]:true},{[key]:0})
  inputs.push({tactical_call:0})
  for(const value of inputs) assert.throws(()=>normalizeHangzhouRoomConfig(value,SettingsError))
  for(const patch of [{game_round:0},{step_timer:101},{tips:'false'},{sub_rule:'other'},{duplicate_key:'abc'},{room_name:'x'.repeat(129)}]) assert.throws(()=>buildHangzhouRoomPayload({...loadHangzhouForm({}),...patch}))
  const rows=eventRoomSettingsRows({room_rule:'hangzhou',room_config:{}},{hangzhou:'杭州麻将'})
  assert.ok(rows.some(row=>row.label==='局数'&&row.value==='16局'))
  assert.ok(rows.some(row=>row.label==='财神'&&row.value==='白板'))
})
