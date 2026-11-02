import test from 'node:test'
import assert from 'node:assert/strict'
import {build} from 'esbuild'
import {fileURLToPath} from 'node:url'
import {applyDuocaiAction} from '../src/utils/duocaiReplay.js'
async function moduleAt(relative){
  const result=await build({entryPoints:[fileURLToPath(new URL(relative,import.meta.url))],bundle:true,platform:'node',format:'esm',write:false,logLevel:'silent'})
  return import('data:text/javascript;base64,'+Buffer.from(result.outputFiles[0].text).toString('base64'))
}
const {duocaiTileToMmcr,mmcrTileToSalasasa,salasasaTileToMmcr}=await moduleAt('../src/game2d/salasasa/gameAdapter.ts')
const {regionalSnapshot}=await moduleAt('../src/game2d/calc/regionalLocal.ts')
const {isRecordSilentTick}=await moduleAt('../src/game2d/replay/recordReplay.ts')
const {mmcrFaceId,tileFaceAssetUrl}=await moduleAt('../src/game2d/lib/tileFaceAsset.ts')

test('Duocai physical IDs round trip without changing the ordinary tile encoding',()=>{
  for(const tile of [1013,2013,3041,3046,5000,5039]){
    const encoded=duocaiTileToMmcr(tile)
    assert.equal(mmcrTileToSalasasa(encoded),tile)
    assert.equal(mmcrFaceId(encoded),tile)
  }
  assert.equal(salasasaTileToMmcr(13),0x43)
  assert.equal(tileFaceAssetUrl(5039,{baseUrl:'/'}),'/rulebooks/duocai/tiles/5039.png')
})

test('physical call and expansion preserve gold and silver identities in replay',()=>{
  const states=Array.from({length:4},()=>({hand:[],drawn:null,river:[],riverDrawn:[],melds:[]}))
  states[0].river=[13];states[0].riverDrawn=[false]
  states[1].hand=[1013,2013,23,24,25]
  applyDuocaiAction(states,{player:1,action:'peng',cut_from_player:0,tile:13,meld_index:0,meld_code:'k13',combination_mask:[0,1013,0,2013,1,13]},duocaiTileToMmcr)
  assert.deepEqual(states[1].hand,[23,24,25]);assert.deepEqual(states[0].river,[])
  assert.deepEqual(states[1].melds[0].duocai_physical,[1013,2013,13])
  states[1].drawn=5039
  applyDuocaiAction(states,{player:1,action:'jiagang',tile:5039,meld_index:0,meld_code:'g13',combination_mask:[0,1013,0,2013,1,13,0,5039]},duocaiTileToMmcr)
  assert.equal(states[1].drawn,null)
  assert.deepEqual(states[1].melds[0].duocai_physical,[1013,2013,13,5039])
})

test('regional worker bridges structural waits and on-demand point descriptions',()=>{
  const hand=[11,12,13,21,22,23,31,32,33,45,45,45,41]
  const request={rule:'duocai',melds:[],context:{variant:'guobiao',seat_wind:42,round_wind:41},hand,scope:'waiting'}
  assert.ok(regionalSnapshot(request).waiting_tiles.includes(41))
  const result=regionalSnapshot({...request,scope:'describe',hand:[...hand,41],win:41,cut:null})
  assert.equal(result.row.can_ron,true);assert.match(result.row.label,/点$/)
})

test('Duocai calls stay individually navigable while state and hints remain silent',()=>{
  assert.equal(isRecordSilentTick(['duocai','action',{action:'peng',player:1}]),false)
  assert.equal(isRecordSilentTick(['duocai','state',{}]),true)
  assert.equal(isRecordSilentTick(['duocai','waits',0,{}]),true)
})
