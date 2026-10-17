import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { fileURLToPath } from 'node:url'
import { createDefaultGuobiaoRoomConfig, buildGuobiaoRoomPayload } from '../src/utils/guobiaoRoomConfig.js'
import { huahunFanPoints } from '../src/utils/guobiaoHuahun.js'
import { recordHandJokers } from '../src/game2d/lib/handJokers.js'
async function moduleAt(path) {
  const built=await build({entryPoints:[fileURLToPath(new URL(path,import.meta.url))],bundle:true,write:false,platform:'node',format:'esm'})
  return import('data:text/javascript;base64,'+Buffer.from(built.outputFiles[0].text).toString('base64'))
}
const {SalasasaGameAdapter,salasasaTileToMmcr:tile}=await moduleAt('../src/game2d/salasasa/gameAdapter.ts')
const {RecordReplay}=await moduleAt('../src/game2d/replay/recordReplay.ts')
const wild=await moduleAt('../src/game2d/calc/guobiao/fourJokers.ts')
const sub='guobiao/huahun', seasons=[51,52,53,54]
test('花混默认加8，黑花递增开启，强制花牌和固定门槛；两种奖励可选',()=>{
  const defaults=createDefaultGuobiaoRoomConfig()
  assert.equal(defaults.no_joker_bonus,'add8');assert.equal(defaults.black_flower_progressive,true)
  for(const no_joker_bonus of ['add8','double','none']) for(const black_flower_progressive of [false,true]) {
    const {room_config:c}=buildGuobiaoRoomPayload({...defaults,sub_rule:sub,no_joker_bonus,black_flower_progressive,use_flowers:false,hepai_limit:1})
    assert.equal(c.no_joker_bonus,no_joker_bonus);assert.equal(c.black_flower_progressive,black_flower_progressive)
    assert.equal(c.use_flowers,true);assert.equal(c.hepai_limit,8);assert.equal(c.joker_multi_wait,false)
  }
  assert.throws(()=>buildGuobiaoRoomPayload({sub_rule:sub,duplicate_key:'ordinary'}))
  assert.equal(huahunFanPoints('花杠'),88);assert.equal(huahunFanPoints('无癞子翻倍*40'),40)
  assert.deepEqual(recordHandJokers({rule:'guobiao',sub_rule:sub}),seasons)
})
function adapter(hand) {
  const a=new SalasasaGameAdapter(100)
  a.accept({type:'gamestate/guobiao/game_start',game_info:{room_id:1,gamestate_id:'huahun-test',room_rule:'guobiao',sub_rule:sub,action_tick:1,current_player_index:0,current_round:1,tile_count:80,
    players_info:[0,1,2,3].map(i=>({user_id:100+i,username:String(i),player_index:i,original_player_index:i,hand_tiles:i===0?hand:[],hand_tiles_count:i===0?hand.length:13,combination_tiles:[],combination_mask:[],discard_tiles:[],huapai_list:[],score:0}))}})
  return a
}
test('客户端只允许当次摸切癞子；花杠候选、动作与补花区分',()=>{
  const a=adapter([11,12,13,14,15,16,17,18,19,...seasons])
  a.accept({type:'gamestate/guobiao/do_action',do_action_info:{action_tick:2,action_player:0,action_list:['deal_tile'],deal_tile:22}})
  const prompt=a.accept({type:'gamestate/guobiao/broadcast_hand_action',ask_hand_action_info:{action_tick:3,player_index:0,action_list:['angang','cut'],forbidden_cut_tiles:seasons,remaining_time:20,remain_tiles:79}})
  assert.ok(prompt.event.viewer.available_actions.some(x=>x.kind==='concealed_kong'&&x.tile===tile(51)))
  assert.ok(prompt.event.viewer.available_actions.every(x=>x.kind!=='flower'))
  for(const t of seasons) for(const drawn of [true,false]) assert.equal(a.encodeSceneInput({kind:'discard_tile',tile:tile(t),use_drawn_tile:drawn}),null)
  assert.equal(a.encodeSceneInput({kind:'discard_tile',tile:tile(22),use_drawn_tile:true}).TileId,22)
  const result=a.accept({type:'gamestate/guobiao/do_action',do_action_info:{action_tick:4,action_player:0,action_list:['angang'],combination_target:'G51',combination_mask:[0,51,0,52,0,53,0,54],is_mo_gang:false}})
  assert.deepEqual(result.events[0].event.physical_tiles,seasons.map(tile))
  assert.deepEqual(result.events[0].event.concealed_face_down,[false,false,false,false])
  const b=adapter([11,12,13,14,15,16,17,18,19,21,22,23,51])
  b.accept({type:'gamestate/guobiao/do_action',do_action_info:{action_tick:2,action_player:0,action_list:['deal_tile'],deal_tile:54}})
  b.accept({type:'gamestate/guobiao/broadcast_hand_action',ask_hand_action_info:{action_tick:3,player_index:0,action_list:['cut'],forbidden_cut_tiles:[51]}})
  assert.equal(b.encodeSceneInput({kind:'discard_tile',tile:tile(54),use_drawn_tile:true}).TileId,54)
  assert.equal(b.encodeSceneInput({kind:'discard_tile',tile:tile(54),use_drawn_tile:false}),null)
})
test('花杠牌谱正向与回退均保留四种季节，不丢失无关摸牌',()=>{
  for(const drawn of [22,54]) {
    const hand=[11,12,13,14,15,16,17,18,19,...seasons.filter(t=>t!==drawn)]
    if(drawn===54)hand.push(21)
    const removed=drawn===54?[54,51,52,53]:seasons
    const r={game_id:'huahun',rule:'guobiao',players:[0,1,2,3].map(i=>({user_id:100+i,username:String(i),score:0,original_player_index:i})),record:{game_title:{rule:'guobiao',sub_rule:sub},game_round:{round_index_1:{round_index:1,seats:[0,1,2,3],start_player_index:0,p0_tiles:hand,p1_tiles:[],p2_tiles:[],p3_tiles:[],tiles_list:[22,25,26],action_ticks:[['d',drawn],['ag',51,drawn===54?'T':'F',...removed],['gd',25]]}}}}
    const replay=new RecordReplay(r)
    for(const step of [2,3,2]) {
      const own=replay.build(0,step).snapshot.seats[0]
      assert.deepEqual(own.melds[0].physical_mask.filter((_,i)=>i%2),removed.map(tile))
      assert.deepEqual(own.melds[0].concealed_face_down,[false,false,false,false])
      assert.ok(!own.hand_tiles.some(t=>seasons.map(tile).includes(t)))
      if(drawn===22)assert.ok(own.hand_tiles.includes(tile(22)))
    }
    assert.equal(replay.build(0,0).snapshot.seats[0].melds.length,0)
  }
})
test('提示缓存隔离子规则、奖励、黑花开关，季节牌只有1张',async()=>{
  const hand=[11,12,13,21,22,23,31,32,33,44,44,44,45]
  wild.acceptFourJokersSnapshot({sub_rule:sub,hand,melds:[],waits:[45,51,52,53,54],discard_waits:{}})
  const ctx={hand,combinations:[],tips:true,subRule:sub,noJokerBonus:'double',blackFlowerProgressive:false,playerIndex:0,currentRound:1,hepaiLimit:8,flowerCount:2,seatDiscards:[[51],[],[],[]],seatCombinations:[[],[],[],[]]}
  let data=null
  for(let i=0;i<200&&!data;i++){data=wild.buildFourJokersWaitData(ctx,false);if(!data)await new Promise(r=>setTimeout(r,10))}
  assert.ok(data,'Local huahun wait job must finish')
  const d=data.details
  assert.equal(d.find(x=>x.tile===52).remaining_count,1);assert.equal(d.find(x=>x.tile===51).remaining_count,0)
  assert.ok(d.every(x=>x.four_jokers_request.no_joker_bonus==='double'&&!x.four_jokers_request.black_flower_progressive))
  const before=globalThis.fetch;let calls=0
  globalThis.fetch=async()=>{calls++;throw Error('Hints must not use HTTP')}
  try {
    const req=d[0].four_jokers_request
    for(const progressive of [false,true]) {
      const results=await Promise.all(['add8','double','none'].map(bonus=>wild.resolveFourJokersHint({...req,no_joker_bonus:bonus,black_flower_progressive:progressive})))
      assert.equal(results[0].score,results[2].score+8)
      assert.equal(results[1].score,results[2].score*2)
    }
    assert.equal(calls,0)
  }finally{globalThis.fetch=before}
})
