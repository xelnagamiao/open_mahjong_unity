import test from 'node:test'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { build } from 'esbuild'
import { loadJiujiangForm, buildJiujiangRoomPayload, JIUJIANG_CONFIG } from '../src/utils/jiujiangRoomConfig.js'
import { createEventRoomForm, buildEventRoomSettings, eventRoomSettingsRows, eventRoomSettingsSummary } from '../src/utils/eventRoomSettings.js'
import { jiujiangHintsAt, jiujiangWaitDataAt, parseJiujiangFan, jiujiangPaymentsAt, jiujiangKnownTiles } from '../src/utils/jiujiangReplay.js'
const require=createRequire(import.meta.url)
const { normalizeJiujiangRoomConfig }=require('../../server/utils/jiujiangRoomSettings.js')
const { readRoomSettings, applyRoomSettingsChange }=require('../../server/utils/eventRoomSettings.js')
class SettingsError extends Error { constructor(code,message) {super(message);this.code=code} }
const flags=['tips','count_tips','pointer_tips','tourist_limit','allow_spectator']

test('五种开关、四种副数、计时边界经赛事预设与建房配置完整往返',()=>{
  for(let bits=0;bits<32;bits++) for(const rounds of [1,2,3,4]) for(const [timer,step] of [[0,0],[20,5],[1000,100]]) {
    const form=loadJiujiangForm({room_rule:'jiujiang'})
    flags.forEach((key,i)=>{form[key]=Boolean(bits&(1<<i))})
    Object.assign(form,{game_round:rounds,round_timer:timer,step_timer:step})
    const payload=buildJiujiangRoomPayload(form)
    const normalized=normalizeJiujiangRoomConfig(payload.room_config,SettingsError)
    assert.equal(normalized.use_flowers,true);assert.equal(normalized.hepai_limit,1)
    assert.deepEqual(normalized.detailed_config,JIUJIANG_CONFIG)
    for(const kind of ['manual','preset-create']) {
      const initial=readRoomSettings(null)
      const saved=applyRoomSettingsChange(initial,{kind,userId:101,eventStatus:'active',body:{revision:initial.revision,...payload,name:'九江'}}).settings
      const restored=createEventRoomForm(kind==='manual'?saved.manual:saved.presets[0])
      for(const key of [...flags,'game_round','round_timer','step_timer']) assert.equal(restored[key],form[key],key)
      assert.deepEqual(buildEventRoomSettings(restored).room_config.detailed_config,JIUJIANG_CONFIG)
      assert.deepEqual(readRoomSettings(saved),saved)
    }
  }
})

test('固定规则及类型不可绕过',()=>{
  for(const bad of [null,[],{hepai_limit:0},{hepai_limit:true},{use_flowers:false},{use_flowers:1},{sub_rule:'hongzhong/mil2024'},
    {unknown:true},{detailed_config:null},{detailed_config:{rule_version:'other'}},{detailed_config:{fan_cap:4}},
    ...flags.map(key=>({[key]:'true'})),...['open_cuohe','tactical_call','claim_protection','tian_di_ren_he'].map(key=>({[key]:true})),
    {game_round:5},{game_round:0},{game_round:1.1},{round_timer:-1},{step_timer:101},{room_name:3},{duplicate_key:'x'}])
    assert.throws(()=>normalizeJiujiangRoomConfig(bad,SettingsError),JSON.stringify(bad))
  for(const changes of [{tips:'true'},{game_round:'1'},{sub_rule:'other'},{duplicate_key:'x'}])
    assert.throws(()=>buildJiujiangRoomPayload({...loadJiujiangForm({}),...changes}))
  const settings=buildJiujiangRoomPayload(loadJiujiangForm({room_rule:'jiujiang'},{game_round:2}))
  assert.equal(eventRoomSettingsSummary(settings,{jiujiang:'九江麻将'}),'九江麻将 · 8副')
  assert.equal(eventRoomSettingsRows(settings).find(r=>r.label==='起胡分').value,'1分（含花分）')
})

const hand=[11,12,13,21,22,23,31,32,33,17,18,19,25]
const hints=(points=2,kind='dianhe')=>({source_hand_tiles:hand,source_melds:[],waiting_tiles:[25],waiting_details:{25:{points,kind}}})
test('稳定提示仅x分，保持不同和法的颜色类别，不泄露其他座位或未来快照',()=>{
  for(const kind of ['dianhe','zimo','wuyi']) for(const points of [1,2,10,36]) {
    const round={action_ticks:[['jiujiang','hints',0,hints(points,kind)]]}
    assert.equal(jiujiangHintsAt(round,0,0,hand,[]),null)
    assert.equal(jiujiangHintsAt(round,1,1,hand,[]),null)
    assert.equal(jiujiangHintsAt(round,1,0,[...hand.slice(1),29],[]),null)
    assert.equal(jiujiangHintsAt(round,1,0,hand,['k45']),null)
    const row=jiujiangWaitDataAt(round,1,0,hand,[],[25,25]).details[0]
    assert.equal(row.label,`${points}分`);assert.equal(row.kind,kind);assert.equal(row.remaining_count,2)
  }
  assert.equal(jiujiangHintsAt(null,0,-1,hand,[]),null)
})

test('切牌预览与切后快照共用数字，回退和不匹配时不遗留',()=>{
  const cache={source_hand_tiles:[...hand,29],source_melds:[],waiting_by_discard:{29:[25]},waiting_details_by_discard:{29:{25:{points:2,kind:'dianhe'}}}}
  const round={action_ticks:[['jiujiang','hints',0,cache]]}
  assert.equal(jiujiangWaitDataAt(round,1,0,[...hand,29],[],[]).details[0].adds[0].label,'2分')
  assert.equal(jiujiangWaitDataAt(round,1,0,hand,[],[]).details[0].label,'2分')
  assert.equal(jiujiangWaitDataAt(round,0,0,hand,[],[]),null)
  assert.deepEqual(parseJiujiangFan('抢杠胡（1分）'),{name:'抢杠胡',value:'1分'})
})

test('公开张数不读他人暗杠，逐家赔付只在结算节点出现',()=>{
  const snapshot={viewer:{seat_index:0},seats:[{seat_index:0,hand_tiles:[25],melds:[]},
    {seat_index:1,hand_tiles:[25,25],melds:[{tile:25,type:'kong',concealed:true,concealed_face_down:[true,true,true,true]}]}]}
  assert.deepEqual(jiujiangKnownTiles(snapshot,t=>t),[25])
  const rows=[{payer:0,winner:1,base:7,unready_flowers:5,declared_penalty:10,dealer_multiplier:2,points:44}]
  const round={action_ticks:[['jiujiang','payments',rows]]}
  assert.deepEqual(jiujiangPaymentsAt(round,0),[]);assert.deepEqual(jiujiangPaymentsAt(round,1),rows)
})

const compiled=await build({entryPoints:[new URL('../src/game2d/replay/recordReplay.ts',import.meta.url).pathname.replace(/^\/([A-Za-z]:)/,'$1')],bundle:true,platform:'node',format:'esm',write:false,logLevel:'silent'})
const {RecordReplay,isRecordSilentTick}=await import(`data:text/javascript;base64,${Buffer.from(compiled.outputFiles[0].text).toString('base64')}`)
function fixture(ticks,tiles=[11,11,11,11,25],wall=[21,22,23,24],finalScores=[0,0,0,0]) {
  return {game_id:'jj-test',rule:'jiujiang',players:[0,1,2,3].map(i=>({score:finalScores[i],original_player_index:i,user_id:101+i,username:String(i)})),
    record:{game_title:{rule:'jiujiang',sub_rule:'jiujiang/standard'},game_round:{round_index_1:{round_index:1,seats:[0,1,2,3],start_player_index:0,
      tiles_list:wall,p0_tiles:tiles,p1_tiles:[],p2_tiles:[],p3_tiles:[],action_ticks:ticks}}}}
}
test('暗杠全背、局终揭示、跳回重藏，旧手牌杠后收起摸牌槽',()=>{
  const replay=new RecordReplay(fixture([['ag',11,'F'],['gd',23],['jiujiang','reveal_kongs',[[[0,11,0,11,0,11,0,11]],[],[],[]]],['liuju']]))
  for(const node of [1,3,1,4]) {
    const seat=replay.build(0,node).snapshot.seats[0]
    assert.deepEqual(seat.melds[0].concealed_face_down,Array(4).fill(node<3))
    if(node===1) assert.equal(seat.drawn_tile,null)
  }
  assert.equal(isRecordSilentTick(['jiujiang','hints',0,{}]),true)
  assert.equal(replay.remainingWallAt(0,2).length,3)
})
test('发财补出与弃出分别进入花区和牌河，补牌从末墩上张开始',()=>{
  const replaced=new RecordReplay(fixture([['bh',47,0,'T'],['bd',23,0]],[11,47]))
  assert.equal(replaced.build(0,2).snapshot.seats[0].flower_tiles.length,1)
  assert.equal(replaced.remainingWallAt(0,2).length,3)
  const discarded=new RecordReplay(fixture([['c',47,'T']],[11,47]))
  assert.equal(discarded.build(0,1).snapshot.seats[0].flower_tiles.length,0)
  assert.equal(discarded.build(0,1).snapshot.seats[0].discard_pile.length,1)
  const otherDetail=fixture([['c',11,'T'],['d',47],['bh',47,1,'T'],['bd',23,1]],[12,11])
  otherDetail.record.game_round.round_index_1.p1_tiles=[21]
  const other=new RecordReplay(otherDetail).build(0,4).snapshot.seats[1]
  assert.equal(other.has_drawn_tile,true)
  assert.equal(other.hand_tiles.length,1)
  assert.equal(other.flower_tiles.length,1)
})

test('抢杠撤销加杠而不移走其他弃牌，跳回可恢复加杠节点',()=>{
  const detail=fixture([
    ['c',25,'T'],['p',25,1,25,25],['c',19,'F'],
    ['d',11],['c',11,'T'],['d',12],['c',12,'T'],
    ['d',13],['c',13,'T'],['d',25],['jg',25,'T'],
    ['hu_first',2,3,['抢杠胡（1分）'],[0,-3,3,0],25,0,1,0],
  ],[17,25],[],[0,-3,3,0])
  const round=detail.record.game_round.round_index_1
  round.p1_tiles=[25,25,19,18];round.p2_tiles=[21];round.p3_tiles=[22]
  const replay=new RecordReplay(detail)
  for(const [node,type] of [[11,'kong'],[12,'triplet'],[11,'kong'],[12,'triplet']]) {
    const snapshot=replay.build(0,node).snapshot
    assert.equal(snapshot.seats[1].melds[0].type,type)
    assert.equal(snapshot.seats[0].discard_pile.length,1)
    assert.equal(snapshot.seats[1].discard_pile.length,1)
    assert.equal(snapshot.seats[2].has_drawn_tile,node===12)
    assert.equal(snapshot.seats[1].score,node===12?-3:0)
  }
})

test('普通放铳把和牌张送到赢家，自摸保留自己的摸牌张',()=>{
  const ron=new RecordReplay(fixture([['c',25,'T'],['hu_first',1,2,['独听（1分）'],[-4,4,0,0],25,0,0,1]],[17,25],[],[-4,4,0,0]))
  assert.equal(ron.build(0,1).snapshot.seats[0].discard_pile.length,1)
  const won=ron.build(0,2).snapshot
  assert.equal(won.seats[0].discard_pile.length,0);assert.equal(won.seats[1].has_drawn_tile,true)
  const self=new RecordReplay(fixture([['hu_self',0,3,['自摸（1分）'],[18,-6,-6,-6],25]],[17,25],[],[18,-6,-6,-6]))
  assert.equal(self.build(0,1).snapshot.seats[0].has_drawn_tile,true)
  assert.equal(self.build(0,1).snapshot.seats[0].score,18)
})
