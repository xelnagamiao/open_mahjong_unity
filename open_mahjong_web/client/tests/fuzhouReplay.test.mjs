import test from 'node:test'
import assert from 'node:assert/strict'
import {build} from 'esbuild'
import {fileURLToPath} from 'node:url'
import {parseFuzhouFan,fuzhouHintsAt,fuzhouWaitDataAt,fuzhouReserve} from '../src/utils/fuzhouReplay.js'
const bundle=await build({stdin:{contents:"export * from './replay/recordReplay';export * from './salasasa/gameAdapter'",resolveDir:fileURLToPath(new URL('../src/game2d',import.meta.url)),loader:'ts'},bundle:true,write:false,platform:'node',format:'esm'})
const {RecordReplay,mmcrTileToSalasasa:decode}=await import('data:text/javascript;base64,'+Buffer.from(bundle.outputFiles[0].text).toString('base64'))
const replay=(round,scores=[0,0,0,0])=>new RecordReplay({game_id:'fuzhou-test',created_at:'',rule:'fuzhou',players:scores.map((score,i)=>({score,user_id:101+i,original_player_index:i,rank:i+1,username:`P${i}`})),record:{game_title:{rule:'fuzhou'},game_round:{r:{seats:[0,1,2,3],p0_tiles:[],p1_tiles:[],p2_tiles:[],p3_tiles:[],start_player_index:0,...round}}}})

test('gold flip flowers and qiangjin transfer consume exactly their physical tiles',()=>{
 const r=replay({p0_tiles:[11],p1_tiles:[12],tiles_list:[21,39,41],fuzhou:{gold_tile:0},action_ticks:[
  ['fuzhou','kaijin_flower',0,41,{gold_tile:0}],['fuzhou','kaijin',0,39,{gold_tile:39}],
  ['fuzhou','qiangjin_take',1,39,{gold_tile:39,indicator_claimed:true}],
 ]})
 const s=r.build(0,3).snapshot
 assert.deepEqual(s.seats[0].flower_tiles.map(decode),[41]);assert.equal(decode(s.seats[1].drawn_tile),39)
 assert.deepEqual(r.remainingWallAt(0,3).map(decode),[21]);assert.deepEqual(s.hand_joker_tiles.map(decode),[39])
 assert.deepEqual(r.build(0,0).snapshot.hand_joker_tiles,[])
})

test('rob-kong keeps pung and consumes one fourth tile without recycling river',()=>{
 const hand=[11,12,13,14,15,16,21,22,23,31,32,33,37,37,37,29,29]
 const r=replay({p0_tiles:[29],p1_tiles:hand.filter((_,i)=>i!==16),action_ticks:[
  ['fuzhou','win_source',1,0,'rob_kong',29,{final_hands:{1:hand}},false,false],
  ['hu_self',1,55,['FZ|rob|15|抢杠'],[-55,165,-55,-55],29],
 ]},[-55,165,-55,-55])
 const s=r.build(0,2).snapshot
 assert.equal(s.seats[0].hand_tiles.length,0)
 assert.equal(s.seats[1].hand_tiles.length,16);assert.equal(decode(s.seats[1].drawn_tile),29)
 assert.deepEqual(s.seats.map(p=>p.score),[-55,165,-55,-55])
})

test('forced three-gold hand is not shortened or given an invented winning tile',()=>{
 const hand=[11,13,15,17,19,21,23,25,27,29,31,33,35,39,39,39]
 const r=replay({p1_tiles:hand,action_ticks:[['fuzhou','win_source',1,-1,'self_draw',0,{final_hands:{1:hand}},true,false],['hu_self',1,43,[],[-43,129,-43,-43],0]]},[-43,129,-43,-43])
 const s=r.build(0,2).snapshot.seats[1]
 assert.deepEqual(s.hand_tiles.map(decode),hand);assert.equal(s.drawn_tile,null)
})

test('sea and double-youjin reset the draw actor rather than advancing automatically',()=>{
 const r=replay({p0_tiles:[11],p1_tiles:[12],tiles_list:[21,22,23],action_ticks:[['reset',1],['d',21],['reset',1],['d',22],['reset',3],['d',23]]})
 const s=r.build(0,6).snapshot
 assert.deepEqual(s.seats[1].hand_tiles.map(decode),[12,21]);assert.equal(decode(s.seats[1].drawn_tile),22)
 assert.equal(decode(s.seats[3].drawn_tile),23)
})

test('reserve counts only kongs and four identical honor flowers',()=>{
 assert.equal(fuzhouReserve([{melds:[{type:'kong',concealed:false},{type:'kong',concealed:true}],flowers:[41,41,41,41,51,52,53,54]}]),23)
 assert.deepEqual(parseFuzhouFan('FZ|base|10|底分'),{name:'底分',value:'10分'})
 assert.equal(parseFuzhouFan('MCR|10'),null)
})

test('v2 reveals the indicator at the front while flower replacements still use the tail',()=>{
 const info={rule_version:'fuzhou-user-20261015-v2',gold_tile:0}
 const r=replay({p0_tiles:[11],tiles_list:[41,39,21,22],fuzhou:info,action_ticks:[
  ['fuzhou','kaijin_flower',0,41,info],['fuzhou','kaijin',0,39,{...info,gold_tile:39}],
  ['reset',0],['bd',22],
 ]})
 assert.deepEqual(r.remainingWallAt(0,1).map(decode),[39,21,22])
 assert.deepEqual(r.remainingWallAt(0,2).map(decode),[21,22])
 assert.deepEqual(r.remainingWallAt(0,4).map(decode),[21])
 assert.deepEqual(r.wallViewAt(0,4).map(t=>t.consumed),[true,true,false,true])
 assert.deepEqual(r.wallViewAt(0,0).map(t=>t.consumed),[false,false,false,false])
 assert.deepEqual(r.build(0,2).snapshot.seats[0].flower_tiles.map(decode),[41])
})

test('only v2 seasonal flower sets increase the reserve',()=>{
 const states=[{melds:[],flowers:[51,52,53,54]},{melds:[],flowers:[55,56,57,58]},
  {melds:[{type:'kong',concealed:true}],flowers:[41,41,41,41]}]
 assert.equal(fuzhouReserve(states),22)
 assert.equal(fuzhouReserve(states,'fuzhou-user-20261014-v1'),22)
 assert.equal(fuzhouReserve(states,'fuzhou-user-20261015-v2'),26)
 states[1].flowers.pop()
 assert.equal(fuzhouReserve(states,'fuzhou-user-20261015-v2'),24)
})

test('hints reject stale hands and show eligibility without invented fan points',()=>{
 const hints={source_hand_tiles:[11,12,39,22,22],source_melds:['k15','k25','k35','k18'],waiting_by_discard:{39:[13,39]},waiting_details_by_discard:{39:{13:{kind:'self_only'},39:{kind:'self_only'}}}}
 const round={fuzhou:{gold_tile:39},action_ticks:[['fuzhou','hints',[hints,null,null,null]]]}
 assert.equal(fuzhouHintsAt(round,1,0,[11,12,39,23,23],hints.source_melds),null)
 assert.equal(fuzhouHintsAt(round,1,0,hints.source_hand_tiles,['k15']),null)
 const data=fuzhouWaitDataAt(round,1,0,[11,12,22,22],hints.source_melds,[39,39])
 assert.equal(data.type,'waits');assert.equal(data.details[1].remaining_count,1)
 assert.equal(data.details[0].label,'仅自摸');assert.equal(data.details[0].base_f,0)
})
