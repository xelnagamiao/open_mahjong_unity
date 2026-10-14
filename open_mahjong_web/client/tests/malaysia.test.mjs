import test from 'node:test'
import assert from 'node:assert/strict'
import {RecordReplay} from '../src/game2d/replay/recordReplay.ts'
import {salasasaTileToMmcr as encode, mmcrTileToSalasasa as decode} from '../src/game2d/salasasa/gameAdapter.ts'
import {mmcrFaceId, tileFaceAssetUrl} from '../src/game2d/lib/tileFaceAsset.ts'
import {relativeSeat, absoluteSeat} from '../src/game2d/lib/seatLayout.js'
import {loadMalaysiaForm, buildMalaysiaRoomPayload} from '../src/utils/malaysiaRoomConfig.js'
import {parseMalaysiaFan} from '../src/utils/malaysiaReplay.js'
import {buildPlayerStatsRows} from '../src/utils/statsDisplay.js'
import {createRequire} from 'node:module'
const {normalizeMalaysiaRoomConfig}=createRequire(import.meta.url)('../../server/utils/malaysiaRoomSettings.js')
const record = (round, scores=[0,0,0]) => new RecordReplay({
  game_id:'malaysia-test',created_at:'',rule:'malaysia',sub_rule:'malaysia/traditional84',
  players:scores.map((score,i)=>({user_id:100+i,username:`P${i}`,score,rank:i+1,original_player_index:i})),
  record:{game_title:{rule:'malaysia',player_count:3},game_round:{round_index_1:{seats:[0,1,2],p0_tiles:[],p1_tiles:[],p2_tiles:[],start_player_index:0,...round}}},
})

test('Malaysia special physical tiles round-trip independently from all standard tiles',()=>{
  const ids=[21,29,41,47,51,58,61,62,63,64,71,72,73,74,81]
  assert.equal(new Set(ids.map(encode)).size,ids.length)
  for(const tile of ids) {assert.equal(decode(encode(tile)),tile);assert.equal(mmcrFaceId(encode(tile)),tile)}
  assert.match(tileFaceAssetUrl(81,{baseUrl:'/',black:true}),/Regular\/81.svg$/)
})

test('Three seats occupy self/right/left and keep original identity under every perspective',()=>{
  for(let viewer=0;viewer<3;viewer++){
    assert.equal(relativeSeat(viewer,viewer,3),0)
    assert.equal(relativeSeat((viewer+1)%3,viewer,3),1)
    assert.equal(relativeSeat((viewer+2)%3,viewer,3),3)
    assert.equal(absoluteSeat(2,viewer,3),-1)
    for(let seat=0;seat<3;seat++)assert.equal(absoluteSeat(relativeSeat(seat,viewer,3),viewer,3),seat)
  }
  for(let viewer=0;viewer<4;viewer++)for(let seat=0;seat<4;seat++)assert.equal(relativeSeat(seat,viewer,4),(seat-viewer+4)%4)
  const replay=record({seats:[2,0,1],action_ticks:[['reset',2],['c',21,'T']]})
  const snap=replay.build(0,2,0).snapshot
  assert.equal(snap.seats.length,3);assert.equal(snap.viewer.seat_index,2);assert.equal(snap.state.current_player,0)
  assert.deepEqual(snap.hand_joker_tiles,[encode(81)])
})

test('Instant payments, rotated seats and final snapshot reconcile without double counting',()=>{
  const replay=record({seats:[2,0,1],malaysia:{start_scores:{0:0,1:0,2:0}},action_ticks:[
    ['malaysia','payment',[200,-100,-100],[]],['hu_self',1,200,['MY|x|5|测试'],[-400,800,-400],21],
    ['malaysia','state',{round_changes:{0:-500,1:-200,2:700}},[-200,700,-500]],['end'],
  ]},[-500,-200,700])
  assert.deepEqual(replay.build(0,0).snapshot.seats.map(s=>s.score),[0,0,0])
  assert.deepEqual(replay.roundScoreChangesByOriginal(0),[-500,-200,700])
  assert.deepEqual(replay.build(0,2).snapshot.seats.map(s=>s.score),[-200,700,-500])
  assert.deepEqual(replay.build(0,4).snapshot.seats.map(s=>s.score),[-200,700,-500])
})

test('Wild pung and redemption preserve the physical fly and newly drawn replacement',()=>{
  const replay=record({p0_tiles:[23],p1_tiles:[23,81,25],action_ticks:[
    ['reset',0],['c',23,'F'],['p',23,1,23,81],['d',23],['malaysia','redeem',1,23,0,2],
  ]})
  const before=replay.build(0,4).snapshot.seats[1],after=replay.build(0,5).snapshot.seats[1]
  assert.deepEqual(before.melds[0].physical_mask,[1,encode(23),0,encode(23),0,encode(81)])
  assert.deepEqual(after.melds[0].physical_mask,[1,encode(23),0,encode(23),0,encode(23)])
  assert.equal(after.drawn_tile,encode(81));assert.deepEqual(after.hand_tiles,[encode(25)])
})

test('Robbed kong consumes only the fourth held tile; flowers use the tail wall',()=>{
  const replay=record({p0_tiles:[45],p1_tiles:[21,21,22,22,23,23,24,24,25,25,26,26,45],tiles_list:[21,22,61],action_ticks:[
    ['malaysia','win_source',1,0,'rob_kong',45,false],['hu_first',1,200,[],[-400,600,-200],45],['bd',61,2],
  ]})
  const snap=replay.build(0,2).snapshot
  assert.deepEqual(snap.seats[0].hand_tiles,[]);assert.equal(snap.seats[1].drawn_tile,encode(45))
  assert.deepEqual(replay.remainingWallAt(0,3),[encode(21),encode(22)])
})

test('Committed added kong exposes four physical tiles and rotated claims skip the empty seat',()=>{
  const replay=record({p0_tiles:[46],p2_tiles:[46,46,25],action_ticks:[
    ['reset',0],['c',46,'F'],['p',46,2,46,46],['d',46],['jg',46,'T'],
  ]})
  const before=replay.build(0,3).snapshot.seats[2],after=replay.build(0,5).snapshot.seats[2]
  assert.equal(before.melds[0].meld_from_rel,3)
  assert.equal(after.melds[0].meld_from_rel,7)
  assert.equal(after.melds[0].type,'kong')
  assert.deepEqual(after.melds[0].physical_mask,[1,encode(46),3,encode(46),0,encode(46),0,encode(46)])
  assert.deepEqual(after.hand_tiles,[encode(25)])
  assert.equal(after.drawn_tile,null)
})

test('Room settings share an explicit fixed three-player rule and strict switches',()=>{
  const form=loadMalaysiaForm({});form.multiple_max=true
  const payload=buildMalaysiaRoomPayload(form)
  assert.equal(payload.room_rule,'malaysia')
  const normalized=normalizeMalaysiaRoomConfig(payload.room_config,Error)
  assert.deepEqual(normalized.detailed_config,{multiple_max:true})
  assert.throws(()=>normalizeMalaysiaRoomConfig({...normalized,detailed_config:{multiple_max:'true'}},Error))
  assert.throws(()=>buildMalaysiaRoomPayload({...form,sub_rule:'malaysia/four'}))
  assert.deepEqual(parseMalaysiaFan('MY|no_bonus|满番|无花'),{name:'无花',value:'满番'})
})

test('Malaysia statistics show real points without fan aggregates or a fourth place',()=>{
  const rows=buildPlayerStatsRows({rule:'malaysia',total_games:1,total_rounds:3,win_count:1,total_fan_score:300,total_fangchong_score:300,total_round_score:200,first_place_count:1})
  assert.equal(rows.find(r=>r.label==='局均点').value,'200.00')
  assert.ok(rows.some(r=>r.label==='平均和巡'))
  assert.ok(!rows.some(r=>r.label.includes('番')||['四位率','错和率'].includes(r.label)))
  assert.ok(buildPlayerStatsRows({rule:'guobiao'}).some(r=>r.label==='平均和番'))
})
