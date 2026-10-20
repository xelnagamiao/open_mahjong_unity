import test from 'node:test'
import assert from 'node:assert/strict'
import fs from 'node:fs'
import {build} from 'esbuild'
import {fileURLToPath} from 'node:url'
import {buildShangraoRoomPayload,loadShangraoForm,SHANGRAO_OPTIONS} from '../src/utils/shangraoRoomConfig.js'
import {createEventRoomForm,buildEventRoomSettings} from '../src/utils/eventRoomSettings.js'
import {shangraoWallAt,shangraoInfoAt,shangraoVisibleIndicators} from '../src/utils/shangraoReplay.js'
async function moduleAt(relative){const result=await build({entryPoints:[fileURLToPath(new URL(relative,import.meta.url))],bundle:true,platform:'node',format:'esm',write:false,logLevel:'silent'});return import('data:text/javascript;base64,'+Buffer.from(result.outputFiles[0].text).toString('base64'))}
const {ShangraoLocal}=await moduleAt('../src/game2d/calc/shangraoLocal.ts')
const {regionalSnapshot}=await moduleAt('../src/game2d/calc/regionalLocal.ts')
const {RecordReplay}=await moduleAt('../src/game2d/replay/recordReplay.ts')
test('five optional switches round trip through venue and event room payloads',()=>{
  const form=loadShangraoForm({room_rule:'shangrao'})
  let packet=buildShangraoRoomPayload(form)
  assert.equal(packet.room_config.detailed_config.rule_version,'shangrao-v0.2')
  for(const key of Object.keys(SHANGRAO_OPTIONS)){assert.equal(packet.room_config.detailed_config[key],false);form['shangrao_'+key]=true}
  packet=buildShangraoRoomPayload(form)
  assert.deepEqual(buildEventRoomSettings(createEventRoomForm(packet)),packet)
  form.shangrao_qingyise='true';assert.throws(()=>buildShangraoRoomPayload(form))
})
test('all shared scoring fixtures match the server, including multi bao seven pairs',()=>{
  const fixtures=JSON.parse(fs.readFileSync(new URL('../../../open_mahjong_server/tests/fixtures/shangrao_calculator.json',import.meta.url),'utf8'))
  for(const [i,f] of fixtures.entries())assert.deepEqual(new ShangraoLocal(f.melds,{indicator:f.indicator,config:f.config,flying_bao:f.flying_bao??0,flying_seven_stars:f.flying_seven_stars??false}).score(f.hand,f.win,null,f.opening??false,f.after_kong??false),f.expected,'fixture '+i)
})
test('native worker contract supplies only allowed self draw points and discard previews',()=>{
  const hand=[11,11,13,13,15,15,21,21,23,23,25,25,47]
  const request={rule:'shangrao',hand,melds:[],context:{indicator:45}}
  const ready=regionalSnapshot(request)
  assert.equal(ready.waiting_tiles.length,34)
  assert.equal(ready.waiting_details[31].label,'仅自摸\n2分')
  assert.equal(regionalSnapshot({...request,context:{indicator:45,blocked:true}}).waiting_details[31].label,'未满足')
  const cuts=regionalSnapshot({...request,hand:[...hand,31]})
  assert.equal(cuts.waiting_details_by_discard[31][29].points,2)
  const first=new ShangraoLocal([],{indicator:45}).describe([...hand.slice(0,12),31,47],47)
  assert.equal(first.label,'未满足');assert.equal(first.points,0)
})
const mm=t=>t<20?0x40+t%10:t<30?0x60+t%10:t<40?0xc0+t%10:0xa0+t%10
function replay(ticks,extra={}){
  const round={seats:[2,3,0,1],tiles_list:[21,31,22,45,24],start_player_index:0,p0_tiles:[11,12,13],p1_tiles:[],p2_tiles:[],p3_tiles:[],shangrao:{indicator:45,bao:47,indicator_index:3,start_scores:[0,0,0,0],burnt_tiles:[]},action_ticks:ticks,...extra}
  return new RecordReplay({game_id:'shangrao-test',rule:'shangrao',players:[0,1,2,3].map(i=>({user_id:100+i,username:String(i),original_player_index:i,score:[5,-3,1,-3][round.seats[i]],rank:1})),record:{game_title:{rule:'shangrao'},game_round:{round_index_1:round}}})
}
test('wall retains indicator and consumes only legal upper lower pairs',()=>{
  const round={tiles_list:[21,31,22,45,24],shangrao:{indicator_index:3},action_ticks:[['gd',24],['gd',22],['shangrao','burnt',31,0],['d',21]]}
  assert.deepEqual(shangraoWallAt(round,1).remaining,[21,31,22,45])
  assert.deepEqual(shangraoWallAt(round,2).remaining,[21,45])
  assert.deepEqual(shangraoWallAt(round,4).remaining,[45]);assert.equal(shangraoWallAt(round,4).playable,0)
})

test('visible count includes burnt bao once and ordinary burnt tiles through the river',()=>{
  const visible=shangraoVisibleIndicators({indicator:45,bao:47,burnt_tiles:[31,47]})
  assert.deepEqual(visible,[45,47])
  assert.equal(4-1-visible.filter(tile=>tile===47).length,2)
})
test('payments, blue river and bao exception survive repeated seeks and seat rotation',()=>{
  const ticks=[['shangrao','payment',{changes:[-1,-1,3,-1]}],['gd',24],['gd',22],['shangrao','burnt',31,0],['shangrao','burnt',47,1],['hu_self',0,2,['SR|1|番|平和'],[6,-2,-2,-2],41],['shangrao','state',{indicator:45,bao:47,ledger:{changes:[5,-3,1,-3]}},[5,-3,1,-3]],['end']]
  const r=replay(ticks)
  for(const n of [8,0,8,5,8]){
    const snapshot=r.build(0,n).snapshot
    if(n===0){assert.deepEqual(snapshot.seats.map(p=>p.score),[0,0,0,0]);continue}
    assert.deepEqual(snapshot.seats[0].discard_pile,[mm(31)])
    assert.deepEqual(snapshot.seats[0].discard_blue_flags,[true])
    assert.deepEqual(snapshot.seats[1].discard_pile,[])
    if(n===8)assert.deepEqual(snapshot.seats.map(p=>p.score),[5,-3,1,-3])
  }
  const header=r.rounds[0].shangrao
  shangraoInfoAt(r.rounds[0],5);assert.deepEqual(header.burnt_tiles,[])
})
