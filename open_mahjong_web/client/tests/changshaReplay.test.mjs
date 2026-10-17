import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { build } from 'esbuild'
import { changshaBirds, changshaInfoAt, changshaRon } from '../src/utils/changshaReplay.js'

const compiled = await build({ entryPoints: [fileURLToPath(new URL('../src/game2d/replay/recordReplay.ts', import.meta.url))], bundle: true, platform: 'node', format: 'esm', write: false, logLevel: 'silent' })
const { RecordReplay } = await import(`data:text/javascript;base64,${Buffer.from(compiled.outputFiles[0].text).toString('base64')}`)
const { fixtures } = JSON.parse(await readFile(new URL('./fixtures/changshaReplay.json', import.meta.url), 'utf8'))
const mm = n => ({ 1: 0x40, 2: 0x60, 3: 0xc0 })[Math.floor(n / 10)] + n % 10
const sorted = values => [...values].sort((a,b) => a-b)
const rd = d => d.record.game_round.round_index_1
const fixture = name => structuredClone(fixtures.find(f => f.name === name))
const hand = seat => [...seat.hand_tiles, ...(seat.drawn_tile == null ? [] : [seat.drawn_tile])]

for (const f of fixtures) test(`Python record: ${f.name}, physical wall and repeatable seeks`, () => {
  const d = structuredClone(f.detail), original = structuredClone(d), round = rd(d), r = new RecordReplay(d)
  const ticks = round.action_ticks, n = ticks.length
  for (const viewer of [0,1,2,3]) {
    const end = r.build(0,n,viewer).snapshot
    assert.deepEqual(r.remainingWallAt(0,n),f.expected.wall.map(mm))
    assert.deepEqual(r.wallViewAt(0,n).filter(t => !t.consumed).map(t => t.tile),f.expected.wall.map(mm))
    assert.equal(end.state.remaining_tile_count, f.expected.wall.length)
    assert.deepEqual(end.seats.map(s => s.score),f.expected.scores)
    for (let i=0;i<4;i++) {
      // Point-win records add a virtual winning tile; the server keeps the waiting hand.
      const won = ticks.filter(t => t[0].startsWith('hu_') && t[0] !== 'hu_self' && t[1] === i).at(-1)
      const expectedHand = [...f.expected.hands[i], ...(won ? [won[5]] : [])]
      assert.deepEqual(sorted(hand(end.seats[i])),sorted(expectedHand.map(mm)),`seat ${i} hand`)
      assert.equal(end.seats[i].melds.length, f.expected.melds[i].length)
    }
    // Independently replay physical removals by the actual recorded indices.
    for (const node of [...Array(n+1).keys()].reverse()) {
      const wall = [...round.tiles_list]
      let tailPair = true
      for (const t of ticks.slice(0,node)) {
        if (t[0] === 'gd' && t[3] === 'changsha_open_kong') {
          if (tailPair && t[4] === wall.length-2) tailPair=false
          assert.equal(wall.splice(t[4],1)[0],t[1])
        } else if (t[0] === 'gd') { assert.equal(wall.splice(tailPair && wall.length>1 ? -2 : -1,1)[0],t[1]); tailPair=!tailPair }
        else if (t[0] === 'd') assert.equal(wall.shift(),t[1])
      }
      assert.deepEqual(r.remainingWallAt(0,node),wall.map(mm))
      assert.equal(r.build(0,node,viewer).snapshot.state.remaining_tile_count,wall.length)
    }
    assert.deepEqual(r.build(0,n,viewer).snapshot,end)
  }
  assert.deepEqual(d,original)
})

test('public batches stay outside concealed hand; all forced cuts belong to the kong owner', () => {
  const {detail:d,expected} = fixture('wall-30-batch-4'), r = new RecordReplay(d), ticks=rd(d).action_ticks
  const baseHand = sorted(expected.hands[0].map(mm)), cuts = []
  for (let i=0;i<ticks.length;i++) {
    const after = r.build(0,i+1).snapshot, update=r.eventForStep(0,i)
    assert.deepEqual(sorted(hand(after.seats[0])),baseHand)
    if (ticks[i][0] === 'gd') {
      assert.equal(update.event.actor_seat,0)
      assert.equal(update.replay_snapshot_required,true)
      assert.equal(after.seats[0].public_kong_tiles.at(-1),mm(ticks[i][1]))
    }
    if (ticks[i][0] === 'c') { cuts.push(mm(ticks[i][1])); assert.equal(update.event.actor_seat,0) }
  }
  const end = r.build(0,ticks.length).snapshot
  assert.deepEqual(end.seats.map(s=>s.discard_pile),[cuts,[],[],[]])
  assert.deepEqual(end.seats[0].public_kong_tiles,[])
  assert.equal(end.state.current_player,1)
  assert.equal(r.xunmuNodes(0,0).length,2)
  assert.deepEqual(r.xunmuNodes(0,1),[0])
})

test('explicit batch seat survives every perspective and seat rotation', () => {
  for (let seat=0;seat<4;seat++) {
    const {detail:d}=fixture('wall-30-batch-2'), round=rd(d)
    const owner=round.p0_tiles; round.p0_tiles=round[`p${seat}_tiles`]; round[`p${seat}_tiles`]=owner
    round.start_player_index=seat
    for (const t of round.action_ticks) if (t[0]==='gd') t[2]=seat
    const r=new RecordReplay(d)
    for (let viewer=0;viewer<4;viewer++) {
      const end=r.build(0,round.action_ticks.length,viewer).snapshot
      assert.deepEqual(end.seats[seat].discard_pile,[21,22].map(mm))
      assert.equal(end.viewer.seat_index,viewer)
      for (let i=1;i<round.action_ticks.length;i++) assert.equal(r.eventForStep(0,i,viewer).event.actor_seat,seat)
    }
  }
})

test('batch multi-ron replaces each winning tile, recycles only the matching final discard, and preserves birds', () => {
  const {detail:d}=fixture('multi-ron-capped'), round=rd(d), r=new RecordReplay(d)
  const wins=round.action_ticks.map((t,i)=>[t,i]).filter(([t])=>t[0].startsWith('hu_'))
  assert.equal(wins.length,3)
  for (const [t,i] of wins) {
    const s=r.build(0,i+1).snapshot
    assert.equal(s.seats[t[1]].drawn_tile,mm(t[5]))
    assert.deepEqual(s.seats[1].hand_tiles,[mm(31)])
    assert.deepEqual(changshaBirds(t),[11,22])
    assert.equal(changshaRon(t).discarder,0)
    assert.equal(r.eventForStep(0,i).event.tile,mm(t[5]))
  }
  const end=r.build(0,round.action_ticks.length).snapshot
  assert.deepEqual(end.seats[0].discard_pile,[mm(21)])
  assert.deepEqual(end.seats.map(s=>s.score),[-88,64,24,0])
  assert.deepEqual(changshaInfoAt(round,round.action_ticks.length,d.record.game_title).birds,[11,22])
  assert.deepEqual(r.remainingWallAt(0,round.action_ticks.length).slice(0,2),[11,22].map(mm))
})

test('single concealed supplement and old consecutive public draws remain compatible', () => {
  const {detail:d}=fixture('concealed-supplement'), r=new RecordReplay(d), end=r.build(0,rd(d).action_ticks.length).snapshot.seats[0]
  assert.deepEqual(end.public_kong_tiles,[])
  assert.equal(end.drawn_tile,mm(rd(d).action_ticks.at(-1)[1]))
  const legacy=fixture('wall-30-batch-2').detail
  const round=rd(legacy)
  round.tiles_list=Array(26).fill(11).concat([21,22,21,22])
  round.action_ticks=round.action_ticks.map(t=>t[0]==='gd'?t.slice(0,2):t)
  delete round.changsha_open_kong_batches
  const old=new RecordReplay(legacy)
  assert.deepEqual(old.build(0,round.action_ticks.length).snapshot.seats[0].discard_pile,[21,22].map(mm))
  assert.deepEqual(old.wallViewAt(0,round.action_ticks.length).map(t=>t.consumed).slice(-4),[false,false,true,true])
  assert.deepEqual(changshaBirds(['hu_first',1,8,[],[-8,8,0,0],21,[11,22]]),[11,22])
})

test('claiming an earlier public tile keeps unrelated river tiles and hand tiles intact', () => {
  const {detail:d}=fixture('wall-30-batch-2'), round=rd(d)
  round.p1_tiles=[21,21,25,26]
  round.action_ticks.push(['p',21,1,21,21])
  const end=new RecordReplay(d).build(0,round.action_ticks.length).snapshot
  assert.deepEqual(end.seats[0].discard_pile,[mm(22)])
  assert.deepEqual(sorted(hand(end.seats[1])),[25,26].map(mm))
  assert.equal(end.seats[1].melds[0].type,'triplet')
})

test('invalid dice positions fail explicitly; other rules retain ordinary draw handling', () => {
  const {detail:d}=fixture('wall-30-batch-2'); rd(d).action_ticks[1][4]=99
  assert.throws(()=>new RecordReplay(d).remainingWallAt(0,2),/取牌位置/)
  d.rule=d.record.game_title.rule='guobiao'
  const r=new RecordReplay(d), after=r.build(0,2).snapshot
  assert.equal(r.eventForStep(0,1).replay_snapshot_required,false)
  assert.equal(after.seats[0].public_kong_tiles,undefined)
  assert.equal(after.seats[0].drawn_tile,mm(21))
})

test('empty dice batches still show dice and advance to the next normal draw', () => {
  const {detail:d}=fixture('wall-8-batch-4'), round=rd(d)
  round.action_ticks.push(['d',round.tiles_list[0]])
  const r=new RecordReplay(d), end=r.build(0,2).snapshot
  assert.equal(end.seats[1].drawn_tile,mm(round.tiles_list[0]))
  assert.equal(r.eventForStep(0,1).event.actor_seat,1)
  assert.equal(changshaInfoAt(round,1).batch.sequence,1)
  assert.deepEqual(changshaInfoAt(round,1).batch.tiles,[])
})

test('a public replacement can extend a pung without consuming an unrelated concealed tile', () => {
  const {detail:d}=fixture('wall-30-batch-2'), round=rd(d)
  round.start_player_index=3
  round.p0_tiles=[21,21,11,11,11,11,12,13,14,22,23,24,25]
  round.p3_tiles=[21]
  round.action_ticks=[['c',21,'F'],['p',21,0,21,21],['c',25,'F'],['reset',0],['ag',11,'F',11,11,11,11],
    ['gd',21,0,'changsha_open_kong',8,[21,31]],['gd',31,0,'changsha_open_kong',8,[21,31]],['c',31,'T'],['jg',21,'T']]
  const r=new RecordReplay(d), before=r.build(0,8).snapshot, after=r.build(0,9).snapshot
  assert.deepEqual(hand(after.seats[0]),hand(before.seats[0]))
  assert.deepEqual(after.seats[0].public_kong_tiles,[])
  assert.equal(after.seats[0].melds[0].type,'kong')
  assert.equal(after.state.current_player,0)
})

test('robbed added kong returns to a pung once and keeps unrelated discards', () => {
  const {detail:d}=fixture('wall-30-batch-2'), round=rd(d)
  round.p0_tiles=[11]; round.p1_tiles=[11,11,23]; round.p2_tiles=[22]; round.p3_tiles=[33]
  round.action_ticks=[['c',11,'F'],['p',11,1,11,11],['c',23,'F'],['reset',1],['d',11],['jg',11,'T'],
    ['hu_first',2,12,['抢杠胡'],[0,-12,12,0],11,1,1,0],['hu_second',3,12,['抢杠胡'],[0,-12,0,12],11,1,1,1]]
  for(const node of [7,8]) {
    const seats=new RecordReplay(d).build(0,node).snapshot.seats
    assert.equal(seats[1].melds[0].type,'triplet')
    assert.deepEqual(seats[1].discard_pile,[mm(23)])
  }
})
