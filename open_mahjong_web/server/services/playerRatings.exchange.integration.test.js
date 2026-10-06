const test = require('node:test');
const assert = require('node:assert/strict');
const { Client } = require('pg');
const { queryRankedStats } = require('./playerRatings');

test('exchange-three ranked metrics stay separate and legacy standard rows aggregate once', {
  skip: !process.env.RATING_TEST_DATABASE_URL,
}, async () => {
  const db = new Client({ connectionString: process.env.RATING_TEST_DATABASE_URL });
  await db.connect();
  try {
    const fields = ['total_rounds', 'win_count', 'self_draw_count', 'deal_in_count', 'total_fan_score',
      'total_win_turn', 'total_fangchong_score', 'first_place_count', 'second_place_count',
      'third_place_count', 'fourth_place_count', 'fulu_round_count', 'cuohe_count', 'total_round_score'];
    await db.query(`CREATE TEMP TABLE game_player_metrics (
      user_id bigint, rule text, sub_rule text, room_type text, match_type text,
      ${fields.map(field => `${field} int DEFAULT 0`).join(', ')}
    )`);
    await db.query(`CREATE TEMP TABLE game_records (game_id text);
      CREATE TEMP TABLE game_player_records (game_id text, user_id bigint, rule text, room_type text, match_type text);
      CREATE TEMP TABLE riichi_player_game_stats (game_id text, user_id bigint, version int, stats jsonb)`);
    for (const [user, sub, room, rounds] of [
      [101, null, 'match', 16], [101, 'sichuan/standard', 'match', 16],
      [101, 'sichuan/xueliu_exchange', 'match', 16],
      [101, 'sichuan/xueliu_exchange', 'custom', 8],
      [202, 'sichuan/xueliu_exchange', 'match', 16],
    ]) {
      await db.query(`INSERT INTO game_player_metrics
        (user_id, rule, sub_rule, room_type, match_type, total_rounds)
        VALUES ($1, 'sichuan', $2, $3, '4/4_rank', $4)`, [user, sub, room, rounds]);
    }
    const result = await queryRankedStats(db, 101);
    assert.equal(result.sichuan.length, 1);
    assert.equal(result.sichuan[0].total_games, 2);
    assert.equal(result.sichuan[0].total_rounds, 32);
    assert.equal(result.sichuan_xueliu_exchange.length, 1);
    assert.equal(result.sichuan_xueliu_exchange[0].total_games, 1);
    assert.equal(result.sichuan_xueliu_exchange[0].total_rounds, 16);
  } finally {
    await db.end();
  }
});
