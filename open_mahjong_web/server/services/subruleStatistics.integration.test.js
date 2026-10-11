const test = require('node:test');
const assert = require('node:assert/strict');
const { Pool } = require('pg');
const { STATISTICS_RULES } = require('./statisticsCatalog');
const { BASE_KEYS, RIICHI_STATS_VERSION } = require('./riichiStats');

test('every statistics selection isolates its subrule and fan capability', {
  skip: !process.env.RECORD_METADATA_TEST_DATABASE_URL,
}, async () => {
  const pool = new Pool({ connectionString: process.env.RECORD_METADATA_TEST_DATABASE_URL, max: 1 });
  const path = require.resolve('../config/database');
  const previous = require.cache[path];
  require.cache[path] = { id: path, filename: path, loaded: true, exports: pool };
  const servicePath = require.resolve('./playerPublicApi');
  delete require.cache[servicePath];
  try {
    const fields = BASE_KEYS.filter(k => k !== 'total_games');
    await pool.query(`CREATE TEMP TABLE game_records(game_id text PRIMARY KEY,record jsonb,created_at timestamptz DEFAULT NOW());
      CREATE TEMP TABLE game_player_records(game_id text,user_id bigint,rule text,sub_rule text,room_type text,match_type text,match_tier text,event_id text,rank int);
      CREATE TEMP TABLE game_player_metrics(id bigserial PRIMARY KEY,game_id text,user_id bigint,rule text,sub_rule text,
        room_type text,match_type text,match_tier text,game_type text,fan_stats jsonb,${fields.map(k=>`${k} int DEFAULT 0`).join(',')});
      CREATE TEMP TABLE riichi_player_game_stats(game_id text,user_id bigint,version int,stats jsonb);`);
    for (const [i, def] of STATISTICS_RULES.entries()) {
      if (!def.basic_stats) continue;
      const gid = `game${i}`;
      await pool.query('INSERT INTO game_records(game_id,record) VALUES($1,$2)', [gid,{game_title:{rule:def.rule,sub_rule:def.sub_rule}}]);
      await pool.query("INSERT INTO game_player_records VALUES($1,10000101,$2,$3,'custom','1/4',NULL,NULL,1)",[gid,def.rule,def.sub_rule]);
      await pool.query(`INSERT INTO game_player_metrics(game_id,user_id,rule,sub_rule,room_type,match_type,fan_stats,total_rounds,win_count,first_place_count)
        VALUES($1,10000101,$2,$3,'custom','1/4',$4,100,$5,1)`,[gid,def.rule,def.sub_rule,def.fan_stats?{test_fan:i+1}:null,i+1]);
      if (def.rule === 'riichi') await pool.query('INSERT INTO riichi_player_game_stats VALUES($1,10000101,$2,$3)',[gid,RIICHI_STATS_VERSION,
        {total_games:1,total_rounds:100,win_count:i+1,first_place_count:1,riichi_details:{riichi_round_count:1},fan_stats:{test_fan:i+1}}]);
    }
    const { fetchPlayerRankStats, queryRuleStats } = require('./playerPublicApi');
    const { homeRuleOptions, selectHomeStats } = await import('../../client/src/utils/homeStats.js');
    const options = homeRuleOptions([{rule:'free'}]);
    assert.equal(options.length,STATISTICS_RULES.filter(row => row.basic_stats).length);
    assert(!options.some(r => r.value === 'free'));
    const { queryRecordHomeStats } = require('./recordHomeStats');
    const { queryRiichiHomeStats } = require('./riichiHomeStats');
    const home = [...await queryRecordHomeStats(pool,['riichi'],{exclude:true}),...await queryRiichiHomeStats(pool)];
    for (const [i, def] of STATISTICS_RULES.entries()) {
      if (!def.basic_stats) continue;
      const stats = await fetchPlayerRankStats(10000101,{rule:def.key,tier:'custom'});
      assert.equal(stats.total_games,1,def.key);
      assert.equal(stats.win_count,i+1,def.key);
      assert.equal(stats.supports_fan_stats,def.fan_stats);
      assert.equal('fan_stats' in stats,def.fan_stats);
      if (def.fan_stats) assert.equal(stats.fan_stats.test_fan, i+1);
      assert.equal(selectHomeStats(home,def.key,'custom').win_count,i+1);
      assert.equal(options.find(r=>r.value===def.key).label,def.stats_name);
      const rows = await queryRuleStats(10000101,def.key);
      assert.equal(rows.length,1);
      assert.equal(rows[0].sub_rule,def.sub_rule);
    }
    // 新增番种不必先登记到国标字典；平台按子规则保留其计数。
    await pool.query(`CREATE TEMP TABLE scene_tier_fan_daily(stat_date date,match_tier text,rule text,sub_rule text,fan_field text,fan_count int);
      INSERT INTO scene_tier_fan_daily VALUES
        ('2026-01-01','elo','hongque','hongque/v1.6','清顺',3),
        ('2026-01-01','elo','sichuan','sichuan/doubles','清一色',2),
        ('2026-01-01','elo','sichuan','sichuan/xueliu_doubles','清一色',7);`);
    const platformPath = require.resolve('./platformStats');
    delete require.cache[platformPath];
    const { querySceneTotalsFans } = require('./platformStats');
    assert.equal((await querySceneTotalsFans({rule:'hongque'})).total['清顺'], 3);
    assert.equal((await querySceneTotalsFans({rule:'sichuan_doubles'})).total['清一色'], 2);
    assert.equal((await querySceneTotalsFans({rule:'sichuan_xueliu_doubles'})).total['清一色'], 7);
    assert.deepEqual(await querySceneTotalsFans({rule:'nanque'}), {});
    for (const hands of [4,8,12,16]) {
      const gid = `hongque${hands}`;
      await pool.query('INSERT INTO game_records(game_id,record) VALUES($1,$2)', [gid,{game_title:{rule:'hongque',sub_rule:'hongque/v1.6'}}]);
      await pool.query("INSERT INTO game_player_records VALUES($1,10000101,'hongque','hongque/v1.6','custom',$2,NULL,NULL,1)",[gid,`${hands}/4`]);
      await pool.query(`INSERT INTO game_player_metrics(game_id,user_id,rule,sub_rule,room_type,match_type,fan_stats,total_rounds,win_count)
        VALUES($1,10000101,'hongque','hongque/v1.6','custom',$2,$3,$4,1)`,[gid,`${hands}/4`,{'清顺':hands},hands]);
      const filtered = await fetchPlayerRankStats(10000101,{rule:'hongque',tier:'custom',game_type:`hands${hands}`});
      assert.equal(filtered.total_games,1);
      assert.equal(filtered.total_rounds,hands);
      assert.equal(filtered.fan_stats['清顺'],hands);
    }
    for (const [gid, mode] of [['hongque-rebuilt','16/4_rank'],['hongque-new','16/4_rank']]) {
      await pool.query('INSERT INTO game_records(game_id,record) VALUES($1,$2)', [gid,{game_title:{rule:'hongque',sub_rule:'hongque/v1.6'}}]);
      await pool.query("INSERT INTO game_player_records VALUES($1,10000101,'hongque','hongque/v1.6','match',$2,'elo',NULL,1)",[gid,mode]);
      await pool.query(`INSERT INTO game_player_metrics(game_id,user_id,rule,sub_rule,room_type,match_type,fan_stats,total_rounds,win_count)
        VALUES($1,10000101,'hongque','hongque/v1.6','match',$2,'{"清顺":1}',16,1)`,[gid,mode]);
    }
    const rankedHongque = await fetchPlayerRankStats(10000101,{rule:'hongque',tier:'rank',game_type:'hands16'});
    assert.equal(rankedHongque.total_games,2);
    assert.equal(rankedHongque.total_rounds,32);
    assert.equal((await fetchPlayerRankStats(10000101,{rule:'hongque',tier:'rank',game_type:'hands4'})).total_games,0);
    const hongqueRows = await queryRuleStats(10000101,'hongque');
    assert.equal(hongqueRows.filter(row => row.mode === '16/4_rank').length,1);
    assert.equal(hongqueRows.find(row => row.mode === '16/4_rank').total_games,2);
    assert(!hongqueRows.some(row => row.mode === '4/4_rank'));
    const hongqueHome = await queryRecordHomeStats(pool,['hongque']);
    assert.equal(selectHomeStats(hongqueHome,'hongque','rank','hands16').total_games,2);
    assert.equal(selectHomeStats(hongqueHome,'hongque','custom','hands4').total_games,1);
    delete require.cache[platformPath];
  } finally {
    await pool.end();
    delete require.cache[servicePath];
    if (previous) require.cache[path] = previous; else delete require.cache[path];
  }
});
