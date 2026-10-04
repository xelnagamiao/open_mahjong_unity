const test = require('node:test');
const assert = require('node:assert/strict');
const { Pool } = require('pg');

test('Bot player API exposes saved multi-rule data without replay contents', {
  skip: !process.env.RECORD_METADATA_TEST_DATABASE_URL,
}, async (t) => {
  process.env.BOT_API_JWT_SECRET ||= 'bot-contract-test';
  const connectionString = process.env.RECORD_METADATA_TEST_DATABASE_URL;
  const schema = `bot_player_test_${process.pid}_${Date.now()}`;
  const admin = new Pool({ connectionString });
  await admin.query(`CREATE SCHEMA ${schema}`);
  const pool = new Pool({ connectionString, options: `-c search_path=${schema}` });
  const databasePath = require.resolve('../config/database');
  const previousDatabase = require.cache[databasePath];
  require.cache[databasePath] = { id: databasePath, filename: databasePath, loaded: true, exports: pool };
  let server;
  try {
    await pool.query(`
      CREATE TABLE users(user_id bigint PRIMARY KEY, username text);
      CREATE TABLE user_settings(user_id bigint PRIMARY KEY, title_id int, profile_image_id int, character_id int, voice_id int);
      CREATE TABLE rank_data(user_id bigint, guobiao_rank text, guobiao_score numeric, updated_at timestamp);
      CREATE TABLE rule_ratings(user_id bigint, rule text, rank_name text, rank_score numeric, elo numeric, games int, updated_at timestamp);
      CREATE TABLE game_records(game_id varchar(16) PRIMARY KEY, record jsonb, created_at timestamp);
      CREATE TABLE game_player_records(game_id varchar(16), user_id bigint, username text, score int, rank int,
        pt_change numeric(12,2), rule text, sub_rule text, match_type text, room_type text, match_tier text,
        event_id text, title_used int, character_used int, profile_used int, voice_used int, PRIMARY KEY(game_id,user_id));
      CREATE TABLE events(event_id text, name text);
      INSERT INTO users VALUES(101,'Bot test'),(102,'Player 2'),(103,'Player 3'),(104,'Player 4');
      INSERT INTO user_settings(user_id) VALUES(101),(102),(103),(104);
      INSERT INTO rank_data VALUES(101,'三段',850.5,'2026-10-04');
      INSERT INTO rule_ratings VALUES(101,'guobiao','9级',9,1550.25,8,'2026-10-04'),
        (101,'riichi','6级',60.5,1580.75,3,'2026-10-04'),(101,'qingque','10级',999,1650,2,'2026-10-04');
    `);
    const metricFields = ['total_rounds','win_count','self_draw_count','deal_in_count','total_fan_score',
      'total_win_turn','total_fangchong_score','first_place_count','second_place_count','third_place_count',
      'fourth_place_count','fulu_round_count','cuohe_count','total_round_score'];
    for (const rule of ['guobiao','riichi','qingque','classical','changsha','jiandan']) {
      await pool.query(`CREATE TABLE ${rule}_history_stats(user_id bigint, rule text, mode text, total_games int DEFAULT 0,
        ${metricFields.map(field => `${field} int DEFAULT 0`).join(',')})`);
      if (rule !== 'changsha') await pool.query(`CREATE TABLE ${rule}_fan_stats(user_id bigint, rule text, mode text, ${rule === 'jiandan' ? 'all_triplets' : 'fan'} int DEFAULT 0)`);
    }
    await pool.query(`
      INSERT INTO jiandan_history_stats(user_id,rule,mode,total_games,total_rounds,win_count) VALUES(101,'jiandan','4/4',2,8,3);
      INSERT INTO jiandan_fan_stats VALUES(101,'jiandan','4/4',3);
      INSERT INTO riichi_history_stats(user_id,rule,mode,total_games) VALUES(101,'riichi','1/4',1),(101,'riichi','1/4_rank',2);
      INSERT INTO riichi_fan_stats VALUES(101,'riichi','1/4',1),(101,'riichi','1/4_rank',4);
      CREATE TABLE game_player_metrics(user_id bigint, rule text, room_type text, match_type text,
        ${metricFields.map(field => `${field} int DEFAULT 0`).join(',')});
      INSERT INTO game_player_metrics(user_id,rule,room_type,match_type,total_rounds,win_count,total_round_score)
        VALUES(101,'riichi','match','1/4_rank',4,1,100),(101,'riichi','custom','1/4',8,6,999),
          (101,'sichuan','match','4/4_rank',4,2,-20);
    `);
    const makeResult = (rule, system) => Object.fromEntries([101,102,103,104].map((uid,i) => [uid, {
      rating_rule: rule, rating_system: system, rating_pt: [11.76,0,-5.15,null][i],
      rank_before: '三段', rank_after: '三段', score_before: 850.5, score_after: 862.26,
      elo_before: 1500, elo_after: [1516,1500,1494.85,1489.15][i],
      elo_delta: [16,0,-5.15,-10.85][i], rating_games: 2,
      wall: 'SECRET_WALL', duplicate_seed: 'SECRET_SEED',
    }]));
    const fixtures = [
      ['national','guobiao','guobiao/standard','match','intermediate',makeResult('guobiao','grade')],
      ['riichi','riichi','riichi/standard','match','beginner',makeResult('riichi','grade')],
      ['elo','qingque','qingque/standard','match','elo',makeResult('qingque','elo')],
      ['custom','qingque','qingque/standard','custom',null,makeResult('qingque','elo')],
      ['nanque','zhongyong','zhongyong/nanque','custom',null,null],
      ['standard','zhongyong','zhongyong/standard','custom',null,null],
      ['legacy','guobiao','guobiao/standard','match','beginner',null],
    ];
    for (const [id,rule,subRule,room,tier,result] of fixtures) {
      await pool.query('INSERT INTO game_records VALUES($1,$2,$3)', [id,{game_title:{rating_result:result,duplicate_seed:'SECRET_SEED'},game_round:{tiles_list:['SECRET_WALL']}},'2026-10-04']);
      for (let i=0;i<4;i++) await pool.query(`INSERT INTO game_player_records
        (game_id,user_id,username,score,rank,pt_change,rule,sub_rule,match_type,room_type,match_tier)
        VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)`,
      [id,101+i,`Player ${i}`,100-i*10,i+1,id==='legacy'?[11.76,0,-5.15,null][i]:null,rule,subRule,room==='match'?'1/4_rank':'1/4',room,tier]);
    }
    const express = require('express');
    const app = express();
    app.use('/api/player', require('../routes/player'));
    app.use('/api/bot', require('../routes/botapi'));
    server = app.listen(0,'127.0.0.1');
    await new Promise(resolve => server.once('listening',resolve));
    const config = require('../config/config');
    const token = require('../utils/jwt').signToken({aud:'botapi',bot_name:'contract-test'},config.botApi.jwtSecret,3600);
    const base = `http://127.0.0.1:${server.address().port}`;
    const get = async (route,auth=true) => {
      const response = await fetch(base+route,{headers:auth?{Authorization:`Bearer ${token}`}:{}});
      const body = await response.json().catch(()=>null);
      return {status:response.status,body};
    };
    const bot = route => get('/api/bot/player'+route);
    const safe = data => {
      const json = JSON.stringify(data);
      for (const word of ['SECRET_WALL','SECRET_SEED','game_round','rating_result','duplicate_seed']) assert.equal(json.includes(word),false,word);
    };

    await t.test('ratings preserve national overrides and return numeric independent defaults', async () => {
      const {status,body} = await bot('/rank/101');
      assert.equal(status,200);
      const rank = body.data;
      assert.equal(rank.guobiao_rank,'三段');assert.equal(rank.guobiao_score,850.5);
      assert.equal(rank.ratings.guobiao.rank_name,'三段');assert.equal(rank.ratings.guobiao.rank_score,850.5);
      assert.equal(rank.ratings.guobiao.elo,1550.25);assert.equal(rank.ratings.riichi.rank_score,60.5);
      assert.equal(rank.ratings.qingque.rank_name,'');assert.equal(rank.ratings.qingque.rank_score,0);
      assert.equal(rank.ratings.qingque.elo,1650);assert.equal(rank.ratings.qingque.bounds,null);
      assert.equal(rank.ratings.sichuan.elo,1500);assert.equal(rank.ratings.sichuan.games,0);
    });
    await t.test('info returns canonical Nanque, dictionary and separate ranked counters', async () => {
      const response = await bot('/info/101');assert.equal(response.status,200);
      const info=response.body.data;
      assert.equal(info.nanque_stats[0].rule,'zhongyong');assert.equal(info.nanque_stats[0].sub_rule,'zhongyong/nanque');
      assert.equal(info.nanque_stats[0].total_games,2);assert.equal(info.nanque_stats[0].fan_stats.all_triplets,3);
      assert.equal(info.fan_dict.nanque.all_triplets,'对对和');assert.equal(info.fan_dict.riichi.riichi,'立直');
      assert.equal(info.jiandan_stats,undefined);
      assert.equal(info.ranked_stats.riichi[0].total_rounds,4);assert.equal(info.ranked_stats.riichi[0].total_round_score,100);
      assert.equal(info.ranked_stats.sichuan[0].total_round_score,-20);
      assert.deepEqual(info.riichi_stats.map(row=>row.mode_fan_stats),[{fan:1},{fan:4}]);
      const publicInfo=await get('/api/player/info/101');assert.deepEqual(info,publicInfo.body.data);safe(info);
    });
    await t.test('Elo tiers, room plus tier, subrules, dates and empty pagination stay exact', async () => {
      for(const query of ['rule=qingque&tier=elo','rule=qingque&room_type=match&match_tier=elo','rule=qingque&tier=rank&match_tier=elo']) {
        const r=await bot(`/records/101?${query}`);assert.equal(r.status,200);
        assert.deepEqual(r.body.data.items.map(row=>row.game_id),['elo']);
        assert.equal((await bot(`/rank-stats/101?${query}`)).body.data.total_games,1);
      }
      const nanque=await bot('/records/101?rule=zhongyong&sub_rule=zhongyong/nanque');
      assert.deepEqual(nanque.body.data.items.map(row=>row.game_id),['nanque']);
      assert.equal((await bot('/records/101?rule=jiandan')).body.data.total,0);
      const empty=await bot('/records/101?rule=qingque&tier=elo&offset=999');
      assert.equal(empty.body.data.total,1);assert.deepEqual(empty.body.data.items,[]);
      assert.equal((await bot('/records/101?tier=elo&date_to=2026-10-04')).body.data.total,0);
      const counts=await bot('/scope-counts/101?rule=qingque');assert.equal(counts.status,200);
      assert.equal(counts.body.data.elo,1);assert.equal(counts.body.data.custom,1);
    });
    await t.test('saved PT and Elo retain negatives, zero and null, and never expose replay contents', async () => {
      const records=(await bot('/records/101')).body.data;safe(records);
      const find=id=>records.items.find(row=>row.game_id===id).players;
      assert.deepEqual(find('riichi').map(p=>p.pt_change),[11.76,0,-5.15,null]);
      assert.deepEqual(find('legacy').map(p=>p.pt_change),[11.76,0,-5.15,null]);
      assert.deepEqual(find('elo').map(p=>p.elo_delta),[16,0,-5.15,-10.85]);
      assert.ok(find('elo').every(p=>p.pt_change===null && p.rating_pt===null));
      assert.ok(find('custom').every(p=>p.elo_delta===null && p.rating_rule===null));
      assert.equal(find('riichi')[0].score_before,850.5);assert.equal(find('riichi')[0].rank_after,'三段');
      assert.deepEqual(records,(await get('/api/player/records/101')).body.data);
      await pool.query(`UPDATE game_records SET record=jsonb_set(record,'{game_title,rating_result,101,rating_rule}','"guobiao"') WHERE game_id='elo'`);
      const malformed=(await bot('/records/101?tier=elo')).body.data.items[0].players[0];
      assert.equal(malformed.rating_rule,null);assert.equal(malformed.elo_delta,null);
    });
    await t.test('all Bot routes remain authenticated and unknown users return 404', async () => {
      for(const route of ['/info/101','/records/101','/rank-stats/101','/rank/101','/scope-counts/101']) {
        assert.equal((await get('/api/bot/player'+route,false)).status,401);
        assert.equal((await bot(route.replace('101','999'))).status,404);
      }
      assert.equal((await bot('/info/Bot%20test')).status,200);
    });
  } finally {
    if(server) await new Promise(resolve=>server.close(resolve));
    if(previousDatabase) require.cache[databasePath]=previousDatabase;else delete require.cache[databasePath];
    await pool.end();
    await admin.query(`DROP SCHEMA ${schema} CASCADE`);
    await admin.end();
  }
});
