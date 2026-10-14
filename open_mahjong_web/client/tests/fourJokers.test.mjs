import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { fileURLToPath } from 'node:url'
async function moduleAt(path) {
  const built=await build({entryPoints:[fileURLToPath(new URL(path,import.meta.url))],bundle:true,write:false,platform:'node',format:'esm'})
  return import('data:text/javascript;base64,'+Buffer.from(built.outputFiles[0].text).toString('base64'))
}
const wild=await moduleAt('../src/game2d/calc/guobiao/fourJokers.ts')
const {salasasaTileToMmcr,mmcrTileToSalasasa}=await moduleAt('../src/game2d/salasasa/gameAdapter.ts')
const {STANDARD_FACE_IDS,isFlowerFaceId,tileFaceAssetUrl}=await moduleAt('../src/game2d/lib/tileFaceAsset.ts')
import { recordHandJokers } from '../src/game2d/lib/handJokers.js'
import { buildGuobiaoRoomPayload, createDefaultGuobiaoRoomConfig } from '../src/utils/guobiaoRoomConfig.js'
test('red flower survives transport, textures, replay and room settings',()=>{
  assert.equal(mmcrTileToSalasasa(salasasaTileToMmcr(59)),59)
  assert.ok(STANDARD_FACE_IDS.includes(59));assert.equal(isFlowerFaceId(59),false)
  assert.match(tileFaceAssetUrl(59,{baseUrl:'/',black:true}),/Black\/59.svg$/)
  assert.deepEqual(recordHandJokers({rule:'guobiao',sub_rule:'guobiao/four_jokers'}, {},0),[59])
  const config=buildGuobiaoRoomPayload({sub_rule:'guobiao/four_jokers',tian_di_ren_he:true}).room_config
  assert.equal(config.hepai_limit,8);assert.equal(config.tian_di_ren_he,true)
  assert.throws(()=>buildGuobiaoRoomPayload({sub_rule:config.sub_rule,duplicate_key:'x'}),/复式/)
})
test('structural snapshots display waits without eagerly requesting scoring',()=>{
  const hand=[11,11,11,21,22,23,31,32,33,44,44,44,45]
  wild.acceptFourJokersSnapshot({hand,melds:[],waits:[45,59],discard_waits:{}})
  const old=globalThis.fetch;globalThis.fetch=()=>{throw new Error('eager scoring')}
  try {
    const ctx={tips:true,hand,combinations:[],flowerCount:0,playerIndex:0,currentRound:1,hepaiLimit:8,
      subRule:'guobiao/four_jokers',seatDiscards:[[],[59],[],[]],seatCombinations:[[],[],[],[]]}
    const data=wild.buildFourJokersWaitData(ctx,false)
    assert.deepEqual(data.details.map(d=>d.tile),[45,59])
    assert.equal(data.details[1].remaining_count,3)
    assert.deepEqual(data.details[1].four_jokers_request.hand_tiles,[...hand,59])
    assert.equal(hand.length,13)
  } finally {globalThis.fetch=old}
})

test('multi-wait defaults off, stays four-joker only, and travels with each hint',()=>{
  assert.equal(createDefaultGuobiaoRoomConfig().joker_multi_wait,false)
  assert.equal(buildGuobiaoRoomPayload({sub_rule:'guobiao/standard',joker_multi_wait:true}).room_config.joker_multi_wait,false)
  const hand=[15,16,17,32,33,34,38,39,59,59],melds=['s24']
  wild.acceptFourJokersSnapshot({hand,melds,waits:[45,59],discard_waits:{}})
  for(const enabled of [true,false]) {
    assert.equal(buildGuobiaoRoomPayload({sub_rule:'guobiao/four_jokers',joker_multi_wait:enabled}).room_config.joker_multi_wait,enabled)
    const ctx={tips:true,hand,combinations:melds,flowerCount:2,playerIndex:1,currentRound:1,hepaiLimit:8,
      subRule:'guobiao/four_jokers',jokerMultiWait:enabled,seatDiscards:[[],[],[],[]],seatCombinations:[[],melds,[],[]]}
    assert.ok(wild.buildFourJokersWaitData(ctx,false).details.every(d=>d.four_jokers_request.joker_multi_wait===enabled))
  }
})

test('switching multi-wait does not reuse an answer under the other scoring policy',async()=>{
  const old=globalThis.fetch;let calls=0
  globalThis.fetch=async(_url,options)=>{calls++;const body=JSON.parse(options.body)
    return {ok:true,json:async()=>({kind:body.joker_multi_wait?'wuyi':'dianhe',label:body.joker_multi_wait?'7番':'8番',score:body.joker_multi_wait?7:8})}}
  try {
    const body={hand_tiles:[37,38,59,46,46],tiles_combination:['s32','s22','s18'],get_tile:37,
      ron_way:['点和'],self_way:['自摸'],hepai_limit:8}
    assert.equal((await wild.resolveFourJokersHint({...body,joker_multi_wait:false})).score,8)
    assert.equal((await wild.resolveFourJokersHint({...body,joker_multi_wait:true})).score,7)
    assert.equal((await wild.resolveFourJokersHint({...body,joker_multi_wait:false})).score,8)
    assert.equal(calls,2)
  } finally {globalThis.fetch=old}
})

test('standard calculator counts all-exposed ron independently of the wait hint flag',async()=>{
  const {hepaiCheck}=await moduleAt('../src/game2d/calc/guobiao/index.ts')
  const score=hepaiCheck([24,24],['k32','k16','k36','k28'],['点和','花牌','花牌','花牌'],24,false)
  assert.equal(score.fan,35)
})
test('exact hints share requests and bound network concurrency',async()=>{
  const old=globalThis.fetch;let active=0,max=0,calls=0
  globalThis.fetch=async()=>{calls++;active++;max=Math.max(max,active);await new Promise(r=>setTimeout(r,2));active--
    return {ok:true,json:async()=>({kind:'zimo',label:'自摸8番',score:8})}}
  try {
    const body={hand_tiles:[59],tiles_combination:[],get_tile:59,ron_way:['点和'],self_way:['自摸'],hepai_limit:8}
    const results=await Promise.all(Array.from({length:12},(_,i)=>wild.resolveFourJokersHint({...body,hepai_limit:8+i%6})))
    assert.equal(calls,6);assert.equal(max,2);assert.ok(results.every(x=>x.kind==='zimo'))
  } finally {globalThis.fetch=old}
})
