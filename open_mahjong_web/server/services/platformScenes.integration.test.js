const test = require('node:test');
const assert = require('node:assert/strict');
const { Pool } = require('pg');
const { STATISTICS_RULES, statisticsLadderTiers } = require('./statisticsCatalog');

test('server scene capabilities agree with the frontend and matchmaking catalog', async () => {
  const client = await import('../../client/src/utils/statisticsScenes.js');
  for (const rule of STATISTICS_RULES.filter(row => row.basic_stats)) {
    assert.deepEqual(statisticsLadderTiers(rule.key), client.statisticsLadderTiers(rule.key), rule.key);
    assert.equal(statisticsLadderTiers(rule.key).length > 0, rule.matchmaking, rule.key);
  }
});

test('platform custom statistics, fans, daily games and record pages stay in the selected scope', {
  skip: !process.env.RECORD_METADATA_TEST_DATABASE_URL,
}, async () => {
  const pool = new Pool({ connectionString: process.env.RECORD_METADATA_TEST_DATABASE_URL, max: 1 });
  const databasePath = require.resolve('../config/database');
  const servicePath = require.resolve('./platformStats');
  const previousDatabase = require.cache[databasePath];
  const previousService = require.cache[servicePath];
  require.cache[databasePath] = { id: databasePath, filename: databasePath, loaded: true, exports: pool };
  delete require.cache[servicePath];
  const metricKeys = ['total_rounds', 'win_count', 'self_draw_count', 'deal_in_count', 'total_fan_score',
    'total_win_turn', 'total_fangchong_score', 'first_place_count', 'second_place_count',
    'third_place_count', 'fourth_place_count', 'fulu_round_count', 'cuohe_count', 'total_round_score'];
  try {
    // Session-local tables shadow the real tables; no persistent test data is written.
    await pool.query(`CREATE TEMP TABLE game_records(game_id text PRIMARY KEY, created_at timestamptz);
      CREATE TEMP TABLE game_player_records(game_id text, user_id bigint, username text, score int, rank int,
        pt_change numeric, rule text, sub_rule text, room_type text, match_type text, match_tier text, event_id text);
      CREATE TEMP TABLE game_player_metrics(game_id text, user_id bigint, created_at timestamptz, room_type text,
        match_tier text, event_id text, rule text, sub_rule text, game_type text, fan_stats jsonb,
        ${metricKeys.map(key => `${key} int DEFAULT 0`).join(',')});
      CREATE TEMP TABLE scene_daily_stats(stat_date date, room_type text, match_tier text, event_id text,
        rule text, sub_rule text, game_type text, total_games int);
      CREATE TEMP TABLE scene_tier_fan_daily(stat_date date, match_tier text, rule text, sub_rule text, fan_field text, fan_count int);
      CREATE TEMP TABLE events(event_id text, name text);
      INSERT INTO events VALUES ('evt_test', '测试比赛');`);

    async function seed(id, { rule = 'guobiao', subRule = `${rule}/standard`, room = 'custom', tier = null,
      event = null, time = '2026-10-28T12:00:00+08:00', day = '2026-10-28' } = {}) {
      await pool.query('INSERT INTO game_records VALUES($1,$2)', [id, time]);
      for (let seat = 1; seat <= 4; seat += 1) {
        await pool.query(`INSERT INTO game_player_records VALUES($1,$2,$3,0,$4,0,$5,$6,$7,'1/4',$8,$9)`,
          [id, 10000100 + seat, `玩家${seat}`, seat, rule, subRule, room, tier, event]);
        await pool.query(`INSERT INTO game_player_metrics(game_id,user_id,created_at,room_type,match_tier,event_id,
          rule,sub_rule,game_type,fan_stats,total_rounds,win_count,first_place_count)
          VALUES($1,$2,$3,$4,$5,$6,$7,$8,'dongfeng',$9,4,1,$10)`,
        [id, 10000100 + seat, time, room, tier, event, rule, subRule, { observed_fan: 1 }, seat === 1 ? 1 : 0]);
      }
      await pool.query("INSERT INTO scene_daily_stats VALUES($1,$2,$3,$4,$5,$6,'dongfeng',1)",
        [day, room, tier, event, rule, subRule]);
      if (room !== 'custom') {
        await pool.query('INSERT INTO scene_tier_fan_daily VALUES($1,$2,$3,$4,$5,4)',
          [day, tier, rule, subRule, 'observed_fan']);
      }
    }
    await seed('custom-null');
    await seed('custom-legacy-tier', { tier: 'beginner' });
    await seed('beginner', { room: 'match', tier: 'beginner' });
    await seed('unsupported-elo', { room: 'match', tier: 'elo' });
    await seed('event', { room: 'events', tier: 'evt_test', event: 'evt_test' });
    await seed('different-subrule', { subRule: 'guobiao/sanma' });
    // 03:59 belongs to the preceding statistical day; 04:00 starts the next one.
    await seed('before-cutoff', { time: '2026-10-29T03:59:59+08:00' });
    await seed('after-cutoff', { time: '2026-10-29T04:00:00+08:00', day: '2026-10-29' });
    await seed('classical', { rule: 'classical' });
    await seed('classical-old-match', { rule: 'classical', room: 'match', tier: 'beginner' });
    await seed('sichuan-custom', { rule: 'sichuan' });
    await seed('sichuan-elo', { rule: 'sichuan', room: 'match', tier: 'elo' });

    const service = require('./platformStats');
    const scope = { rule: 'guobiao', roomType: 'all', asOfDate: '2026-10-28' };
    const totals = await service.querySceneTotals(scope);
    assert.deepEqual(totals.map(row => row.match_tier), ['beginner', 'custom', 'total']);
    assert.equal(totals.find(row => row.match_tier === 'custom').total_games, 3);
    assert.equal(totals.find(row => row.match_tier === 'custom').total_rounds, 12);
    assert.equal(totals.find(row => row.match_tier === 'total').total_games, 4);
    const custom = await service.querySceneTotals({ ...scope, roomType: 'custom' });
    assert.deepEqual(custom.map(row => row.match_tier), ['custom', 'total']);
    assert.equal(custom[1].win_count, 12);
    const fans = await service.querySceneTotalsFans(scope);
    assert.deepEqual(Object.keys(fans), ['beginner', 'intermediate', 'advanced', 'mcrpl', 'custom', 'total']);
    assert.equal(fans.custom.observed_fan, 12);
    assert.equal(fans.total.observed_fan, 16);
    assert.equal((await service.querySceneTotalsFans({ ...scope, roomType: 'custom' })).total.observed_fan, 12);
    const daily = await service.querySceneDailyGames({ ...scope, dateFrom: '2026-10-28', dateTo: '2026-10-28' });
    assert.deepEqual(daily, [
      { stat_date: '2026-10-28', match_tier: 'beginner', total_games: 1 },
      { stat_date: '2026-10-28', match_tier: 'custom', total_games: 3 },
    ]);
    assert.equal((await service.querySceneDailyGames({ ...scope, roomType: 'custom' }))[0].total_games, 3);

    const first = await service.queryRecentLadderRecords({ rule: 'guobiao', roomType: 'custom', limit: 2 });
    const second = await service.queryRecentLadderRecords({ rule: 'guobiao', roomType: 'custom', limit: 2, offset: 2 });
    assert.equal(first.total, 4); // Recent records deliberately include today's games.
    assert.equal(second.total, 4);
    const records = [...first.items, ...second.items];
    assert.equal(new Set(records.map(row => row.game_id)).size, 4);
    assert(records.every(row => row.room_type === 'custom' && row.players.length === 4));
    const allRecords = await service.queryRecentLadderRecords({ rule: 'guobiao', roomType: 'all' });
    assert.equal(allRecords.total, 5);
    assert(!allRecords.items.some(row => ['event', 'unsupported-elo', 'different-subrule'].includes(row.game_id)));
    for (const eventId of ['evt_test', 'all']) {
      const events = { ...scope, eventId };
      assert.equal((await service.querySceneTotals(events)).at(-1).total_games, 1);
      assert.equal((await service.querySceneTotalsFans(events)).total.observed_fan, 4);
      assert.equal((await service.querySceneDailyGames(events))[0].match_tier, 'evt_test');
      assert.equal((await service.queryRecentLadderRecords(events)).items[0].game_id, 'event');
    }
    for (const [rule, tiers, total] of [
      ['classical', ['custom', 'total'], 1],
      ['sichuan', ['elo', 'custom', 'total'], 2],
    ]) {
      const options = { ...scope, rule };
      const rows = await service.querySceneTotals(options);
      assert.deepEqual(rows.map(row => row.match_tier), tiers);
      assert.equal(rows.at(-1).total_games, total);
      assert.deepEqual(Object.keys(await service.querySceneTotalsFans(options)), tiers);
      assert.equal((await service.queryRecentLadderRecords(options)).total, total);
    }
    assert.deepEqual(await service.querySceneTotals({ ...scope, rule: 'hongque_w' }), []);
    assert.deepEqual(await service.querySceneDailyGames({ ...scope, rule: 'hongque_w' }), []);
  } finally {
    if (previousDatabase) require.cache[databasePath] = previousDatabase;
    else delete require.cache[databasePath];
    if (previousService) require.cache[servicePath] = previousService;
    else delete require.cache[servicePath];
    await pool.end();
  }
});
