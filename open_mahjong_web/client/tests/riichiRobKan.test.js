import test from 'node:test'
import assert from 'node:assert/strict'
import { mjaiToSalasasaRecord, salasasaToMjaiRecord } from '../src/utils/recordConvert/mjaiRiichi.js'
import { RecordReplay } from '../src/game2d/replay/recordReplay.ts'
import { SalasasaGameAdapter, salasasaTileToMmcr } from '../src/game2d/salasasa/gameAdapter.ts'

const initial = [11,12,13,21,22,23,31,32,33,35,35,45,45]
test('立直成立前后回放点数守恒，MJAI 不改变下一摸牌的玩家', () => {
  const source = detail()
  source.record.game_round.round_index_1.action_ticks = [
    ['d',47],['c',47,'T','H'],['riichi',0,1],['d',46],['c',46,'T'],
  ]
  const replay = new RecordReplay(source)
  assert.equal(replay.build(0,2,0,true).snapshot.seats[0].score,25000)
  assert.equal(replay.build(0,3,0,true).snapshot.seats[0].score,24000)
  const events = salasasaToMjaiRecord(source.record).events
  assert.deepEqual(events.filter(e=>['reach','reach_accepted'].includes(e.type)).map(e=>e.type),['reach','reach_accepted'])
  assert.equal(events.filter(e=>e.type==='tsumo')[1].actor,1)
  assert.equal(events.find(e=>e.type==='end_game').scores[0],24000)
})

test('立直宣言牌被荣和时不收点棒，MJAI 往返也不伪造成立', () => {
  const source = detail()
  source.record.game_round.round_index_1.action_ticks = [
    ['d',47],['c',47,'T','H'],['hu_riichi',1,'hu_first',1,30,['役牌'],[-1000,1000,0,0],[],[],0,0,0,1000],
  ]
  const events = salasasaToMjaiRecord(source.record).events
  assert.ok(events.some(e=>e.type==='reach'))
  assert.ok(!events.some(e=>e.type==='reach_accepted'))
  const imported = mjaiToSalasasaRecord(events)
  assert.ok(!imported.game_round.round_index_1.action_ticks.some(t=>t[0]==='riichi'))
  assert.equal(new RecordReplay(source).build(0,3,0,true).snapshot.seats[0].score,24000)
})
function detail() {
  return { game_id: 'riichi-rob', rule: 'riichi', players: [], record: {
    game_title: { rule: 'riichi', starting_score: 25000 },
    game_round: { round_index_1: { current_round: 1, start_player_index: 0, seats: [0,1,2,3],
      p0_tiles: initial, p1_tiles: initial, p2_tiles: initial, p3_tiles: initial,
      action_ticks: [['d',35],['c',35,'T'],['p',35,1,35,35],['c',45,'F'],
        ...[2,3,0].flatMap(()=>[['d',11],['c',11,'T']]),
        ['d',305],['rk',1,305,1,'jiagang',[35,35,35]],
        ['hu_riichi',2,'hu_first',2,30,['枪杠'],[0,-2000,2000,0],[],[],1,0,0,2000]] }
    }
  } }
}

test('赤五在三门都归一到对应的五，不产生无效牌号', () => {
  for (const [red,plain] of [[105,15],[205,25],[305,35]]) assert.equal(salasasaTileToMmcr(red),salasasaTileToMmcr(plain))
})

test('MJAI 抢加杠往返保留赤牌和来源，未生成成功的加杠', () => {
  const events = salasasaToMjaiRecord(detail().record).events
  assert.equal(events.find(e=>e.type==='kakan').pai,'5sr')
  assert.equal(events.find(e=>e.type==='hora').pai,'5sr')
  assert.equal(events.find(e=>e.type==='hora').target,1)
  assert.equal(events.find(e=>e.type==='hora').hora_points,2000)
  const ticks = mjaiToSalasasaRecord(events).game_round.round_index_1.action_ticks
  assert.ok(ticks.some(t=>t[0]==='rk' && t[1]===1 && t[2]===305))
  assert.ok(!ticks.some(t=>t[0]==='jg'))
})

test('MJAI 国士抢暗杠保留声明类型', () => {
  const source=detail().record
  source.game_round.round_index_1.action_ticks=[['rk',0,47,1,'angang',[47,47,47,47]],['hu_riichi',1,'hu_first',13,25,['国士无双'],[-32000,32000,0,0],[],[],0,0,0,32000]]
  const events=salasasaToMjaiRecord(source).events
  assert.ok(events.some(e=>e.type==='ankan'))
  const ticks=mjaiToSalasasaRecord(events).game_round.round_index_1.action_ticks
  assert.deepEqual(ticks[0],['rk',0,47,0,'angang',[47,47,47,47]])
})

test('共享动作适配器 rob_kan 保留实际抢杠牌', () => {
  const adapter=new SalasasaGameAdapter(101)
  adapter.accept({type:'gamestate/guobiao/game_start',game_info:{room_id:1,gamestate_id:'rk',room_rule:'guobiao',current_round:1,action_tick:1,current_player_index:1,players_info:[0,1,2,3].map(seat=>({
    user_id:100+seat,username:`p${seat}`,player_index:seat,original_player_index:seat,score:25000,
    hand_tiles_count:seat===1?11:13,hand_tiles:seat===1?[11,12,13,21,22,23,31,32,33,45,305]:undefined,
    combination_tiles:seat===1?['k35']:[],combination_mask:seat===1?[[0,35,1,35,0,35]]:[],discard_tiles:[],huapai_list:[],tag_list:[],
  }))}})
  const accepted=adapter.accept({type:'gamestate/guobiao/do_action',do_action_info:{action_tick:2,action_list:['rob_kan'],action_player:1,cut_tile:305,is_mo_gang:true,silent:true}})
  assert.equal(accepted.events[0].event.kind,'rob_kong_tile')
  assert.equal(accepted.events[0].event.tile,salasasaTileToMmcr(35))
})

test('抢杠回放跳转前后只减少一张，不改碰为杠或添加弃牌', () => {
  const source=detail()
  // Seat 1 calls pon, discards, then receives its own next draw (intervening turns omitted via reset-free fixture).
  source.record.game_round.round_index_1.action_ticks=[['c',35,'F'],['p',35,1,35,35],['d',305],['rk',1,305,1,'jiagang',[35,35,35]]]
  const replay=new RecordReplay(source)
  const before=replay.build(0,3,1,true).snapshot
  const after=replay.build(0,4,1,true).snapshot
  const seat=s=>s.seats.find(p=>p.seat_index===1)
  assert.equal(seat(before).melds[0].type,'triplet')
  assert.equal(seat(after).melds[0].type,'triplet')
  assert.equal(seat(after).drawn_tile,null)
  assert.equal(seat(after).discard_pile.length,seat(before).discard_pile.length)
  assert.equal(replay.eventForStep(0,3,1,true)?.event?.kind,'rob_kong_tile')
})

test('日麻荣和与抢杠跳转会追加和牌张，错和不会补牌', () => {
  for (const cuohe of [false,true]) {
    const source=detail()
    const ticks=source.record.game_round.round_index_1.action_ticks
    if (cuohe) ticks.at(-1)[5]=['错和']
    const replay=new RecordReplay(source)
    const after=replay.build(0,ticks.length,2,true).snapshot.seats.find(p=>p.seat_index===2)
    assert.equal(after.hand_tile_count,13)
    assert.equal(after.drawn_tile,cuohe?null:salasasaTileToMmcr(35))
    const event=replay.eventForStep(0,ticks.length-1,2,true).event
    assert.equal(event.revealed_hand_tiles.length,cuohe?13:14)
  }
})

test('成功赤牌加杠保留赤牌在第四张或原碰中的位置', () => {
  for (const redInPon of [false,true]) {
    const source=detail().record
    source.game_round.round_index_1.action_ticks=[
      ['c',35,'F'],['p',35,1,redInPon?305:35,35],['c',45,'F'],
      ...[2,3,0].flatMap(()=>[['d',11],['c',11,'T']]),
      ['d',redInPon?35:305],['jg',35,'T',redInPon?35:305],['gd',47],
    ]
    const events=salasasaToMjaiRecord(source).events
    const kan=events.find(e=>e.type==='kakan')
    assert.equal(kan.actor,1)
    assert.equal(kan.pai,redInPon?'5s':'5sr')
    assert.deepEqual(kan.consumed,redInPon?['5s','5sr','5s']:['5s','5s','5s'])
    const imported=mjaiToSalasasaRecord(events)
    assert.deepEqual(imported.game_round.round_index_1.action_ticks.find(t=>t[0]==='jg'),['jg',35,'T',redInPon?35:305])
  }
})

test('换庄后 MJAI 玩家、手牌和点数按原座位映射，往返不丢立直扣点', () => {
  const source=detail()
  const first=source.record.game_round.round_index_1
  first.action_ticks=[['d',47],['c',47,'T','H'],['riichi',0,1],['ryuukyoku',[1,0,0,0],[3000,-1000,-1000,-1000],'exhaustive']]
  const second={...first,current_round:2,round_index:2,seats:[3,0,1,2],dealer_index:0,
    p0_tiles:Array(13).fill(21),p1_tiles:Array(13).fill(31),p2_tiles:Array(13).fill(41),p3_tiles:initial,
    riichi:{honba:1,riichi_sticks:1},
    action_ticks:[['d',46],['c',46,'T','H'],['riichi',0,0],['d',47],['hu_riichi',1,'hu_self',2,30,['门前清自摸和'],[-1000,4000,-500,-500],[],[],0,1,2,2000]]}
  source.record.game_round.round_index_2=second
  const events=salasasaToMjaiRecord(source.record).events
  const heads=events.filter(e=>e.type==='start_kyoku')
  assert.equal(heads[1].oya,1)
  assert.deepEqual(heads[1].scores,[27000,24000,24000,24000])
  assert.equal(heads[1].tehais[1][0],'1p')
  const secondEvents=events.slice(events.indexOf(heads[1]))
  assert.equal(secondEvents.find(e=>e.type==='reach').actor,1)
  assert.equal(secondEvents.find(e=>e.type==='hora').actor,2)
  assert.deepEqual(secondEvents.find(e=>e.type==='hora').deltas,[-500,-1000,4000,-500])
  assert.deepEqual(events.at(-1).scores,[26500,22000,28000,23500])
  const imported=mjaiToSalasasaRecord(events)
  assert.deepEqual(imported.game_round.round_index_2.seats,[3,0,1,2])
  assert.equal(imported.game_round.round_index_2.dealer_index,0)
  assert.deepEqual(salasasaToMjaiRecord(imported).events,events)
  const byOriginal=new RecordReplay(source).build(1,3,0,true).snapshot.seats
  assert.equal(byOriginal.find(p=>p.seat_index===3).score,27000)
  assert.equal(byOriginal.find(p=>p.seat_index===0).score,23000)
})

test('终局供托分配只在最后 end 生效，MJAI 终局保留实点', () => {
  const source=detail()
  source.record.game_round.round_index_1.action_ticks=[['d',47],['c',47,'T','H'],['riichi',0,1],['end']]
  source.record.game_title.riichi_final_scores=[25000,25000,25000,25000]
  const replay=new RecordReplay(source)
  assert.equal(replay.build(0,3,0,true).snapshot.seats[0].score,24000)
  assert.equal(replay.build(0,4,0,true).snapshot.seats[0].score,25000)
  const events=salasasaToMjaiRecord(source.record).events
  assert.deepEqual(events.at(-1).scores,[25000,25000,25000,25000])
  assert.deepEqual(mjaiToSalasasaRecord(events).game_title.riichi_final_scores,[25000,25000,25000,25000])
})
