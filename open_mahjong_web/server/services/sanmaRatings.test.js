const test = require('node:test');
const assert = require('node:assert/strict');
const { queryPlayerRatings, queryRankedStats, recordRatingFields } = require('./playerRatings');

test('three-player grades load independently and do not expose Elo', async () => {
  const db = { query: async () => ({ rows: [
    { rule:'riichi',rank_name:'七段',rank_score:1000,games:20 },
    { rule:'riichi_sanma',rank_name:'初段',rank_score:245,games:1 },
  ] }) };
  const ratings = await queryPlayerRatings(db,101,{guobiao_rank:'10级',guobiao_score:0});
  assert.equal(ratings.riichi_sanma.rank_score,245);
  assert.equal(ratings.riichi_sanma.system,'grade');
  assert.equal('elo' in ratings.riichi_sanma,false);
  assert.equal(ratings.riichi.rank_score,1000);
});

test('three-player record rating aliases the gameplay sub-rule and rejects four-player payloads', () => {
  const row = {user_id:101,rule:'riichi',sub_rule:'riichi/sanma',room_type:'match'};
  const rating = {rating_rule:'riichi_sanma',rating_system:'grade',rating_pt:-30,rank_before:'初段',rank_after:'初段',score_before:200,score_after:170};
  const result=recordRatingFields(row,{101:rating});
  assert.equal(result.rating_rule,'riichi_sanma');assert.equal(result.rating_pt,-30);
  assert.equal(result.elo_delta,null);assert.equal(result.score_after,170);
  assert.equal(recordRatingFields(row,{101:{...rating,rating_rule:'riichi'}}).rating_rule,null);
  assert.equal(recordRatingFields({...row,room_type:'custom'},{101:rating}).rating_rule,null);
});

test('ranked three-player summaries exclude four-player and custom modes', async () => {
  const db={query:async sql=>({rows:sql.includes('game_player_metrics')?[]:[
    {mode:'1/4_sanma_rank',stats:{total_games:1,total_rounds:3,fan_stats:{riichi:1}}},
    {mode:'2/4_sanma_rank',stats:{total_games:1,total_rounds:6,fan_stats:{riichi:2}}},
    {mode:'2/4_rank',stats:{total_games:10,total_rounds:80}},
  ]})};
  const stats=await queryRankedStats(db,101);
  assert.deepEqual(stats.riichi_sanma.map(s=>s.mode),['1/4_sanma_rank','2/4_sanma_rank']);
  assert.equal(stats.riichi.length,1);
  assert.equal(stats.riichi[0].total_games,10);
});
