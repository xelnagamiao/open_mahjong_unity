import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { buildEventRoomSettings, createEventRoomForm, eventRoomSettingsRows } from '../src/utils/eventRoomSettings.js'
import { riichiWinLines } from '../src/utils/riichiWinRequirements.js'
const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange } = require('../../server/utils/eventRoomSettings.js')

function persist(room_config) {
  const doc=readRoomSettings({})
  return applyRoomSettingsChange(doc, {kind:'manual',body:{revision:doc.revision,room_rule:'riichi',room_config},
    userId:10001,eventStatus:'active'}).settings.manual
}

test('all win lines survive editing, Node validation, persistence and display', () => {
  assert.deepEqual(riichiWinLines.map(row=>row.label), ['不限','2000（闲家标准）','4000（闲家标准）','6000（闲家标准）','满贯','跳满','倍满','三倍满','役满'])
  for (const line of riichiWinLines) {
    const form=createEventRoomForm({room_rule:'riichi'})
    form.riichi_hepai_limit=3
    form.minimum_win_score=line.value
    const stored=persist(buildEventRoomSettings(form).room_config)
    const restored=createEventRoomForm(stored)
    assert.equal(restored.riichi_hepai_limit,3)
    assert.equal(restored.minimum_win_score,line.value)
    const rows=eventRoomSettingsRows(stored)
    assert.equal(rows.find(row=>row.label==='起和线').value,line.label)
    assert.equal(rows.find(row=>row.label==='起和番').value,'3 番（不含宝牌）')
  }
})

test('legacy rooms default to one actual han and no score line', () => {
  const stored=persist({starting_score:30000})
  assert.equal(stored.room_config.hepai_limit,1)
  assert.equal(stored.room_config.detailed_config.minimum_win_score,0)
  assert.equal(stored.room_config.detailed_config.ura_dora,true)
  const form=createEventRoomForm(stored)
  form.room_rule='guobiao'
  assert.equal(buildEventRoomSettings(form).room_config.detailed_config,undefined)
  form.room_rule='taiwan'
  assert.equal(buildEventRoomSettings(form).room_config.hepai_limit,undefined)
})

test('invalid win lines and noninteger yaku limits are rejected by both boundaries', () => {
  for(const value of [-1,1000,3000,50000,'8000',true,false,null]) {
    const form=createEventRoomForm({room_rule:'riichi'})
    form.minimum_win_score=value
    assert.throws(()=>buildEventRoomSettings(form),/起和线/)
    assert.throws(()=>persist({detailed_config:{minimum_win_score:value}}),/起和线/)
  }
  for(const value of [0,65,1.5,'3',true,null]) {
    const form=createEventRoomForm({room_rule:'riichi'})
    form.riichi_hepai_limit=value
    assert.throws(()=>buildEventRoomSettings(form),/起和番/)
    assert.throws(()=>persist({hepai_limit:value}),/起和番/)
  }
  for(const detail of [null,[],{unknown:8000}]) assert.throws(()=>persist({detailed_config:detail}),/起和线/)
})
