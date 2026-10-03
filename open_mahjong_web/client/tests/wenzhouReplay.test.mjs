import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile, access } from 'node:fs/promises'
import { resolve, relative, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { build } from 'esbuild'
import { wenzhouInfoAt, wenzhouWaitsAt, wenzhouWaitData, wenzhouLedgerRows, wenzhouLedgerEntries, parseWenzhouFan, wenzhouTileName } from '../src/utils/wenzhouReplay.js'
const workspace=resolve(fileURLToPath(new URL('../../..',import.meta.url)))
const stage=process.env.WENZHOU_STAGE_ROOT
const plugins=stage?[{name:'review-staged-integration',setup(b){b.onLoad({filter:/\.[cm]?[jt]s$/},async args=>{
 const file=join(stage,'preview',relative(workspace,args.path));try{await access(file)}catch{return null}
 return {contents:await readFile(file,'utf8'),loader:args.path.endsWith('.ts')?'ts':'js',resolveDir:resolve(args.path,'..')}
})}}]:[]
const compiled=await build({entryPoints:[fileURLToPath(new URL('../src/game2d/replay/recordReplay.ts',import.meta.url))],bundle:true,platform:'node',format:'esm',write:false,logLevel:'silent',plugins})
const {RecordReplay}=await import('data:text/javascript;base64,'+Buffer.from(compiled.outputFiles[0].text).toString('base64'))
const info={rule_version:'mil-wenzhou-2024-om1',caishen:11,indicator:11,indicator_index:47,white_natural:11,dice:[1,2,3,4],start_scores:[0,0,0,0],seat_to_original:[0,1,2,3]}
function record(ticks,hands,extras={},rule='wenzhou'){
 const round={seats:[0,1,2,3],tiles_list:[11,12,13,14,15,16],start_player_index:0,wenzhou:info,...Object.fromEntries(hands.map((h,i)=>['p'+i+'_tiles',h])),action_ticks:ticks,...extras}
 const final=ticks.findLast(t=>t[0]==='wenzhou'&&t[1]==='state')?.[3] || [0,0,0,0]
 const r=new RecordReplay({game_id:'wenzhou-physical-contract',created_at:'',rule,players:[0,1,2,3].map(i=>({user_id:100+i,username:String(i),score:final[round.seats[i]],rank:1,original_player_index:i})),record:{game_title:{rule},game_round:{round_index_1:round}}})
 return {r,round}
}
const physical=(r,n,seat)=>{const p=r.build(0,n).snapshot.seats[seat];return [...p.hand_tiles,...(p.drawn_tile==null?[]:[p.drawn_tile])]}
const full=(r,n)=>r.build(0,n).snapshot
const mm=t=>t>=11&&t<=19?0x40+t%10:t>=21&&t<=29?0x60+t%10:t>=31&&t<=39?0xc0+t%10:(0xa0+t%10)

test('十七张开局独立摸牌位，活墙扣除四张且杠尾取末张',()=>{
 const hand=[11,12,13,14,15,16,17,18,19,21,22,23,24,25,26,27,46]
 const {r}=record([['c',46,'F'],['gd',16]], [hand,[],[],[]])
 assert.equal(full(r,0).seats[0].hand_tiles.length,16)
 assert.equal(full(r,0).seats[0].drawn_tile,mm(46))
 assert.equal(physical(r,1,0).length,16)
 assert.equal(full(r,0).state.remaining_tile_count,2)
 assert.equal(full(r,2).state.remaining_tile_count,1)
 const wall=r.wallViewAt(0,2);assert.equal(wall.at(-1).consumed,true)
})

test('白板吃牌的逻辑副露与实体牌面独立，拖动回放重复稳定',()=>{
 const ticks=[['c',46,'F'],['cm',46,1,12,14],['wenzhou','meld',1,[0,12,1,46,0,14],'s13']]
 const {r}=record(ticks,[[46],[12,14,31,32],[],[]],{wenzhou:{...info,caishen:13,white_natural:13}})
 for(const node of [3,0,3,2,3])if(node===3){
  assert.deepEqual(physical(r,node,1),[mm(31),mm(32)])
  assert.deepEqual(full(r,node).seats[1].melds[0].physical_mask,[0,mm(12),1,mm(46),0,mm(14)])
  assert.equal(full(r,node).seats[1].melds[0].tile,mm(13))
  assert.equal(full(r,node).seats[0].discard_pile.length,0)
 }
})

test('白板暗杠移除四张实际白板且全部扣放',()=>{
 const {r}=record([['ag',11,'F',46,46,46,46,'gs',6,-2,-2,-2],['wenzhou','meld',0,[2,46,2,46,2,46,2,46],'G11']],[[46,46,46,46,31,32,33],[],[],[]])
 const player=full(r,2).seats[0]
 assert.deepEqual(physical(r,2,0),[mm(31),mm(32),mm(33)])
 assert.deepEqual(player.melds[0].concealed_face_down,[true,true,true,true])
 assert.equal(player.melds[0].physical_mask.filter((_,i)=>i%2).every(t=>t===mm(46)),true)
})

test('加杠使用实际白板并升级原先第一组碰，保留后来第二组碰',()=>{
 const ticks=[['c',46,'F'],['p',46,1,46,46],['wenzhou','meld',1,[1,46,0,46,0,46],'k11'],['reset',2],['c',45,'F'],['p',45,1,45,45],['wenzhou','meld',1,[1,45,0,45,0,45],'k45'],['jg',11,'F',46],['wenzhou','meld',1,[1,46,0,46,0,46,3,46],'g11']]
 const {r}=record(ticks,[[46],[46,46,46,45,45,31,32],[45],[]])
 const player=full(r,ticks.length).seats[1]
 assert.deepEqual(physical(r,ticks.length,1),[mm(31),mm(32)])
 assert.deepEqual(player.melds.map(m=>m.type),['kong','triplet'])
 assert.equal(player.melds[0].physical_mask.length,8)
 assert.equal(player.melds[1].tile,mm(45))
})

for(const kind of ['p','g'])test(`抢加杠改为${kind==='p'?'碰':'杠'}：原碰保留，临时牌河来源消费一次`,()=>{
 const ticks=[['reset',2],['c',46,'F'],['p',46,0,46,46],['wenzhou','meld',0,[1,46,0,46,0,46],'k11'],['wenzhou','kong_claim_source',0,46,false],[kind,46,1,...Array(kind==='p'?2:3).fill(46)],['wenzhou','meld',1,kind==='p'?[1,46,0,46,0,46]:[1,46,0,46,0,46,0,46],kind==='p'?'k11':'g11']]
 const {r}=record(ticks,[[46,46,46,31],kind==='p'?[46,46,32,33]:[46,46,46,32],[46],[]])
 assert.equal(full(r,5).seats[0].discard_pile.length,1)
 assert.equal(full(r,ticks.length).seats[0].discard_pile.length,0)
 assert.equal(full(r,ticks.length).seats[0].melds[0].type,'triplet')
 assert.deepEqual(physical(r,ticks.length,0),[mm(31)])
})

test('抢杠和来源从手牌消费一次且赢家取得实体白板',()=>{
 const ticks=[['wenzhou','win_source',1,0,'rob_kong',46,info],['hu_first',1,2,['WZ|win|x2|硬和'],[-2,6,-2,-2],46]]
 const {r}=record(ticks,[[31,46],[32],[],[]])
 for(const node of [2,0,1,2])if(node){assert.deepEqual(physical(r,node,0),[mm(31)]);assert.equal(full(r,node).seats[0].discard_pile.length,0)}
 assert.deepEqual(physical(r,2,1),[mm(32),mm(46)])
})

test('即时杠分加和牌财分一次，终局覆盖为最终减开局分并正确映射换位',()=>{
 const start=[10,20,30,40],final=[19,19,29,33]
 const metadata={...info,start_scores:start,caishen_counts:[1,0,0,0],caishen_changes:[3,-1,-1,-1],round_changes:[9,-1,-1,-7]}
 const ticks=[['ag',31,'F',31,31,31,31,'gs',6,-2,-2,-2],['hu_self',0,1,['WZ|win|x1|软和'],[3,1,1,-5]],['wenzhou','state',metadata,final]]
 const {r}=record(ticks,[[31,31,31,31],[],[],[]],{seats:[2,0,3,1],wenzhou:metadata})
 assert.deepEqual(full(r,0).seats.map(p=>p.score),start)
 assert.deepEqual(full(r,1).seats.map(p=>p.score),[16,18,28,38])
 assert.deepEqual(full(r,2).seats.map(p=>p.score),final)
 assert.deepEqual(full(r,3).seats.map(p=>p.score),final)
})

test('权威听牌逐座位更新与回退，无缓存时不给普通无癞提示',()=>{
 const old=[{tile:46,ron:true,self_draw:true,ron_multiplier:2,self_draw_multiplier:2}],updated=[{tile:31,ron:false,self_draw:true,ron_multiplier:0,self_draw_multiplier:4}]
 const round={wenzhou:info,wenzhou_waits:{1:{'12,13,14,15':old}},action_ticks:[['wenzhou','waits',1,{'12,13,14,15':updated}]]}
 assert.deepEqual(wenzhouWaitsAt(round,0,1,[15,12,14,13]),old)
 assert.deepEqual(wenzhouWaitsAt(round,1,1,[12,13,14,15]),updated)
 assert.deepEqual(wenzhouWaitsAt(round,1,0,[12,13,14,15]),[])
 assert.deepEqual(wenzhouWaitsAt(round,0,1,[12,13,14,15]),old)
 assert.equal(wenzhouWaitData({...round,wenzhou_waits:{}},0,1,[12,13,14,15],{seats:[]},x=>x),null)
})

test('提示按实体白板计数，财神指示牌占一张且显示倍数',()=>{
 const wait={tile:11,ron:true,self_draw:true,ron_multiplier:2,self_draw_multiplier:2}
 const round={wenzhou:info,wenzhou_waits:{0:{'12,13,14,15':[wait]}},action_ticks:[]}
 const result=wenzhouWaitData(round,0,0,[12,13,14,15],{seats:[{discard_pile:[],melds:[{physical_mask:[1,46,0,46,0,46]}]}]},x=>x)
 assert.equal(result.details[0].remaining_count,3)
 assert.equal(result.details[0].unit,'倍')
})

test('保存的翻财/骰子、计分文字及逐笔账目保留',()=>{
 const terminal={...info,caishen_counts:[1,0,2,0],caishen_changes:[0,-4,8,-4],round_changes:[1,-5,10,-6],ledger:[{kind:'ming',changes:[3,-1,-1,-1]}]}
 const round={wenzhou:info,action_ticks:[['wenzhou','state',terminal,[1,-5,10,-6]]]}
 assert.deepEqual(wenzhouInfoAt(round,0).dice,[1,2,3,4]);assert.equal(wenzhouInfoAt(round,1).indicator_index,47)
 assert.equal(wenzhouLedgerRows(terminal)[2].total,10)
 assert.match(wenzhouLedgerEntries(terminal)[0],/明杠.*东\+3/)
 assert.deepEqual(parseWenzhouFan('WZ|dealer|x4|庄家1连庄'),{name:'庄家1连庄',value:'×4'})
 assert.equal(wenzhouTileName(46),'白');assert.equal(wenzhouTileName(47),'发')
})

test('国标正常暗杠与默认十四张布局保持原状',()=>{
 const {r}=record([['ag',11,'F']],[[11,11,11,11,21,22,23],[],[],[]],{},'guobiao')
 assert.equal(full(r,1).seats[0].melds[0].concealed_face_down,undefined)
 assert.equal(full(r,1).seats[0].melds[0].physical_mask,undefined)
 assert.deepEqual(physical(r,1,0),[mm(21),mm(22),mm(23)])
})
