import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'
import { build } from 'esbuild'
import { loadChangshaForm, buildChangshaRoomPayload, changshaRows } from '../src/utils/changshaRoomConfig.js'
import { changshaInfoAt } from '../src/utils/changshaReplay.js'
import { exchangeSixInfoAt, exchangeSixStatisticsRound, exchangeSixResultTick, exchangeSixChoiceText, exchangeSixActionNames } from '../src/utils/changshaExchangeSixReplay.js'
import { changshaShapeFans, exchangeSixWaits } from '../src/utils/changshaWaits.js'
import { analyzeRecords } from '../src/utils/recordAnalyzer.js'
const require = createRequire(import.meta.url)
const { readRoomSettings, applyRoomSettingsChange } = require('../../server/utils/eventRoomSettings.js')
const normalizeRoomConfig = (rule, config) => applyRoomSettingsChange(readRoomSettings({}), {
  kind: 'manual', body: { revision: 0, room_rule: rule, room_config: config }, userId: 10001, eventStatus: 'active',
}).settings.manual.room_config
const fixed = JSON.parse(await readFile(new URL('../../shared/changshaExchangeSixOptions.json',import.meta.url),'utf8'))
const compiled = await build({ entryPoints: [fileURLToPath(new URL('../src/game2d/replay/recordReplay.ts',import.meta.url))], bundle:true,platform:'node',format:'esm',write:false,logLevel:'silent' })
const { RecordReplay } = await import(`data:text/javascript;base64,${Buffer.from(compiled.outputFiles[0].text).toString('base64')}`)
const { fixtures } = JSON.parse(await readFile(new URL('./fixtures/changshaExchangeSixReplay.json',import.meta.url),'utf8'))
const mm = t => ({1:0x40,2:0x60,3:0xc0})[Math.floor(t/10)] + t%10
const sorted = a => [...a].sort((a,b)=>a-b)
const tiles = s => [...s.hand_tiles,...(s.drawn_tile == null ? [] : [s.drawn_tile])]

for (const f of fixtures) test(`exchange-six server record: ${f.name}`, () => {
  const original = structuredClone(f.detail), replay = new RecordReplay(f.detail), round = f.detail.record.game_round.round_index_1, n=round.action_ticks.length
  for (let viewer=0;viewer<4;viewer++) {
    const end=replay.build(0,n,viewer).snapshot
    assert.deepEqual(end.seats.map(s=>s.score), f.expected.scores)
    assert.deepEqual(replay.remainingWallAt(0,n), f.expected.wall.map(mm))
    assert.equal(end.state.remaining_tile_count, f.expected.wall.length)
    for (let i=0;i<4;i++) {
      assert.deepEqual(sorted(tiles(end.seats[i])),sorted(f.expected.hands[i].map(mm)),`seat ${i} hand`)
      assert.deepEqual(end.seats[i].discard_pile,f.expected.rivers[i].map(mm),`seat ${i} river`)
      assert.equal(end.seats[i].melds.length,f.expected.melds[i].length)
    }
    for (let node=n;node>=0;node--) {
      const s=replay.build(0,node,viewer).snapshot
      assert.equal(s.state.remaining_tile_count,replay.remainingWallAt(0,node).length)
      assert.equal(replay.wallViewAt(0,node).filter(t=>!t.consumed).length,s.state.remaining_tile_count)
    }
    assert.deepEqual(replay.build(0,n,viewer).snapshot,end)
  }
  assert.deepEqual(f.detail,original)
})

test('four-joy score is visible immediately and terminal summary does not double it',()=>{
 const f=fixtures.find(f=>f.name==='four-joy-then-self-draw'), r=new RecordReplay(f.detail), ticks=f.detail.record.game_round.round_index_1.action_ticks
 const at=ticks.findIndex(t=>t[0]==='changsha_four_joy')
 assert.deepEqual(r.build(0,at+1).snapshot.seats.map(s=>s.score),Object.values(ticks[at][4]))
 assert.deepEqual(r.build(0,ticks.length).snapshot.seats.map(s=>s.score),f.expected.scores)
})
test('multi-ron statistics credit both winners and only actual payer',()=>{
 const f=fixtures.find(f=>f.name==='multiple-ron'), round=f.detail.record.game_round.round_index_1
 const record=structuredClone(f.detail.record)
 for(let i=0;i<4;i++) record.game_title[`p${i}_uid`]=100+i
 const stats=[100,101,102,103].map(id=>analyzeRecords([record],id))
 assert.deepEqual(stats.map(s=>s.win_count),[0,1,1,0])
 assert.deepEqual(stats.map(s=>s.total_round_score),f.expected.scores)
 assert.equal(stats[0].deal_in_count,2)
 assert.equal(stats[1].fan_stats['小胡'],1)
 const normalized=exchangeSixStatisticsRound(round).action_ticks.filter(t=>t[0].startsWith('hu_'))
 assert.equal(normalized.length,2); assert.deepEqual(normalized.map(t=>t[1]),[1,2])
 assert.deepEqual(normalized.reduce((sum,t)=>sum.map((v,i)=>v+t[4][i]),[0,0,0,0]),f.expected.scores)
 const summary=round.action_ticks.find(t=>t[0].startsWith('hu_')), wins=round.action_ticks.find(t=>t[0]==='changsha_exchange_six_win')[1]
 for(const win of wins) {
  const shown=exchangeSixResultTick(summary,wins,win.winner)
  assert.equal(shown[1],win.winner);assert.equal(shown[2],win.payment);assert.equal(shown[5],win.groups[0].tiles[0])
  assert.deepEqual(shown[4],summary[4])
 }
})
test('declared open hands stay visible with other hands hidden, and reverse seeking restores privacy',()=>{
 const detail=structuredClone(fixtures[0].detail), round=detail.record.game_round.round_index_1
 round.action_ticks=[['changsha_declaration',1,'changsha_open_hand']]
 const r=new RecordReplay(detail)
 assert.equal(r.build(0,1,0,false).snapshot.seats[1].hand_revealed,true)
 assert.equal(r.build(0,1,0,false).snapshot.seats[2].hand_revealed,false)
 assert.equal(r.build(0,0,0,false).snapshot.seats[1].hand_revealed,false)
})
test('front and node keep exchange mode/time, normalize stale classic options, and preserve classic defaults',()=>{
 for(const seconds of [10,30,60]) {
  const form=loadChangshaForm({}, {sub_rule:'changsha/exchange_six',exchange_selection_seconds:seconds,score_cap_type:'score',score_cap_value:64})
  const copy=structuredClone(form), payload=buildChangshaRoomPayload(form), node=normalizeRoomConfig('changsha',payload.room_config)
  assert.equal(node.sub_rule,'changsha/exchange_six');assert.equal(node.exchange_selection_seconds,seconds)
  for(const [key,value] of Object.entries(fixed)) assert.equal(node[key],value)
  const preview=changshaInfoAt({action_ticks:[]},0,form)
  assert.equal(preview.openKongCount,4);assert.equal(preview.capType,'none')
  assert.equal(changshaRows(form).some(row=>row.value==='64'),false)
  assert.deepEqual(form,copy)
 }
 for(const seconds of [9,61,10.5,true,'30',null]) {
  assert.throws(()=>normalizeRoomConfig('changsha',{sub_rule:'changsha/exchange_six',exchange_selection_seconds:seconds}))
 }
 assert.equal(buildChangshaRoomPayload(loadChangshaForm({})).room_config.sub_rule,'changsha/classic_double_bird')
})
test('new hand shapes and reported non-jiang pair agree with Python calculator',async()=>{
 const vectors=JSON.parse(await readFile(new URL('./fixtures/changshaExchangeSixWaits.json',import.meta.url),'utf8'))
 for(const v of vectors) assert.deepEqual(sorted(changshaShapeFans(v.hand,[],{ready:v.ready})),sorted(v.fans),JSON.stringify(v))
})
test('declarations, disqualification, locked fans and small-hu ban affect replay hints',()=>{
 const round={action_ticks:[['changsha_deal_dice',[1,2],2],['changsha_declaration',1,'changsha_report_ready'],['changsha_declaration',1,'changsha_open_hand'],['changsha_lock_patterns',1,{11:[]}]]}
 const info=exchangeSixInfoAt(round,4), hand=[11,12,13,14,21,22,23,24,25,26,31,32,33]
 assert.deepEqual(exchangeSixWaits(hand,[],info,1).map(w=>[w.tile,w.fans]),[[11,['报听','明牌']]])
 info.disqualified=[1]; assert.deepEqual(exchangeSixWaits(hand,[],info,1),[])
})

test('exchange choices preserve hands, scores and wall and can be sought backward', () => {
 const detail = structuredClone(fixtures[0].detail), round = detail.record.game_round.round_index_1
 round.action_ticks = [['changsha_deal_dice', [1, 2], 2], ['changsha_exchange_choice_start', 0],
   ['changsha_exchange_decision', 0, 3, 'changsha_no_exchange']]
 const replay = new RecordReplay(detail), original = replay.build(0, 0).snapshot
 for (let viewer = 0; viewer < 4; viewer++) {
   const before = replay.build(0, 0, viewer).snapshot
   const after = replay.build(0, 3, viewer).snapshot
   assert.deepEqual(after.seats.map(tiles), before.seats.map(tiles))
   assert.deepEqual(after.seats.map(s => s.score), before.seats.map(s => s.score))
   assert.deepEqual(replay.remainingWallAt(0, 3), replay.remainingWallAt(0, 0))
 }
 assert.equal(exchangeSixInfoAt(round, 2).choicePhase, 'exchange_start_choice')
 assert.equal(exchangeSixChoiceText(exchangeSixInfoAt(round, 3)), '北家选择：全桌不换')
 assert.equal(exchangeSixInfoAt(round, 3).completedRounds, 0)
 assert.equal(exchangeSixInfoAt(round, 1).choice, null)
 assert.deepEqual(replay.build(0, 0).snapshot, original)
 assert.equal(exchangeSixActionNames.changsha_exchange_decision, '换牌决定')
})

test('second exchange uses current hands and records the completed rounds and timeout choice', () => {
 const detail = structuredClone(fixtures[0].detail), round = detail.record.game_round.round_index_1
 const initial = structuredClone(round.action_ticks)
 const hands = fixtures[0].expected.hands.map(hand => [...hand])
 const outgoing = hands.map(hand => hand.slice(0, 6)), offset = 3
 const final = hands.map((hand, seat) => {
   const kept = [...hand]
   for (const tile of outgoing[seat]) kept.splice(kept.indexOf(tile), 1)
   return sorted([...kept, ...outgoing[(seat + offset) % 4]])
 })
 round.action_ticks = [
   ['changsha_exchange_choice_start', 0], ['changsha_exchange_decision', 0, -1, 'changsha_begin_exchange'],
   ...initial, ['changsha_exchange_choice_start', 1], ['changsha_exchange_decision', 1, 2, 'changsha_exchange_again'],
   ['changsha_exchange_start', 2, [], 0], ['changsha_exchange_roll', 2, [4, 4], offset],
   ['changsha_exchange_six', [4, 4], offset, outgoing, [], 2],
 ]
 const replay = new RecordReplay(detail), end = round.action_ticks.length
 for (let viewer = 0; viewer < 4; viewer++) {
   assert.deepEqual(replay.build(0, end, viewer).snapshot.seats.map(s => sorted(tiles(s))), final.map(h => sorted(h.map(mm))))
   assert.deepEqual(replay.build(0, initial.length + 2, viewer).snapshot.seats.map(s => sorted(tiles(s))), hands.map(h => sorted(h.map(mm))))
 }
 assert.equal(exchangeSixInfoAt(round, end).completedRounds, 2)
 assert.equal(exchangeSixChoiceText(exchangeSixInfoAt(round, end)), '西家选择：再换一轮')
 const timeout = {action_ticks: [...initial, ['changsha_exchange_decision', 1, -1, 'changsha_no_exchange']]}
 assert.equal(exchangeSixChoiceText(exchangeSixInfoAt(timeout, timeout.action_ticks.length)), '超时默认：不再换牌')
 const oldRound = {action_ticks: initial.map(t => t[0] === 'changsha_exchange_six' ? t.slice(0, 5) : t)}
 assert.equal(exchangeSixInfoAt(oldRound, oldRound.action_ticks.length).completedRounds, 1)
})
