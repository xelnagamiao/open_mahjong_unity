import test from 'node:test'
import assert from 'node:assert/strict'
import {build} from 'esbuild'
const compiled=await build({entryPoints:[new URL('../src/game2d/replay/recordReplay.ts',import.meta.url).pathname.replace(/^\/([A-Za-z]:)/,'$1')],bundle:true,platform:'node',format:'esm',write:false,logLevel:'silent'})
const {RecordReplay}=await import('data:text/javascript;base64,'+Buffer.from(compiled.outputFiles[0].text).toString('base64'))
const hu=(seat)=>['hu_first',seat,3,[],[0,0,0,0]]
function replay(ticks,hands,rule='guizhou'){
 return new RecordReplay({game_id:'guizhou-node-contract',created_at:'',rule,players:[0,1,2,3].map(i=>({user_id:i+100,username:String(i),score:0,rank:1,original_player_index:i})),record:{game_title:{rule},game_round:{round_index_1:{seats:[0,1,2,3],tiles_list:[],start_player_index:0,...Object.fromEntries(hands.map((h,i)=>['p'+i+'_tiles',h])),action_ticks:ticks}}}})
}
const hand=(r,node,seat)=>{const p=r.build(0,node).snapshot.seats[seat];return [...p.hand_tiles,...(p.drawn_tile==null?[]:[p.drawn_tile])]}
test('贵州点和的来源节点只收走河牌，和牌节点再补赢家显示牌',()=>{
 const r=replay([['c',11,'F'],['guizhou','win_source',1,0,'discard',11,true,{}],hu(1)],[[11],[12],[],[]])
 assert.equal(r.build(0,1).snapshot.seats[0].discard_pile.length,1)
 assert.equal(r.build(0,2).snapshot.seats[0].discard_pile.length,0)
 assert.deepEqual(hand(r,2,1),[0x42])
 assert.deepEqual(hand(r,3,1),[0x42,0x41])
})
test('贵州多家抢杠的实体牌只移除一次，每家各在和牌节点补显示牌',()=>{
 const r=replay([['guizhou','win_source',1,0,'rob_kong',29,false,{}],hu(1),['guizhou','win_source',2,0,'rob_kong',29,true,{}],hu(2)],[[29],[12],[13],[]])
 assert.deepEqual(hand(r,1,0),[0x69]);assert.deepEqual(hand(r,1,1),[0x42])
 assert.deepEqual(hand(r,2,1),[0x42,0x69])
 assert.deepEqual(hand(r,3,0),[]);assert.deepEqual(hand(r,3,2),[0x43])
 assert.deepEqual(hand(r,4,2),[0x43,0x69])
 for(const node of [0,4,1,3,4])assert.equal(hand(r,node,0).length,node>=3?0:1)
})

for(let actor=0;actor<4;actor++)test(`贵州第 ${actor+1} 家起手暗杠按其他三家的首舍决定公示`,()=>{
 const hands=[0,1,2,3].map(i=>i===actor?[11,11,11,11,12,13,14,21,22,23,31,32,33]:[39])
 const ticks=Array.from({length:actor},(_,i)=>[['reset',i],['c',39,'F']]).flat()
 ticks.push(['reset',actor],['ag',11])
 const r=replay(ticks,hands),expected=actor===3?[true,false,false,true]:[true,true,true,true]
 for(const n of [ticks.length,0,ticks.length])if(n)assert.deepEqual(r.build(0,n).snapshot.seats[actor].melds[0].concealed_face_down,expected)
 assert.deepEqual(r.eventForStep(0,ticks.length-1).event.concealed_face_down,expected)
})

test('贵州首摸凑齐四张的暗杠直接亮中间两张',()=>{
 const r=replay([['reset',1],['d',11],['ag',11]],[[39],[11,11,11,12,13,14,21,22,23,31,32,33,39],[],[]])
 assert.deepEqual(r.build(0,3).snapshot.seats[1].melds[0].concealed_face_down,[true,false,false,true])
})

test('贵州起手暗杠的公示节点同步牌面，回退恢复扣放',()=>{
 const masks=[[[2,11,0,11,0,11,2,11]],[],[],[]]
 const r=replay([['ag',11],['guizhou','reveal_kongs',masks]],[[11,11,11,11,39],[],[],[]])
 for(const [node,expected] of [[1,true],[2,false],[1,true],[2,false]])
  assert.deepEqual(r.build(0,node).snapshot.seats[0].melds[0].concealed_face_down,[true,expected,expected,true])
})

test('其他规则保留原有暗杠展示',()=>{
 const r=replay([['ag',11]],[[11,11,11,11],[],[],[]],'guobiao')
 assert.equal(r.build(0,1).snapshot.seats[0].melds[0].concealed_face_down,undefined)
})
