import test from 'node:test'
import assert from 'node:assert/strict'
import {build} from 'esbuild'
import {fileURLToPath} from 'node:url'
import {createRequire} from 'node:module'
import {buildGuobiaoRoomPayload} from '../src/utils/guobiaoRoomConfig.js'
import {buildEventRoomSettings, createEventRoomForm, eventRoomSettingsRows} from '../src/utils/eventRoomSettings.js'
import {buildEventRoomCreation} from '../src/utils/eventRoomCreation.js'
import {recordHandJokers} from '../src/game2d/lib/handJokers.js'
const require=createRequire(import.meta.url)
const {applyRoomSettingsChange,readRoomSettings}=require('../../server/utils/eventRoomSettings.js')
const sub='guobiao/sixteen'
const bundled=await build({stdin:{contents:"export * from './fourJokers';export * from './waitTips'",resolveDir:fileURLToPath(new URL('../src/game2d/calc/guobiao',import.meta.url)),loader:'ts'},bundle:true,write:false,platform:'node',format:'esm'})
const tips=await import('data:text/javascript;base64,'+Buffer.from(bundled.outputFiles[0].text).toString('base64'))
async function localWaitData(ctx,options) {
  for(let i=0;i<200;i++) {
    const data=tips.buildLocalWaitData(ctx,options)
    if(data)return data
    await new Promise(resolve=>setTimeout(resolve,10))
  }
  assert.fail('Local sixteen-tile job did not finish')
}

for(const flowers of [false,true]) for(const opening of [false,true])
for(const wrong of [false,true]) for(const limit of [1,8,32,64]) {
  test(`sixteen preset roundtrip flowers=${flowers} opening=${opening} wrong=${wrong} limit=${limit}`,()=>{
    const form=createEventRoomForm({room_rule:'guobiao',room_config:{sub_rule:sub,use_flowers:flowers,tian_di_ren_he:opening,
      open_cuohe:wrong,cuohe_type:wrong?1:0,hepai_limit:limit,tips:!wrong,round_timer:0,step_timer:0,allow_spectator:false,
      tactical_call:false,claim_protection:false}})
    const body=buildEventRoomSettings(form)
    const saved=applyRoomSettingsChange({}, {kind:'preset-create',body:{revision:0,name:'十六张测试',room_rule:'guobiao',room_config:body.room_config},userId:1}, {id:'sixteen'})
    const persisted=readRoomSettings(saved.settings).presets.find(p=>p.preset_id==='sixteen')
    assert.ok(persisted)
    const loaded=createEventRoomForm(persisted)
    for(const key of ['sub_rule','use_flowers','tian_di_ren_he','open_cuohe','cuohe_type','hepai_limit','tips','round_timer','step_timer','allow_spectator','tactical_call','claim_protection']) assert.equal(loaded[key],form[key],key)
    assert.equal(loaded.joker_multi_wait,false)
    assert.equal(eventRoomSettingsRows(persisted).find(r=>r.label==='子规则').value,'国标麻将（十六张）')
  })
}
test('sixteen rejects ordinary duplicate walls in direct, event and persisted presets',()=>{
  const duplicate_key='dup_'+ 'a'.repeat(32)
  assert.throws(()=>buildGuobiaoRoomPayload({sub_rule:sub,duplicate_key}),/复式/)
  assert.throws(()=>buildEventRoomCreation({...createEventRoomForm({room_rule:'guobiao',room_config:{sub_rule:sub}}),room_count:1,duplicate_enabled:true,auto_duplicate:true,duplicate_round_count:16}),/复式/)
  assert.throws(()=>applyRoomSettingsChange({}, {kind:'preset-create',body:{revision:0,name:'非法牌墙',room_rule:'guobiao',room_config:{sub_rule:sub,duplicate_key}},userId:1}),/复式/)
  assert.deepEqual(recordHandJokers({rule:'guobiao',sub_rule:sub},{},0),[])
})
test('sixteen uses all seventeen tiles in local hint jobs and separates caches',async()=>{
  const hand=[11,12,13,21,22,23,31,32,33,41,41,41,45,45,45,46]
  tips.acceptFourJokersSnapshot({sub_rule:sub,hand,melds:[],waits:[46],discard_waits:{}})
  tips.acceptFourJokersSnapshot({sub_rule:'guobiao/four_jokers',hand,melds:[],waits:[59],discard_waits:{}})
  const old=globalThis.fetch;globalThis.fetch=()=>{throw Error('Hints must not use HTTP')}
  try {
    const ctx={tips:true,hand,combinations:[],flowerCount:2,playerIndex:0,currentRound:1,hepaiLimit:8,subRule:sub,
      seatDiscards:[[],[46],[],[]],seatCombinations:[[],[],[],[]]}
    const rows=await localWaitData(ctx,{includeDiscards:false})
    assert.deepEqual(rows.details.map(d=>d.tile),[46])
    assert.equal(rows.details[0].remaining_count,2)
    const body=rows.details[0].four_jokers_request
    assert.equal(body.sub_rule,sub);assert.equal(body.hand_tiles.length,17)
    assert.equal(body.ron_way.filter(w=>w==='花牌').length,2)
    assert.equal(hand.length,16)
    const complete=[...hand,46]
    tips.acceptFourJokersSnapshot({sub_rule:sub,hand:complete,melds:[],waits:[],discard_waits:{46:[46]}})
    const discards=await localWaitData({...ctx,hand:complete},{includeDiscards:true})
    assert.equal(discards.type,'waits_all')
    assert.ok(discards.details.some(d=>d.discard_tile===46&&d.adds[0].tile===46))
  } finally {globalThis.fetch=old}
})
test('five exposed melds produce a two-tile scoring request',async()=>{
  const hand=[45],melds=['s12','s25','s38','k41','k22']
  tips.acceptFourJokersSnapshot({sub_rule:sub,hand,melds,waits:[45],discard_waits:{}})
  const rows=await localWaitData({tips:true,hand,combinations:melds,flowerCount:0,playerIndex:0,currentRound:1,
    hepaiLimit:8,subRule:sub,seatDiscards:[[],[],[],[]],seatCombinations:[melds,[],[],[]]},{includeDiscards:false})
  assert.equal(rows.details[0].four_jokers_request.tiles_combination.length,5)
  assert.deepEqual(rows.details[0].four_jokers_request.hand_tiles,[45,45])
})
