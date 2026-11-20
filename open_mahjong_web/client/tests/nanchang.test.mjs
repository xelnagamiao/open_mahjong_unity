import test from 'node:test'
import assert from 'node:assert/strict'
import {build} from 'esbuild'
import {fileURLToPath} from 'node:url'
import {loadNanchangForm,buildNanchangRoomPayload} from '../src/utils/nanchangRoomConfig.js'
import {parseNanchangFan,nanchangInfoAt,nanchangWaitsAt,nanchangWaitData,nanchangHint,nanchangLedgerRows} from '../src/utils/nanchangReplay.js'
const compiled=await build({entryPoints:[fileURLToPath(new URL('../src/game2d/replay/recordReplay.ts',import.meta.url))],bundle:true,platform:'node',format:'esm',write:false,logLevel:'silent'})
const {RecordReplay}=await import('data:text/javascript;base64,'+Buffer.from(compiled.outputFiles[0].text).toString('base64'))
const localCompiled=await build({entryPoints:[fileURLToPath(new URL('../src/game2d/calc/nanchangLocal.ts',import.meta.url))],bundle:true,platform:'node',format:'esm',write:false,logLevel:'silent'})
const {NanchangLocal}=await import('data:text/javascript;base64,'+Buffer.from(localCompiled.outputFiles[0].text).toString('base64'))

test('video third point original examples do not borrow another decomposition',()=>{
  // BV1D6Wu6gEWZ at 26:37, transcribed from the slide.
  for(const [before,melds,indicator,win] of [
    [[29,29,29,21,21,14,17,41,42,43,45,46,47],[],29,11],
    [[46,46,16,16,16,31,33],['k43','k24'],46,33],
  ]){
    const solver=new NanchangLocal(melds,{indicator,index:1,declared_jingdiao:true})
    assert.equal(solver.describe([...before,win],win).self_draw_points,31)
  }
})

test('declared jingdiao quotes use the same decomposition as the server',()=>{
  const cases=[
    [[11,11,11,11,12,12,12,41,42,43,44,45,46],47,11,47],
    [[11,11,11,13,13,22,23,31,31,45,47,47,47],13,45,47],
    [[11,11,11,21,21,21,31,31,31,41,41,41,45],22,45,79],
    [[11,11,12,12,21,21,22,22,31,31,32,32,45],45,45,158],
    [[11,12,13,21,22,23,31,32,33,45,45,45,47],47,45,62],
  ]
  for(const [before,win,indicator,points] of cases){
    const solver=new NanchangLocal([],{indicator,index:1,declared_jingdiao:true})
    assert.equal(solver.describe([...before,win],win).self_draw_points,points)
  }
})
const mm=t=>t>=11&&t<=19?0x40+t%10:t>=21&&t<=29?0x60+t%10:t>=31&&t<=39?0xc0+t%10:(0xa0+t%10)
function replay(ticks,hands,extra={}) {
  const round={seats:[0,1,2,3],tiles_list:[11,12,13,14,15,16],start_player_index:0,nanchang:{indicator:45,secondary:47,start_scores:[0,0,0,0]},...Object.fromEntries(hands.map((h,i)=>['p'+i+'_tiles',h])),action_ticks:ticks,...extra}
  const scores=ticks.findLast(t=>t[0]==='nanchang'&&t[1]==='state')?.[3] || [0,0,0,0]
  return new RecordReplay({game_id:'nanchang-fixture',rule:'nanchang',players:[0,1,2,3].map(i=>({user_id:101+i,username:String(i),score:scores[round.seats[i]],rank:1,original_player_index:i})),record:{game_title:{rule:'nanchang'},game_round:{round_index_1:round}}})
}
test('singles room contract keeps one supported version',()=>{
  const form=loadNanchangForm({room_rule:'nanchang',room_name:'南昌测试'})
  const payload=buildNanchangRoomPayload(form)
  assert.equal(payload.room_config.sub_rule,'nanchang/mil2026')
  assert.deepEqual(payload.room_config.detailed_config,{rule_version:'mil-nanchang-2026'})
  form.sub_rule='nanchang/doubles';assert.throws(()=>buildNanchangRoomPayload(form))
})
test('hint labels prioritize restrictions over numeric quotes',()=>{
  for(const [status,label,kind] of [['unmet','未满足','wuyi'],['below_minimum','未起和','wuyi'],['self_draw_only','仅自摸','zimo'],['allowed','12分','dianhe']])
    assert.deepEqual(nanchangHint({status,points:12,self_draw:true,self_draw_points:24}),{label,kind})
  assert.equal(nanchangHint({status:'allowed',points:16,ron_points:{0:16,1:12,2:0}}).label,'12～16分')
  assert.equal(nanchangHint({status:'allowed',points:15,points_min:15,points_max:46}).label,'15～46分')
})
test('wait cache does not look ahead or count the indicator twice',()=>{
  const hand=[11,12,13,21,22,23,31,32,33,41,41,41,45],key=hand.join(',')
  const row={tile:45,status:'allowed',points:12,self_draw:true,self_draw_points:24}
  const round={nanchang:{indicator:45},nanchang_hints:{0:{[key]:[row]}},action_ticks:[['nanchang','waits',0,{}]]}
  assert.equal(nanchangWaitsAt(round,0,0,[...hand].reverse()).length,1)
  assert.equal(nanchangWaitsAt(round,1,0,hand).length,0)
  const out=nanchangWaitData(round,0,0,hand,{seats:[]},v=>v)
  assert.equal(out.details[0].remaining_count,3)
  assert.equal(out.details[0].label,'12分')
})
test('jingdiao replay declaration does not mutate the round header',()=>{
  const initial={declared_jingdiao:[false,false,false,false]}
  const round={nanchang:initial,action_ticks:[['nanchang','jingdiao',1]]}
  assert.equal(nanchangInfoAt(round,1).declared_jingdiao[1],true)
  assert.equal(initial.declared_jingdiao[1],false)
})
test('honor chow keeps exact physical tiles and can seek repeatedly',()=>{
  const ticks=[['c',41,'F'],['cm',41,1,42,44],['nanchang','meld',1,[1,41,0,42,0,44],'s414244']]
  const r=replay(ticks,[[41],[42,44,11,12],[],[]])
  for(const n of [3,0,3,2,3]) {
    const p=r.build(0,n).snapshot.seats[1]
    if(n===3){assert.deepEqual(p.hand_tiles,[mm(11),mm(12)]);assert.deepEqual(p.melds[0].physical_mask,[1,mm(41),0,mm(42),0,mm(44)])}
  }
})
test('settlement records include jing and kong scores after seat rotation',()=>{
  const r=replay([['hu_self',2,12,['NC|1|番|德国'],[-4,-4,12,-4],41],['nanchang','extras','gs',3,-1,-1,-1],['nanchang','state',{},[-1,-5,11,-5]],['end']],[[11],[22],[41],[33]],{seats:[2,3,0,1]})
  const full=r.build(0,4).snapshot
  assert.deepEqual(full.seats.map(p=>p.score),[-1,-5,11,-5])
  assert.deepEqual(r.build(0,0).snapshot.seats.map(p=>p.score),[0,0,0,0])
})
test('all remaining wall tiles are live, kong takes upper then lower',()=>{
  const r=replay([['gd',15],['gd',16]],[[11],[22],[33],[44]])
  assert.equal(r.build(0,0).snapshot.state.remaining_tile_count,6)
  const wall=r.wallViewAt(0,2)
  assert.equal(wall.at(-1).consumed,true);assert.equal(wall.at(-2).consumed,true)
})
test('fixed points and three-ledger breakdown retain units',()=>{
  assert.deepEqual(parseNanchangFan('NC|40|分|天和精吊'),{name:'天和精吊',value:'40分'})
  const rows=nanchangLedgerRows({seat_to_original:[2,3,0,1],ledger:{jing_changes:[3,-1,-1,-1],kong_changes:[-2,6,-2,-2],win_changes:[-4,-4,12,-4],changes:[-3,1,9,-7]}})
  assert.deepEqual(rows[0],{seat:0,original:2,jing:3,kong:-2,win:-4,total:-3})
})
