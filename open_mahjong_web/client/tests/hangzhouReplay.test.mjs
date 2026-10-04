import test from 'node:test'
import assert from 'node:assert/strict'
import { build } from 'esbuild'
import { fileURLToPath } from 'node:url'
import { parseHangzhouFan, hangzhouInfoAt, hangzhouWaitDataAt, hangzhouKnownTiles, hangzhouPaymentReason, hangzhouNormalDrawableWallIndices } from '../src/utils/hangzhouReplay.js'
const compiled=await build({entryPoints:[fileURLToPath(new URL('../src/game2d/replay/recordReplay.ts',import.meta.url))],bundle:true,platform:'node',format:'esm',write:false,logLevel:'silent'})
const {RecordReplay}=await import('data:text/javascript;base64,'+Buffer.from(compiled.outputFiles[0].text).toString('base64'))
function replay(ticks,rule='hangzhou',wall=[11,12,13,14,15,16],titleRule=rule) { return new RecordReplay({game_id:'hangzhou-client-test',created_at:'',rule,players:[0,1,2,3].map(i=>({user_id:100+i,username:String(i),score:[6,-2,-2,-2][i],rank:i+1,original_player_index:i})),record:{game_title:{rule:titleRule,sub_rule:titleRule+'/mil2025'},game_round:{round_index_1:{hangzhou:{dealer_streak:1},seats:[0,1,2,3],tiles_list:wall,start_player_index:0,p0_tiles:[11,12,13,21,22,23,31,32,33,41,41,46,46],p1_tiles:[],p2_tiles:[],p3_tiles:[],action_ticks:ticks}}}}) }
test('杭州先暗弃上牌再摸下牌，节点重放和墙索引一致',()=>{
  const r=replay([['hangzhou','tail_burn',0,15],['gd',16],['d',11]])
  assert.equal(r.remainingWallAt(0,1).length,5);assert.equal(r.remainingWallAt(0,2).length,4)
  for(const node of [3,0,2,1,3]) {
    assert.equal(r.remainingWallAt(0,node).length,6-node)
    assert.equal(r.build(0,node).snapshot.state.remaining_tile_count,0)
    assert.equal(r.wallViewAt(0,node).filter(t=>t.consumed).length,node)
  }
  assert.deepEqual(r.wallViewAt(0,2).map(t=>t.consumed),[false,false,false,false,true,true])
})
test('十风和仍保留13张暗手与第十张弃牌，回放不生成摸牌',()=>{
  const r=replay([['d',41],['c',41,'T'],['hangzhou','win_source',0,'ten_winds',41,{}],['hu_self',0,2,['HZ|ten_winds|3|十风'],[6,-2,-2,-2],0,0,-1,0]])
  for(const node of [4,0,3,4]) {
    const s=r.build(0,node).snapshot.seats[0]
    assert.equal(s.hand_tiles.length,13);assert.equal(s.drawn_tile,null)
    assert.equal(s.discard_pile.length,node?1:0)
  }
  const event=r.eventForStep(0,3).event
  assert.equal(event.tile,undefined);assert.equal(event.revealed_hand_tiles.length,13)
  assert.deepEqual(r.roundScoreChangesByOriginal(0),[6,-2,-2,-2])
})
test('杭州状态和番种按节点读取，旧规则不消耗杭州专用事件',()=>{
  assert.deepEqual(parseHangzhouFan('HZ|cai_piao|2|财飘'),{name:'财飘',value:'2番'});assert.equal(parseHangzhouFan('plain'),null)
  const round={hangzhou:{dealer_streak:1},action_ticks:[['hangzhou','state',{dealer_streak:2},[0,0,0,0]]]}
  assert.equal(hangzhouInfoAt(round,0).dealer_streak,1);assert.equal(hangzhouInfoAt(round,1).dealer_streak,2)
  round.action_ticks.push(['hangzhou','win_source',0,'ten_winds',41,{phase:'END',dealer_streak:2}]);assert.equal(hangzhouInfoAt(round,2).phase,'END')
  const old=replay([['hangzhou','tail_burn',0,15]],'guobiao')
  assert.equal(old.remainingWallAt(0,1).length,6);assert.equal(old.build(0,1).snapshot.state.remaining_tile_count,6)
})

test('杭州听牌只消费当前视角已记录的匹配手牌提示',()=>{
  const hand=[11,12,13,21,22,23,31,32,33,41,41,46,46]
  const hint={source_hand_tiles:hand,source_melds:[],waiting_tiles:[46,11],waiting_by_discard:{}}
  const round={action_ticks:[['hangzhou','hints',2,hint]]}
  assert.equal(hangzhouWaitDataAt(round,0,2,hand,[],hand),null)
  assert.equal(hangzhouWaitDataAt(round,1,0,hand,[],hand),null)
  assert.equal(hangzhouWaitDataAt({},5,2,hand,[],hand),null)
  assert.equal(hangzhouWaitDataAt(round,1,2,hand.slice(1),[],hand),null)
  assert.equal(hangzhouWaitDataAt(round,1,2,hand,['k41'],hand),null)
  const data=hangzhouWaitDataAt(round,1,2,[...hand].reverse(),[],[...hand,46])
  assert.equal(data.type,'waits')
  assert.deepEqual(data.details,[{tile:11,base_f:0,selfdrawn_f:1,remaining_count:3},{tile:46,base_f:0,selfdrawn_f:1,remaining_count:1}])
})

test('摸牌后的合法弃牌提示和弃牌瞬间只移除一张实体财神',()=>{
  const hand=[11,12,13,21,22,23,31,32,33,41,41,46,46,46]
  const hint={source_hand_tiles:hand,source_melds:[],waiting_tiles:[],waiting_by_discard:{46:[11,46],47:[47]}}
  const round={action_ticks:[['hangzhou','hints',0,hint]]}
  const all=hangzhouWaitDataAt(round,1,0,hand,[],hand)
  assert.equal(all.type,'waits_all');assert.deepEqual(all.details.map(row=>row.discard_tile),[46])
  assert.equal(all.details[0].adds.find(row=>row.tile===46).remaining_count,1)
  const cut=hand.slice(0,-1)
  const after=hangzhouWaitDataAt(round,1,0,cut,[],[...cut,46])
  assert.equal(after.type,'waits');assert.deepEqual(after.details.map(row=>row.tile),[11,46])
  assert.equal(hangzhouWaitDataAt(round,1,0,cut.slice(0,-1),[],cut),null)
  assert.deepEqual(hint.source_hand_tiles,hand)
  round.action_ticks.push(['hangzhou','hints',0,{}])
  assert.equal(hangzhouWaitDataAt(round,2,0,hand,[],hand),null)
})

test('剩余牌数只使用己方手牌与公开副露河牌，隐藏手牌及暗杠不泄露',()=>{
  const snapshot={viewer:{seat_index:1},seats:[
    {seat_index:0,hand_tiles:[46,46],drawn_tile:46,discard_pile:[11],melds:[{type:'kong',concealed:true,tile:46}]},
    {seat_index:1,hand_tiles:[46],drawn_tile:12,discard_pile:[13],melds:[{type:'kong',concealed:true,tile:41}]},
    {seat_index:2,hand_tiles:[46],discard_pile:[21],melds:[{type:'sequence',tile:32}]},
    {seat_index:3,hand_tiles:[],discard_pile:[],melds:[{type:'triplet',tile:22}]},
  ]}
  assert.deepEqual(hangzhouKnownTiles(snapshot,tile=>tile),[46,12,11,13,41,41,41,41,21,31,32,33,22,22,22])
})

test('杭州内部节点用中文标签，十风支付只变更展示而不改记账原因', () => {
  const ticks = [
    ['hangzhou', 'state', { phase: 'waiting_hangzhou_ten_winds' }],
    ['hangzhou', 'hints', 0, {}],
    ['hangzhou', 'win_source', 0, 'ten_winds', 41, {}],
    ['hangzhou', 'tail_burn', 0, 15],
    ['hangzhou', 'state', { phase: 'END' }],
  ]
  const record = replay(ticks)
  assert.deepEqual(ticks.map((_, index) => record.build(0, index + 1).actionLabel),
    ['十风和牌选择', '听牌提示', '十风和', '杠后暗弃', '杭州状态'])
  assert.equal(replay([['d', 11]], 'guobiao').build(0, 1).actionLabel, '摸牌')
  const ledger = Object.freeze({ source: 'ten_winds', payment: { transfers: [{ reason: '自摸' }] } })
  const info = Object.freeze({ ledger })
  assert.equal(hangzhouPaymentReason(info, ledger.payment.transfers[0].reason), '十风')
  assert.equal(ledger.payment.transfers[0].reason, '自摸')
  assert.equal(hangzhouPaymentReason(info, '三吃承包'), '三吃承包')
  assert.equal(hangzhouPaymentReason({ ledger: { source: 'normal' } }, '自摸'), '自摸')
  assert.equal(hangzhouPaymentReason({ ledger: null }, '自摸'), '自摸')
})


test('杭州余牌是可摸数，83张墙显示63、尾20张保留且旧标题仍可识别', () => {
  const wall = Array.from({ length: 83 }, (_, index) => 11 + index % 9)
  for (const count of [0, 1, 19, 20, 21, 22, 23, 83]) {
    for (const detailRule of ['hangzhou', '']) {
      const record = replay([], detailRule, wall.slice(0, count), 'hangzhou')
      assert.equal(record.remainingWallAt(0, 0).length, count)
      assert.equal(record.build(0, 0).snapshot.state.remaining_tile_count, Math.max(0, count - 20))
      assert.deepEqual([...hangzhouNormalDrawableWallIndices(record.wallViewAt(0, 0))], Array.from({ length: Math.max(0, count - 20) }, (_, i) => i))
    }
  }
  assert.equal(replay([], 'guobiao', wall).build(0, 0).snapshot.state.remaining_tile_count, 83)
})

test('暗弃和杠补保持实体墙、可摸数、普通摸牌预测分别正确，回退不重复扣尾20', () => {
  const wall = Array.from({ length: 23 }, (_, index) => 11 + index % 9)
  const record = replay([['hangzhou', 'tail_burn', 0, wall[21]], ['gd', wall[22]], ['c', wall[22], 'T'], ['d', wall[0]], ['liuju']], 'hangzhou', wall)
  const physical = [23, 22, 21, 21, 20, 20]
  for (const node of [5, 0, 4, 2, 1, 3, 5]) {
    const count = physical[node]
    const view = record.wallViewAt(0, node)
    const present = view.flatMap((entry, index) => entry.consumed ? [] : [index])
    assert.equal(record.remainingWallAt(0, node).length, count)
    assert.equal(record.build(0, node).snapshot.state.remaining_tile_count, count - 20)
    assert.deepEqual([...hangzhouNormalDrawableWallIndices(view)], present.slice(0, count - 20))
  }
  assert.deepEqual([...hangzhouNormalDrawableWallIndices(record.wallViewAt(0, 5))], [])
})
