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
      CREATE TEMP TABLE riichi_player_game_stats (game_id text,user_id bigint,version int,stats jsonb);
      CREATE TEMP TABLE game_player_metrics (
        id bigserial PRIMARY KEY, game_id text, user_id bigint,
        total_rounds int, win_count int, self_draw_count int, deal_in_count int,
        total_fan_score int, total_win_turn int, total_fangchong_score int,
        fulu_round_count int, cuohe_count int
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
    const metricKeys = ['total_rounds', 'win_count', 'self_draw_count', 'deal_in_count',
      'total_fan_score', 'total_win_turn', 'total_fangchong_score', 'fulu_round_count', 'cuohe_count'];
    const startMetrics = [4, 1, 1, 0, 32, 6, 0, 1, 0];
    const endMetrics = [8, 3, 1, 2, 168, 30, 48, 4, 1];
    const selectedMetrics = Object.fromEntries(metricKeys.map((key, i) => [key, startMetrics[i] + endMetrics[i]]));
    for (const [id] of fixtures) {
      // Other players and out-of-range games must never contribute to this player's details.
      for (const userId of [101, 202]) {
        const metrics = userId === 101 ? (id === 'start' ? startMetrics : endMetrics) : metricKeys.map(() => 9999);
        await pool.query(`INSERT INTO game_player_metrics (game_id, user_id, ${metricKeys.join(',')})
          VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)`, [id, userId, ...metrics]);
      }
    }
    const { fetchPlayerRankStats } = require('./playerPublicApi');
    const dates = { date_from: '2026-08-01T00:00:00', date_to: '2026-09-01T00:00:00' };
    const query = { rule: 'guobiao', tier: 'beginner', game_type: 'dongfeng', ...dates };

    await t.test('inclusive start, exclusive end and every filter apply to both score and rank', async () => {
      const result = await fetchPlayerRankStats(101, query);
      assert.deepEqual(result, {
        total_games: 2, total_round_score: 80,
        first_place_count: 1, second_place_count: 0, third_place_count: 0, fourth_place_count: 1,
        details_available: true, analyzed_games: 2, ...selectedMetrics,
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
        assert.equal(result.details_available, true);
        assert.equal(result.analyzed_games, games);
        const includesStart = !['banzhuang'].includes(filters.game_type) && ['beginner', 'rank'].includes(filters.tier);
        for (const [i, key] of metricKeys.entries()) {
          assert.equal(result[key], endMetrics[i] * games + (includesStart ? startMetrics[i] - endMetrics[i] : 0), key);
        }
      }
    });

    await t.test('empty date ranges return zero without historical scores', async () => {
      const result = await fetchPlayerRankStats(101, { ...query, date_from: '2026-12-01', date_to: '2026-12-02' });
      assert.equal(result.total_games, 0);
      assert.equal(result.total_round_score, 0);
      assert.equal(result.details_available, true);
      assert.equal(result.analyzed_games, 0);
      for (const key of metricKeys) assert.equal(result[key], 0, key);
    });

    await t.test('rule, sub-rule, raw scene filters and one-sided dates select the same metrics', async () => {
      await pool.query("UPDATE game_player_records SET sub_rule='guobiao/lanshi' WHERE game_id='end'");
      try {
        for (const filters of [
          { ...query, sub_rule: 'guobiao' },
          { ...query, tier: null, room_type: 'match', match_tier: 'beginner', sub_rule: 'guobiao' },
          { ...query, date_to: null, sub_rule: 'guobiao/lanshi' },
          { ...query, date_from: null, sub_rule: 'guobiao/lanshi' },
        ]) {
          const result = await fetchPlayerRankStats(101, filters);
          const expected = filters.sub_rule === 'guobiao' ? startMetrics : endMetrics;
          assert.equal(result.total_games, 1);
          for (const [i, key] of metricKeys.entries()) assert.equal(result[key], expected[i], key);
        }
        const otherRule = await fetchPlayerRankStats(101, { ...query, rule: 'qingque' });
        assert.equal(otherRule.total_games, 0);
        assert.equal(otherRule.total_rounds, 0);
      } finally {
        await pool.query("UPDATE game_player_records SET sub_rule='guobiao' WHERE game_id='end'");
      }
    });

    await t.test('duplicate metric rows use the latest snapshot without multiplying games or scores', async () => {
      await pool.query(`INSERT INTO game_player_metrics (id, game_id, user_id, ${metricKeys.join(',')})
        VALUES (-1, 'start', 101, 999,999,999,999,999,999,999,999,999)`);
      const result = await fetchPlayerRankStats(101, query);
      assert.equal(result.total_games, 2);
      assert.equal(result.total_round_score, 80);
      assert.equal(result.analyzed_games, 2);
      for (const key of metricKeys) assert.equal(result[key], selectedMetrics[key], key);
      await pool.query('DELETE FROM game_player_metrics WHERE id=-1');
    });

    await t.test('missing metrics keep settlement totals but do not expose partial details as complete', async () => {
      await pool.query("UPDATE game_player_metrics SET user_id=303 WHERE game_id='end' AND user_id=101");
      try {
        const result = await fetchPlayerRankStats(101, query);
        assert.equal(result.total_games, 2);
        assert.equal(result.total_round_score, 80);
        assert.equal(result.fourth_place_count, 1);
        assert.equal(result.analyzed_games, 1);
        assert.equal(result.details_available, false);
        for (const key of metricKeys) assert.equal(result[key], null, key);
        const missing = await fetchPlayerRankStats(101, { ...query, date_from: '2026-08-31' });
        assert.equal(missing.total_games, 1);
        assert.equal(missing.analyzed_games, 0);
        assert.equal(missing.details_available, false);
        for (const key of metricKeys) assert.equal(missing[key], null, key);
      } finally {
        await pool.query("UPDATE game_player_metrics SET user_id=101 WHERE user_id=303");
      }
    });

    await t.test('missing scores and rules with starting points remain unavailable', async () => {
      await pool.query("UPDATE game_player_records SET score = NULL WHERE game_id='start' AND user_id=101");
      assert.equal((await fetchPlayerRankStats(101, query)).total_round_score, null);
      await pool.query("UPDATE game_player_records SET rule='riichi', score=35000 WHERE game_id='start' AND user_id=101");
      const result = await fetchPlayerRankStats(101, { ...query, rule: 'riichi' });
      assert.equal(result.total_games, 1);
      assert.equal(result.total_round_score, null);
    });
    await t.test('riichi filtered details use only the same selected player and records', async () => {
      const stats = { total_games: 1, total_rounds: 4, total_round_score: 10000, win_count: 1,
        riichi_details: { riichi_round_count: 2, riichi_win_count: 1, net_score_count: 1 }, fan_stats: { riichi: 1 } };
      const { RIICHI_STATS_VERSION } = require('./riichiStats');
      await pool.query('INSERT INTO riichi_player_game_stats VALUES ($1, $2, $3, $4)', ['start', 101, RIICHI_STATS_VERSION, stats]);
      await pool.query('INSERT INTO riichi_player_game_stats VALUES ($1, $2, $3, $4)', ['outside', 101, RIICHI_STATS_VERSION, { ...stats, total_rounds: 999 }]);
      const result = await fetchPlayerRankStats(101, { ...query, rule: 'riichi' });
      assert.equal(result.details_available, true);
      assert.equal(result.total_games, 1);
      assert.equal(result.total_rounds, 4);
      assert.equal(result.total_round_score, 10000);
      assert.equal(result.riichi_details.riichi_round_count, 2);
      assert.equal(result.fan_stats.riichi, 1);
      const empty = await fetchPlayerRankStats(101, { ...query, rule: 'riichi', tier: 'custom' });
      assert.equal(empty.total_rounds, 0);
      assert.equal(empty.riichi_details.riichi_round_count, 0);
    });
  } finally {
    if (previousDatabase) require.cache[databasePath] = previousDatabase;
    else delete require.cache[databasePath];
    await pool.end();
  }
});
