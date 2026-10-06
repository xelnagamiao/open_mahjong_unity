const test = require('node:test');
const assert = require('node:assert/strict');
const { Pool } = require('pg');
const { RIICHI_STATS_VERSION, BASE_KEYS, DETAIL_KEYS } = require('./riichiStats');

test('home rule statistics keep personal denominators, subrules and every scene aligned', {
  skip: !process.env.RECORD_METADATA_TEST_DATABASE_URL,
}, async (t) => {
  const pool = new Pool({ connectionString: process.env.RECORD_METADATA_TEST_DATABASE_URL, max: 1 });
  const databasePath = require.resolve('../config/database');
  const previous = require.cache[databasePath];
  require.cache[databasePath] = { id: databasePath, filename: databasePath, loaded: true, exports: pool };
  const servicePath = require.resolve('./platformStats');
  const previousService = require.cache[servicePath];
  delete require.cache[servicePath];
  try {
    const metricKeys = BASE_KEYS.filter(key => key !== 'total_games');
    await pool.query(`CREATE TEMP TABLE game_records(game_id text PRIMARY KEY,record jsonb DEFAULT '{}');
      CREATE TEMP TABLE game_player_records(game_id text,user_id bigint,rule text,sub_rule text,
        room_type text,match_type text,match_tier text,event_id text,rank int,PRIMARY KEY(game_id,user_id));
      CREATE TEMP TABLE riichi_player_game_stats(game_id text,user_id bigint,version int,stats jsonb,PRIMARY KEY(game_id,user_id));
      CREATE TEMP TABLE game_player_metrics(id bigserial PRIMARY KEY,game_id text,user_id bigint,rule text,
        room_type text,sub_rule text,match_type text,match_tier text,game_type text,
        ${metricKeys.map(key => `${key} int DEFAULT 0`).join(',')});`);
    for (const rule of ['guobiao','qingque','classical','changsha','riichi']) {
      await pool.query(`CREATE TEMP TABLE ${rule}_history_stats(mode text,${BASE_KEYS.filter(key => !['cuohe_count','total_round_score'].includes(key)).map(key => `${key} int DEFAULT 0`).join(',')})`);
    }

    async function seed(id, { rule = 'riichi', subRule = 'riichi/standard', room = 'custom',
      mode = '1/4', tier = null, count = 4, rounds = 2, wins = [1,1,0,0],
      declarations = [1,1,0,0], fulu = [0,1,0,1], winPoints = [6000,6000,0,0],
      winTurns = [6,6,0,0], net = null, version = RIICHI_STATS_VERSION, missing = [], metrics = false } = {}) {
      await pool.query('INSERT INTO game_records(game_id) VALUES($1)', [id]);
      for (let seat = 0; seat < count; seat += 1) {
        await pool.query('INSERT INTO game_player_records VALUES($1,$2,$3,$4,$5,$6,$7,NULL,$8)',
          [id, 101 + seat, rule, subRule, room, mode, tier, seat + 1]);
        const stats = { ...Object.fromEntries(BASE_KEYS.map(key => [key, 0])), total_games: 1,
          total_rounds: rounds, win_count: wins[seat] || 0, total_win_turn: winTurns[seat] || 0,
          fulu_round_count: fulu[seat] || 0, total_round_score: net?.[seat] || 0,
          riichi_details: { ...Object.fromEntries(DETAIL_KEYS.map(key => [key, 0])),
            riichi_round_count: declarations[seat] || 0, total_win_points: winPoints[seat] || 0,
            net_score_count: 1, final_score_count: 1, final_score_total: 25000 + (net?.[seat] || 0) }, fan_stats: {} };
        if (rule === 'riichi' && !missing.includes(seat)) {
          await pool.query('INSERT INTO riichi_player_game_stats VALUES($1,$2,$3,$4)', [id, 101 + seat, version, stats]);
        }
        if (metrics) {
          await pool.query(`INSERT INTO game_player_metrics(game_id,user_id,rule,room_type,sub_rule,match_type,match_tier,game_type,${metricKeys.join(',')})
            VALUES($1,$2,$3,$4,$5,$6,$7,'quanzhuang',${metricKeys.map((_, i) => `$${i + 8}`).join(',')})`,
          [id, 101 + seat, rule, room, subRule, mode, tier, ...metricKeys.map(key => stats[key])]);
        }
      }
    }
    await seed('four-short');
    await seed('four-long', { rounds: 8, wins: [2,1,1,1], declarations: [1,1,1,1],
      fulu: [2,2,2,2], winPoints: [10000,6000,6000,6000], winTurns: [24,12,12,12] });
    await seed('three-modern', { subRule: 'riichi/sanma', count: 3, mode: '1/4_sanma', rounds: 4, wins: [1,1,1], fulu: [1,1,1] });
    await seed('three-old-mode', { subRule: null, count: 3, mode: '1/3', rounds: 4, wins: [1,1,1], fulu: [1,1,1] });
    await seed('rank-four', { room: 'match', tier: 'beginner', mode: '2/4_rank', metrics: true });
    await seed('rank-three', { subRule: 'riichi/sanma', count: 3, room: 'match', tier: 'advanced', mode: '2/4_sanma_rank', wins: [1,1,0] });
    await seed('event-missing', { room: 'events', missing: [0] });
    await seed('rank-stale', { room: 'match', tier: 'intermediate', mode: '1/4_rank', version: RIICHI_STATS_VERSION - 1 });
    await seed('partial-participants', { count: 2, mode: '4/4', rounds: 1, net: [1000,3000] });
    await seed('blood-battle', { rule: 'sichuan', subRule: 'sichuan/standard', room: 'match', tier: 'elo', mode: '4/4_rank', rounds: 1, wins: [1,1,1,0], metrics: true });
    await seed('blood-flow', { rule: 'sichuan', subRule: 'sichuan/xueliu_exchange', room: 'match', tier: 'elo', mode: '4/4_rank', rounds: 1, wins: [3,2,2,1], metrics: true });
    await seed('blood-flow-throw', { rule: 'sichuan', subRule: 'sichuan/xueliu', mode: '4/4' });
    await seed('hongque-event', { rule: 'hongque', subRule: 'hongque/v1.6', room: 'events', mode: '8/4' });
    await seed('guangdong-mil', { rule: 'guangdong', subRule: 'guangdong/mil2023', metrics: true, wins: [1,0,0,0] });
    await seed('guangdong-tuidao', { rule: 'guangdong', subRule: 'guangdong/tuidao_mil2024', metrics: true, wins: [1,1,1,0] });
    await seed('guangdong-legacy-default', { rule: 'guangdong', subRule: null, metrics: true, wins: [0,1,0,0] });
    await seed('zhongyong-standard', { rule: 'zhongyong', subRule: 'zhongyong/standard', metrics: true });
    await seed('nanque-new', { rule: 'zhongyong', subRule: 'zhongyong/nanque', metrics: true });
    await seed('nanque-legacy', { rule: 'jiandan', subRule: 'jiandan/standard', metrics: true });
    await seed('guobiao-three', { rule: 'guobiao', subRule: 'guobiao/sanma', count: 3, metrics: true });
    await seed('qingque-real', { rule: 'qingque', subRule: 'qingque/standard', room: 'match',
      mode: '4/4_rank', tier: 'beginner', metrics: true });
    await seed('guobiao-custom-low-fan', { rule: 'guobiao', subRule: 'guobiao/standard', metrics: true });
    await seed('guobiao-event-real', { rule: 'guobiao', subRule: 'guobiao/standard', room: 'events', metrics: true });
    await seed('shanghai-qiaoma', { rule: 'shanghai', subRule: 'shanghai/qiaoma', metrics: true, rounds: 2, wins: [1,0,0,0] });
    await seed('shanghai-qinghunpeng', { rule: 'shanghai', subRule: 'shanghai/qinghunpeng', metrics: true, rounds: 6, wins: [2,1,0,0] });
    await seed('shanghai-old-default', { rule: 'shanghai', subRule: null, metrics: true, rounds: 2, wins: [0,1,0,0] });
    await seed('shanghai-header-only', { rule: 'shanghai', subRule: null, metrics: true, rounds: 1, wins: [0,0,1,0] });
    await pool.query("UPDATE game_records SET record=$1 WHERE game_id='shanghai-header-only'", [{ game_title: { rule: 'shanghai', sub_rule: 'shanghai/qinghunpeng' } }]);
    await pool.query("INSERT INTO riichi_history_stats(mode,total_games,total_rounds,win_count) VALUES('1/4_rank',999,999,999)");
    await pool.query("INSERT INTO qingque_history_stats(mode,total_games,total_rounds) VALUES('4/4_rank',4,16)");
    // A legacy mixed accumulator is not an independent source of platform games.
    await pool.query("INSERT INTO guobiao_history_stats(mode,total_games,total_rounds) VALUES('1/4',999,999)");
    // An earlier bad metrics row must not add another player's sample.
    await pool.query("INSERT INTO game_player_metrics(game_id,user_id,rule,total_rounds,win_count) VALUES('blood-flow',101,'sichuan',1,99)");
    await pool.query("INSERT INTO game_player_metrics(game_id,user_id,rule,total_rounds,win_count) VALUES('blood-flow',101,'sichuan',1,3)");
    const { queryHomeHierarchyStats } = require('./platformStats');
    const { selectHomeStats, buildHomeStatsRows } = await import('../../client/src/utils/homeStats.js');
    const response = await queryHomeHierarchyStats();
    const rows = response.rows;
    const display = stats => Object.fromEntries(buildHomeStatsRows(stats).map(row => [row.label,row.value]));

    await t.test('four-player rates weight personal rounds and retain riichi details', async () => {
      const stats = selectHomeStats(rows, 'riichi', 'custom', 'dongfeng');
      assert.equal(stats.total_games, 2);
      assert.equal(stats.player_game_count, 8);
      assert.equal(stats.total_rounds, 40);
      assert.equal(stats.win_count, 7);
      const values = display(stats);
      assert.equal(values['和牌率'], '17.50%');
      assert.equal(values['副露率'], '25.00%');
      assert.equal(values['立直率'], '15.00%');
      assert.equal(values['平均和了打点'], '5714.29');
      assert.equal(values['平均和了巡目'], '10.29');
      assert.equal(values['总对局'], '2');
      assert.equal(values['总回合'], '40');
      assert.equal(values['平均和番'], undefined);
      assert.equal(values['立直后和牌率'], undefined);
    });
    await t.test('three-player statistics canonicalize old modes and remain isolated', async () => {
      const stats = selectHomeStats(rows, 'riichi_sanma', 'custom', 'dongfeng');
      assert.equal(stats.total_games, 2);
      assert.equal(stats.player_game_count, 6);
      assert.equal(stats.total_rounds, 24);
      assert.equal(display(stats)['和牌率'], '25.00%');
      assert.equal(display(stats)['四位率'], undefined);
      assert.equal(rows.filter(row => row.rule === 'riichi_sanma' && row.room_type === 'custom').length, 1);
    });
    await t.test('ranked data matches the correct rule and tier without history or metric duplication', async () => {
      const stats = selectHomeStats(rows, 'riichi', 'beginner', 'banzhuang');
      assert.equal(stats.total_games, 1);
      assert.equal(stats.total_rounds, 8);
      assert.equal(display(stats)['和牌率'], '25.00%');
      assert.equal(selectHomeStats(rows, 'riichi_sanma', 'advanced', 'banzhuang').total_games, 1);
      assert.equal(rows.filter(row => ['riichi','riichi_sanma'].includes(row.rule)).every(row => row.source === 'riichi_summary'), true);
      assert.equal(rows.find(row => row.rule === 'qingque').room_type, 'match');
      assert.equal(rows.filter(row => row.rule === 'qingque').length, 1);
      assert.equal(selectHomeStats(rows, 'guobiao', 'custom').total_games, 1);
      assert.equal(selectHomeStats(rows, 'guobiao', 'events').total_games, 1);
      assert.equal(rows.some(row => row.source === 'history'), false);
    });
    await t.test('missing or stale summaries keep available numeric counters and ranks', async () => {
      for (const scene of ['events','intermediate']) {
        const stats = selectHomeStats(rows, 'riichi', scene);
        assert.equal(stats.total_games, 1);
        assert.equal(stats.details_available, false);
        assert.match(display(stats)['和牌率'], /^\d+\.\d+%$/);
        assert.equal(display(stats)['立直率'], scene === 'events' ? '16.67%' : '0.00%');
        assert.equal(display(stats)['一位率'], '25.00%');
      }
    });
    await t.test('personal net-score denominator counts participants instead of tables', async () => {
      const stats = selectHomeStats(rows, 'riichi', 'custom', 'quanzhuang');
      assert.equal(stats.total_games, 1);
      assert.equal(stats.player_game_count, 2);
      assert.equal(display(stats)['局均点'], '2000.00');
    });
    await t.test('blood-flow and blood-battle Elo scenes remain separate and avoid repeated metrics', async () => {
      const battle = selectHomeStats(rows, 'sichuan', 'rank');
      const flow = selectHomeStats(rows, 'sichuan_xueliu_exchange', 'rank');
      assert.equal(battle.total_games, 1);
      assert.equal(flow.total_games, 1);
      assert.equal(battle.win_count, 3);
      assert.equal(flow.win_count, 8);
      assert.equal(display(flow)['局均和牌次数'], '8.00');
      assert.equal(selectHomeStats(rows, 'sichuan_xueliu_exchange', 'custom').total_games, 0);
      assert.equal(selectHomeStats(rows, 'sichuan_xueliu', 'custom').total_games, 1);
      assert.equal(display(selectHomeStats(rows, 'sichuan_xueliu', 'custom'))['局均和牌次数'], '0.00');
    });
    await t.test('rules outside the shortcut tabs expose saved games and ranks', async () => {
      const stats = selectHomeStats(rows, 'hongque', 'events');
      assert.equal(stats.total_games, 1);
      assert.equal(display(stats)['平均顺位'], '2.50');
      assert.equal(display(stats)['和牌率'], '0.00%');
    });
    await t.test('Guangdong profiles, Nanque, standard Zhongyong and three-player Guobiao remain separate', async () => {
      assert.equal(selectHomeStats(rows, 'guangdong', 'custom').win_count, 1);
      assert.equal(selectHomeStats(rows, 'guangdong_tuidao', 'custom').win_count, 4);
      assert.equal(selectHomeStats(rows, 'guangdong_tuidao', 'custom').total_games, 2);
      assert.equal(selectHomeStats(rows, 'zhongyong', 'custom').total_games, 1);
      assert.equal(selectHomeStats(rows, 'nanque', 'custom').total_games, 2);
      assert.equal(rows.some(row => row.rule === 'jiandan'), false);
      const three = selectHomeStats(rows, 'guobiao_sanma', 'custom');
      assert.equal(three.total_games, 1);
      assert.equal(display(three)['四位率'], undefined);
    });
    await t.test('Shanghai Qiaoma and Qinghunpeng read their own records including old metadata', async () => {
      const qiaoma = selectHomeStats(rows,'shanghai','custom');
      const qinghunpeng = selectHomeStats(rows,'shanghai_qinghunpeng','custom');
      assert.equal(qiaoma.total_games,2);
      assert.equal(qiaoma.win_count,2);
      assert.equal(qiaoma.total_rounds,4);
      assert.equal(qinghunpeng.total_games,2);
      assert.equal(qinghunpeng.win_count,4);
      assert.equal(qinghunpeng.total_rounds,7);
    });
    await t.test('an older database without summary or metrics tables keeps basic record data', async () => {
      await pool.query('DROP TABLE riichi_player_game_stats');
      const { queryRiichiHomeStats } = require('./riichiHomeStats');
      const fallback = await queryRiichiHomeStats(pool);
      const stats = selectHomeStats(fallback, 'riichi', 'custom', 'dongfeng');
      assert.equal(stats.total_games, 2);
      assert.equal(display(stats)['立直率'], '0.00%');
      await pool.query('DROP TABLE game_player_metrics');
      const { queryRecordHomeStats } = require('./recordHomeStats');
      const records = await queryRecordHomeStats(pool, ['sichuan']);
      assert.equal(selectHomeStats(records, 'sichuan_xueliu_exchange', 'rank').total_games, 1);
    });
  } finally {
    if (previous) require.cache[databasePath] = previous; else delete require.cache[databasePath];
    if (previousService) require.cache[servicePath] = previousService; else delete require.cache[servicePath];
    await pool.end();
  }
});
