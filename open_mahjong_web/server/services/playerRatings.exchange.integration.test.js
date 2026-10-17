const test = require('node:test');
const assert = require('node:assert/strict');
const { Client } = require('pg');
const { queryRankedStats } = require('./playerRatings');

test('exchange-three ranked metrics preserve old full games beside new eight-hand games', {
  skip: !process.env.RATING_TEST_DATABASE_URL,
}, async () => {
  const db = new Client({ connectionString: process.env.RATING_TEST_DATABASE_URL });
  await db.connect();
  try {
    const fields = ['total_rounds', 'win_count', 'self_draw_count', 'deal_in_count', 'total_fan_score',
      'total_win_turn', 'total_fangchong_score', 'first_place_count', 'second_place_count',
      'third_place_count', 'fourth_place_count', 'fulu_round_count', 'cuohe_count', 'total_round_score'];
    await db.query(`CREATE TEMP TABLE game_player_metrics (
      game_id text, user_id bigint, rule text, sub_rule text, room_type text, match_type text,
      ${fields.map(field => `${field} int DEFAULT 0`).join(', ')}
    )`);
    await db.query(`CREATE TEMP TABLE game_records (game_id text);
      CREATE TEMP TABLE game_player_records (game_id text, user_id bigint, rule text, room_type text, match_type text, rank int);
      CREATE TEMP TABLE riichi_player_game_stats (game_id text, user_id bigint, version int, stats jsonb)`);
    let game = 0;
    for (const [user, sub, room, rounds] of [
      [101, null, 'match', 16], [101, 'sichuan/standard', 'match', 16],
      [101, 'sichuan/xueliu_exchange', 'match', 16],
      [101, 'sichuan/xueliu_exchange', 'match', 8],
      [101, 'sichuan/xueliu_exchange', 'custom', 8],
      [202, 'sichuan/xueliu_exchange', 'match', 16],
    ]) {
      const id = `game-${++game}`;
      const mode = `${rounds / 4}/4${room === 'match' ? '_rank' : ''}`;
      await db.query('INSERT INTO game_records VALUES ($1)', [id]);
      await db.query(`INSERT INTO game_player_records VALUES ($1,$2,'sichuan',$3,$4,1)`, [id, user, room, mode]);
      await db.query(`INSERT INTO game_player_metrics
        (game_id, user_id, rule, sub_rule, room_type, match_type, total_rounds)
        VALUES ($1, $2, 'sichuan', $3, $4, $5, $6)`, [id, user, sub || 'sichuan/standard', room, mode, rounds]);
    }
    const result = await queryRankedStats(db, 101);
    assert.equal(result.sichuan.length, 1);
    assert.equal(result.sichuan[0].total_games, 2);
    assert.equal(result.sichuan[0].total_rounds, 32);
    assert.equal(result.sichuan_xueliu_exchange.length, 2);
    const history = Object.fromEntries(result.sichuan_xueliu_exchange.map(row => [row.mode, row]));
    assert.equal(history['4/4_rank'].total_games, 1);
    assert.equal(history['4/4_rank'].total_rounds, 16);
    assert.equal(history['2/4_rank'].total_games, 1);
    assert.equal(history['2/4_rank'].total_rounds, 8);
  } finally {
    await db.end();
  }
});
