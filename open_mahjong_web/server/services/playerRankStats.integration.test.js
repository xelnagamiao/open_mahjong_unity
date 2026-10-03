const test = require('node:test');
const assert = require('node:assert/strict');
const { Pool } = require('pg');

test('filtered player scores and counts use the same records', {
  skip: !process.env.RECORD_METADATA_TEST_DATABASE_URL,
}, async (t) => {
  // A single connection keeps fixtures in temporary tables, with no persistent data changes.
  const pool = new Pool({ connectionString: process.env.RECORD_METADATA_TEST_DATABASE_URL, max: 1 });
  const databasePath = require.resolve('../config/database');
  const previousDatabase = require.cache[databasePath];
  require.cache[databasePath] = { id: databasePath, filename: databasePath, loaded: true, exports: pool };
  try {
    await pool.query(`
      CREATE TEMP TABLE game_records (game_id text PRIMARY KEY, created_at timestamp NOT NULL);
      CREATE TEMP TABLE game_player_records (
        game_id text, user_id bigint, rule text, sub_rule text, room_type text,
        match_type text, match_tier text, event_id text, score int, rank int,
        PRIMARY KEY (game_id, user_id)
      );
    `);
    const fixtures = [
      ['old', '2026-07-31 23:59:59', 'match', 'beginner', '1/4_rank', null, 10000, 1],
      ['start', '2026-08-01 00:00:00', 'match', 'beginner', '1/4_rank', null, 120, 1],
      ['end', '2026-08-31 23:59:59.999', 'match', 'beginner', '1/4_rank', null, -40, 4],
      ['outside', '2026-09-01 00:00:00', 'match', 'beginner', '1/4_rank', null, -9000, 4],
      ['tier', '2026-08-10 12:00:00', 'match', 'intermediate', '1/4_rank', null, 300, 1],
      ['length', '2026-08-10 12:00:00', 'match', 'beginner', '2/4_rank', null, -160, 4],
      ['custom', '2026-08-10 12:00:00', 'custom', null, '1/4', null, -32, 3],
      ['eventA', '2026-08-10 12:00:00', 'events', 'evtA', '1/4', 'evtA', 24, 2],
      ['eventB', '2026-08-10 12:00:00', 'events', 'evtB', '1/4', 'evtB', -240, 4],
    ];
    for (const [id, date, room, tier, length, event, score, rank] of fixtures) {
      await pool.query('INSERT INTO game_records VALUES ($1, $2)', [id, date]);
      await pool.query(`INSERT INTO game_player_records VALUES
        ($1, 101, 'guobiao', 'guobiao', $2, $3, $4, $5, $6, $7),
        ($1, 202, 'guobiao', 'guobiao', $2, $3, $4, $5, -9999, 4)`,
      [id, room, length, tier, event, score, rank]);
    }
    const { fetchPlayerRankStats } = require('./playerPublicApi');
    const dates = { date_from: '2026-08-01T00:00:00', date_to: '2026-09-01T00:00:00' };
    const query = { rule: 'guobiao', tier: 'beginner', game_type: 'dongfeng', ...dates };

    await t.test('inclusive start, exclusive end and every filter apply to both score and rank', async () => {
      const result = await fetchPlayerRankStats(101, query);
      assert.deepEqual(result, {
        total_games: 2, total_round_score: 80,
        first_place_count: 1, second_place_count: 0, third_place_count: 0, fourth_place_count: 1,
      });
      assert.equal(result.total_round_score / result.total_games, 40);
    });

    await t.test('negative, all-ladder, custom and specific-event selections', async () => {
      for (const [filters, games, score] of [
        [{ ...query, game_type: 'banzhuang' }, 1, -160],
        [{ ...query, tier: 'rank' }, 3, 380],
        [{ ...query, tier: 'custom' }, 1, -32],
        [{ ...query, tier: 'events', event_id: 'evtA' }, 1, 24],
        [{ ...query, tier: 'events', event_id: 'evtB' }, 1, -240],
        [{ rule: 'guobiao', tier: 'beginner', game_type: 'dongfeng' }, 4, 1080],
      ]) {
        const result = await fetchPlayerRankStats(101, filters);
        assert.equal(result.total_games, games);
        assert.equal(result.total_round_score, score);
      }
    });

    await t.test('empty date ranges return zero without historical scores', async () => {
      const result = await fetchPlayerRankStats(101, { ...query, date_from: '2026-12-01', date_to: '2026-12-02' });
      assert.equal(result.total_games, 0);
      assert.equal(result.total_round_score, 0);
    });

    await t.test('missing scores and rules with starting points remain unavailable', async () => {
      await pool.query("UPDATE game_player_records SET score = NULL WHERE game_id='start' AND user_id=101");
      assert.equal((await fetchPlayerRankStats(101, query)).total_round_score, null);
      await pool.query("UPDATE game_player_records SET rule='riichi', score=35000 WHERE game_id='start' AND user_id=101");
      const result = await fetchPlayerRankStats(101, { ...query, rule: 'riichi' });
      assert.equal(result.total_games, 1);
      assert.equal(result.total_round_score, null);
    });
  } finally {
    if (previousDatabase) require.cache[databasePath] = previousDatabase;
    else delete require.cache[databasePath];
    await pool.end();
  }
});
