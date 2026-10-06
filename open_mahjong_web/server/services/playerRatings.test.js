const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { queryPlayerRatings, queryRankedStats, recordRatingFields } = require('./playerRatings');

test('older database defaults are additive and genuine database failures are not hidden', async () => {
  const missing = { query: async () => { throw Object.assign(new Error('missing table'), {code:'42P01'}); } };
  const ratings = await queryPlayerRatings(missing,101,{guobiao_rank:'三段',guobiao_score:0});
  assert.equal(ratings.guobiao.rank_name,'三段');assert.equal(ratings.guobiao.rank_score,0);
  assert.equal(ratings.riichi.rank_name,'10级');assert.equal(ratings.qingque.elo,1500);
  assert.equal('elo' in ratings.guobiao,false);assert.equal('elo' in ratings.riichi,false);
  assert.deepEqual(await queryRankedStats(missing,101),{guobiao:[],riichi:[],qingque:[],sichuan:[],riichi_sanma:[],sichuan_xueliu_exchange:[]});
  const broken = { query: async () => { throw Object.assign(new Error('connection failed'), {code:'08006'}); } };
  await assert.rejects(queryPlayerRatings(broken,101,{}),{code:'08006'});
  await assert.rejects(queryRankedStats(broken,101),{code:'08006'});
});

test('settlement values are whitelisted, finite and separate from replay and grade balances', () => {
  const row={user_id:101,rule:'riichi',room_type:'match',pt_change:null};
  const results={101:{rating_rule:'riichi',rating_system:'grade',rating_pt:'0',
    rank_before:'10级',rank_after:'9级',score_before:90,score_after:0,
    elo_before:'1500',elo_after:'1484',elo_delta:'-16',rating_games:'1',duplicate_seed:'secret'}};
  const fields=recordRatingFields(row,results);
  assert.equal(fields.pt_change,0);assert.equal(fields.rating_pt,0);assert.equal(fields.elo_delta,null);
  assert.equal(fields.elo_before,null);assert.equal(fields.elo_after,null);
  assert.equal(fields.score_after-fields.score_before,-90);
  assert.equal(fields.duplicate_seed,undefined);
  assert.equal(recordRatingFields({...row,pt_change:'-5.15'},results).pt_change,-5.15);
  assert.equal(recordRatingFields({...row,room_type:'custom'},results).elo_delta,null);
  assert.equal(recordRatingFields({...row,rule:'qingque',pt_change:0},null).pt_change,null);
  for(const invalid of [NaN,Infinity,'not-a-number',' ',true,[],{}]) {
    const eloRow={...row,rule:'qingque'};
    assert.equal(recordRatingFields(eloRow,{101:{...results[101],rating_rule:'qingque',rating_system:'elo',elo_delta:invalid}}).elo_delta,null);
  }
  for(const invalid of [null,[],{...results[101],rating_rule:'guobiao'},{...results[101],rating_system:'elo'}]) {
    assert.equal(recordRatingFields(row,{101:invalid}).rating_rule,null);
  }
});

test('grade pools hide legacy Elo while Elo pools expose their stored rating', async () => {
  const db={query:async()=>({rows:[
    {rule:'guobiao',elo:1800,games:9},{rule:'riichi',elo:1600,games:2},
    {rule:'qingque',elo:1650,games:4},{rule:'sichuan',elo:1300,games:5},
  ]})};
  const ratings=await queryPlayerRatings(db,101,{guobiao_rank:'四段',guobiao_score:800});
  assert.equal('elo' in ratings.guobiao,false);assert.equal('elo' in ratings.riichi,false);
  assert.equal(ratings.qingque.elo,1650);assert.equal(ratings.sichuan.elo,1300);
  const payload={101:{rating_rule:'sichuan',rating_system:'elo',elo_before:1500,elo_after:1484,elo_delta:-16,rating_games:1}};
  assert.equal(recordRatingFields({user_id:101,rule:'sichuan',room_type:'match'},payload).elo_delta,-16);
});

test('Nanque dictionary follows the authoritative fan IDs and Chinese names', () => {
  const source=fs.readFileSync(path.join(__dirname,'../../../open_mahjong_server/server/game_calculation/jiandan/fan_definitions.py'),'utf8');
  const fans=Object.fromEntries([...source.matchAll(/_fan\("([^"]+)",\s*"([^"]+)"/g)].map(match=>[match[1],match[2]]));
  assert.ok(Object.keys(fans).length>0);
  assert.deepEqual(require('../constants/nanqueFanDict'),fans);
});

test('exchange-three ratings default independently and replay results require the exact sub-rule', async () => {
  const pool = 'sichuan_xueliu_exchange';
  const ratings = await queryPlayerRatings({ query: async () => ({ rows: [{ rule: 'sichuan', elo: 1777, games: 8 }] }) }, 101, {});
  assert.equal(ratings.sichuan.elo, 1777);
  assert.equal(ratings[pool].elo, 1500);
  assert.equal(ratings[pool].games, 0);
  const results = { 101: { rating_rule: pool, rating_system: 'elo', elo_before: 1500, elo_after: 1516, elo_delta: 16, rating_games: 1 } };
  const row = { user_id: 101, rule: 'sichuan', sub_rule: 'sichuan/xueliu_exchange', room_type: 'match' };
  assert.equal(recordRatingFields(row, results).elo_delta, 16);
  assert.equal(recordRatingFields({ ...row, sub_rule: 'sichuan/standard' }, results).elo_delta, null);
  assert.equal(recordRatingFields({ ...row, room_type: 'custom' }, results).rating_rule, null);
});
