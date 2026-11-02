import test from 'node:test'
import assert from 'node:assert/strict'
import {build} from 'esbuild'
import {fileURLToPath} from 'node:url'
async function moduleAt(relative){
  const result=await build({entryPoints:[fileURLToPath(new URL(relative,import.meta.url))],bundle:true,platform:'node',format:'esm',write:false,logLevel:'silent'})
  return import('data:text/javascript;base64,'+Buffer.from(result.outputFiles[0].text).toString('base64'))
}
const api=await moduleAt('../src/game2d/calc/duocaiLocal.ts')
const {allows,validPhysical}=await moduleAt('../src/game2d/calc/duocaiTiles.ts')
const {meldOptions}=await moduleAt('../src/game2d/calc/duocaiShapes.ts')
const complete=[11,12,13,21,22,23,31,32,33,45,45,45,41,41]

test('physical bonus identities and joker domains obey the original catalog',()=>{
  assert.equal(api.DOMAIN.size,91)
  assert.deepEqual(api.DOMAIN.get(5008),[11,15,19,21,25,29,31,35,39,41])
  assert.equal(validPhysical([13,13,1013,2013]),true)
  assert.equal(validPhysical([13,13,13]),false)
  assert.equal(validPhysical([5039,5039]),false)
  assert.equal(validPhysical(Array.from({length:8},(_,i)=>5000+i)),true)
  assert.equal(validPhysical(Array.from({length:9},(_,i)=>5000+i)),false)
  assert.equal(validPhysical([3055]),false)
  assert.equal(validPhysical([5000.5]),false)
})

test('special jokers retain their group-slot restrictions',()=>{
  assert.equal(allows(5030,11,'sequence',0,11,true,'chi'),true)
  assert.equal(allows(5030,11,'sequence',0,11),false)
  assert.equal(allows(5031,45,'kong',0,45,true,'peng'),true)
  assert.equal(allows(5031,45,'kong',0,45,true,'gang'),false)
  assert.equal(allows(5032,41,'seven_pair'),false)
  assert.equal(allows(5032,41,'pair'),true)
  assert.equal(allows(5033,13,'sequence',2,11),false)
  assert.equal(allows(5033,11,'sequence',0,11),true)
  assert.equal(allows(5034,13,'sequence',2,11),true)
  assert.equal(allows(5034,11,'sequence',0,11),false)
  assert.equal(allows(5035,17,'sequence',0,17),true)
  assert.equal(allows(5035,14,'sequence',0,14),false)
  assert.equal(allows(5036,41,'triplet',0,41,true,'peng'),false)
  assert.equal(meldOptions({physical:[11,12,5031],concealed:false,declared:'peng'}).length,0)
})

test('logical copies may exceed four without duplicating physical tiles',()=>{
  const hand=[11,11,11,11,5039,5032,21,22,23,31,32,33,45,45]
  assert.ok([...api.duocaiShapes(hand,[],'riichi')].some(s=>s.assignment.filter(t=>t===11).length===6))
  assert.equal(api.evaluateDuocai([11,11,22,22,33,33,44,44,45,45,46,46,47,5032],[],'riichi'),null)
})

test('the rulebook parity example uses one consistent interpretation',()=>{
  const hand=[5038,12,12,33,1033,2033,34,35,36,22,23,26,27,28]
  const result=api.evaluateDuocai(hand,[],'riichi',{riichi:1,ippatsu:true})
  assert.equal(result.valid,true)
  assert.equal(result.fan,2)
  assert.equal(result.shape.assignment[0],21)
})

test('pure-joker kong bonuses use yakuman limits and do not create a yaku',()=>{
  const meld={physical:[5000,5008,5020,5039],concealed:false,declared:'gang'}
  const hand=[45,45,45,22,23,24,36,37,41,41,38]
  const open=api.evaluateDuocai(hand,[meld],'riichi')
  assert.equal(open.valid,true);assert.equal(open.fan,13);assert.equal(open.yakuman,1)
  assert.equal(api.duocaiWinPoints(open,false),32000)
  const closed=api.evaluateDuocai(hand,[{...meld,concealed:true,declared:'angang'}],'riichi')
  assert.equal(closed.fan,26);assert.equal(api.duocaiWinPoints(closed,false),64000)
  const noYaku=api.evaluateDuocai([22,23,24,32,33,34,36,37,41,41,38],[meld],'riichi',{indicators:[37]})
  assert.equal(noYaku.fan,13);assert.equal(noYaku.valid,false)
})

test('valid zero-point hands remain legal and display zero points',()=>{
  const hand=[11,12,2013,22,23,24,34,35,36,36,37,41,41,38]
  const engine=new api.DuocaiLocal([],{variant:'riichi',riichi:1,honba:3,riichi_sticks:2})
  const result=api.evaluateDuocai(hand,[],'riichi',{riichi:1})
  assert.equal(result.valid,true);assert.equal(result.fan,0)
  assert.equal(api.duocaiWinPoints(result,false,false,3),0)
  const hint=engine.describe(hand,38)
  assert.equal(hint.can_ron,true);assert.equal(hint.points,0);assert.equal(hint.label,'0点')
})

test('discard previews include the proposed discard when scoring the last visible copy',()=>{
  const hand=[12,13,14,22,23,24,33,34,35,36,37,28,28,38]
  const engine=new api.DuocaiLocal([],{variant:'guobiao',visible_tiles:[38,38]})
  assert.equal(engine.describe(hand,38).ron_points,308)
  assert.equal(engine.describe(hand,38,38).ron_points,312)
  assert.equal(engine.describe(hand,38,38).remaining,1)
})

test('negative odd visible fan and hidden dora retain the rulebook parity semantics',()=>{
  const hand=[11,12,2013,22,23,24,34,35,36,36,2037,41,41,5037]
  const score=api.evaluateDuocai(hand,[],'riichi',{riichi:1})
  assert.equal(score.valid,true);assert.equal(score.visible,-1);assert.equal(score.fan,-1)
  const ura=api.evaluateDuocai(hand,[],'riichi',{riichi:1,ura_indicators:[37]})
  assert.equal(ura.valid,true);assert.equal(ura.visible,-1);assert.equal(ura.fan,0)
  assert.equal(api.duocaiWinPoints(ura,false),0)
})

test('hints distinguish conditions, minimum, self-draw-only and ordinary points',()=>{
  const gb=new api.DuocaiLocal([],{variant:'guobiao',seat_wind:42,round_wind:41})
  assert.equal(gb.describe([...complete.slice(0,-1),5038],5038).label,'未满足')
  assert.equal(gb.describe([...complete.slice(0,-1),5036],5036).label,'仅自摸\n375点')
  assert.equal(gb.describe(complete,41).label,'323点')
  const meld={physical:[11,12,13],concealed:false,declared:'chi'}
  const noYaku=new api.DuocaiLocal(['s12'],{variant:'riichi',physical_melds:[meld]})
  assert.equal(noYaku.describe([22,23,24,32,33,34,36,37,41,41,38],38).label,'未起和')
  const riichi=new api.DuocaiLocal([],{variant:'riichi'})
  assert.equal(riichi.describe([...complete.slice(0,-1),5037],5037).kind,'ron_only')
  assert.equal(riichi.describe([...complete.slice(0,-1),5038],5038).kind,'zimo')
})

test('discard furiten follows joker domains and ignores chow or pung jokers',()=>{
  const before=complete.slice(0,-1)
  assert.equal(api.duocaiDiscardFuriten(before,[],[5013],41),true)
  assert.equal(api.duocaiDiscardFuriten(before,[],[5030,5031],41),false)
  const normal=new api.DuocaiLocal([],{variant:'riichi',discards:[5013]})
  assert.equal(normal.describe(complete,41).kind,'zimo')
  const conditional=[5038,12,12,33,1033,2033,34,35,36,22,23,26,27]
  assert.equal(api.duocaiDiscardFuriten(conditional,[],[5038],28,{riichi:1,ippatsu:true}),true)
})

test('snapshot preserves authoritative legal cuts and input arrays',()=>{
  const hand=[...complete],original=[...hand]
  const snapshot=api.duocaiSnapshot({hand,melds:[],cuts:[41],context:{variant:'guobiao'}})
  assert.deepEqual(hand,original)
  assert.deepEqual(Object.keys(snapshot.waiting_by_discard),['41'])
  assert.ok(snapshot.waiting_by_discard[41].includes(5039))
  assert.equal(snapshot.waiting_details_by_discard[41][5036].kind,'zimo')
  assert.throws(()=>new api.DuocaiLocal([],{variant:'sichuan'}))
})

test('on-demand jobs separate structural waits from scoring and reject a displaced win slot',()=>{
  const context={variant:'guobiao'}
  const waits=api.duocaiHintRequest({scope:'waiting',hand:complete.slice(0,-1),melds:[],context})
  assert.deepEqual(Object.keys(waits),['waiting_tiles']);assert.ok(waits.waiting_tiles.includes(5036))
  const detail=api.duocaiHintRequest({scope:'describe',hand:[...complete.slice(0,-1),5036],melds:[],context,win:5036})
  assert.equal(detail.row.label,'仅自摸\n375点')
  assert.throws(()=>api.duocaiHintRequest({scope:'describe',hand:complete,melds:[],context,win:5039}))
})
