import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { fileURLToPath } from 'node:url'
async function moduleAt(path) {
  const built=await build({entryPoints:[fileURLToPath(new URL(path,import.meta.url))],bundle:true,write:false,platform:'node',format:'esm',define:{'import.meta.url':JSON.stringify(new URL(path,import.meta.url).href)}})
  return import('data:text/javascript;base64,'+Buffer.from(built.outputFiles[0].text).toString('base64'))
}
const wild=await moduleAt('../src/game2d/calc/guobiao/fourJokers.ts')
const { calculateLocalHint }=await moduleAt('../src/game2d/calc/localHintEngine.ts')
async function waitData(ctx) {
  for(let i=0;i<200;i++) {
    const data=wild.buildFourJokersWaitData(ctx,false)
    if(data)return data
    await new Promise(resolve=>setTimeout(resolve,10))
  }
  assert.fail('Local wait job did not finish')
}
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
test('waits are computed locally and ignore legacy server results',async()=>{
  const hand=[11,11,11,21,22,23,31,32,33,44,44,44,45]
  wild.acceptFourJokersSnapshot({hand,melds:[],waits:[11],discard_waits:{}})
  const old=globalThis.fetch;globalThis.fetch=()=>{throw new Error('eager scoring')}
  try {
    const ctx={tips:true,hand,combinations:[],flowerCount:0,playerIndex:0,currentRound:1,hepaiLimit:8,
      subRule:'guobiao/four_jokers',seatDiscards:[[],[59],[],[]],seatCombinations:[[],[],[],[]]}
    const data=await waitData(ctx)
    assert.deepEqual(data.details.map(d=>d.tile),[45,59])
    assert.equal(data.details[1].remaining_count,3)
    assert.deepEqual(data.details[1].four_jokers_request.hand_tiles,[...hand,59])
    assert.equal(hand.length,13)
  } finally {globalThis.fetch=old}
})

test('multi-wait defaults off, stays four-joker only, and travels with each hint',async()=>{
  assert.equal(createDefaultGuobiaoRoomConfig().joker_multi_wait,false)
  assert.equal(buildGuobiaoRoomPayload({sub_rule:'guobiao/standard',joker_multi_wait:true}).room_config.joker_multi_wait,false)
  const hand=[15,16,17,32,33,34,38,39,59,59],melds=['s24']
  wild.acceptFourJokersSnapshot({hand,melds,waits:[45,59],discard_waits:{}})
  for(const enabled of [true,false]) {
    assert.equal(buildGuobiaoRoomPayload({sub_rule:'guobiao/four_jokers',joker_multi_wait:enabled}).room_config.joker_multi_wait,enabled)
    const ctx={tips:true,hand,combinations:melds,flowerCount:2,playerIndex:1,currentRound:1,hepaiLimit:8,
      subRule:'guobiao/four_jokers',jokerMultiWait:enabled,seatDiscards:[[],[],[],[]],seatCombinations:[[],melds,[],[]]}
    assert.ok((await waitData(ctx)).details.every(d=>d.four_jokers_request.joker_multi_wait===enabled))
  }
})

test('switching multi-wait does not reuse an answer under the other scoring policy',async()=>{
  const old=globalThis.fetch;let calls=0
  globalThis.fetch=async()=>{calls++;throw new Error('Hints must not use HTTP')}
  try {
    const body={hand_tiles:[37,38,59,46,46],tiles_combination:['s32','s22','s18'],get_tile:37,
      ron_way:['点和'],self_way:['自摸'],hepai_limit:8}
    assert.equal((await wild.resolveFourJokersHint({...body,joker_multi_wait:false})).score,8)
    const multi=await wild.resolveFourJokersHint({...body,joker_multi_wait:true})
    assert.equal(multi.kind,'zimo');assert.equal(multi.score,8)
    assert.equal((await wild.resolveFourJokersHint({...body,joker_multi_wait:false})).score,8)
    assert.equal(calls,0)
  } finally {globalThis.fetch=old}
})

test('standard calculator counts all-exposed ron independently of the wait hint flag',async()=>{
  const {hepaiCheck}=await moduleAt('../src/game2d/calc/guobiao/index.ts')
  const score=hepaiCheck([24,24],['k32','k16','k36','k28'],['点和','花牌','花牌','花牌'],24,false)
  assert.equal(score.fan,35)
})
test('exact hints share local worker jobs and bound concurrency without HTTP',async()=>{
  const old=globalThis.fetch,oldWorker=globalThis.Worker;let active=0,max=0,calls=0,network=0
  globalThis.fetch=async()=>{network++;throw new Error('Hints must not use HTTP')}
  globalThis.Worker=class {
    postMessage(message){calls++;active++;max=Math.max(max,active);setTimeout(()=>{
      const result=calculateLocalHint(message.kind,message.body);active--;this.onmessage({data:{id:message.id,result}})
    },2)}
  }
  try {
    const body={hand_tiles:[11,11,11,21,22,23,31,32,33,44,44,44,45,59],tiles_combination:[],get_tile:59,ron_way:['点和'],self_way:['自摸'],hepai_limit:8}
    const jobs=Array.from({length:12},(_,i)=>wild.resolveFourJokersHint({...body,hepai_limit:8+i%6}))
    assert.equal(jobs[0],jobs[6])
    const results=await Promise.all(jobs)
    assert.equal(calls,6);assert.equal(max,2);assert.equal(network,0);assert.ok(results.every(x=>x.kind==='zimo'))
  } finally {globalThis.fetch=old;globalThis.Worker=oldWorker}
})

test('queued scores keep the hand that was requested when later UI input changes',async()=>{
  const body={hand_tiles:[11,11,11,21,22,23,31,32,33,44,44,44,45,59],tiles_combination:[],get_tile:59,ron_way:['点和'],self_way:['自摸']}
  const requests=Array.from({length:6},(_,i)=>({...body,hand_tiles:[...body.hand_tiles],hepai_limit:1000+i}))
  const jobs=requests.map(request=>wild.resolveFourJokersHint(request))
  requests.forEach(request=>{request.hand_tiles.length=0})
  const results=await Promise.all(jobs)
  assert.ok(results.every(result=>result.score>0))
})

test('regional snapshots freeze nested inputs before asynchronous module loading',async()=>{
  const {resolveLocalHintSnapshot}=await moduleAt('../src/utils/localHintSnapshots.js')
  const request={rule:'hangzhou',hand:[11,12,13,21,22,23,31,32,33,41,41,41,45],melds:[],context:{cai_piao_count:0}}
  const original=JSON.parse(JSON.stringify(request)),before=globalThis.fetch
  globalThis.fetch=()=>{throw Error('Hints must not use HTTP')}
  try{
    assert.equal(resolveLocalHintSnapshot({local_request:request}),null)
    request.hand.length=0;request.context.cai_piao_count=3
    let result=null
    for(let i=0;i<200&&!result;i++){
      await new Promise(resolve=>setTimeout(resolve,5))
      result=resolveLocalHintSnapshot({local_request:original})
    }
    assert.deepEqual(result.waiting_tiles,[45,46])
    assert.deepEqual(result.source_hand_tiles,original.hand)
  }finally{globalThis.fetch=before}
})
