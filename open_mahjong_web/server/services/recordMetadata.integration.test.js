const test = require('node:test');
const assert = require('node:assert/strict');
const { Pool } = require('pg');
const { readFileSync } = require('node:fs');
const path = require('node:path');

// Explicit database only. All tables/indexes live in this run's private schema.
test('record metadata is public while locked content remains protected', {
  skip: !process.env.RECORD_METADATA_TEST_DATABASE_URL,
}, async (t) => {
  process.env.ADMIN_USER_IDS ||= '1';
  process.env.ADMIN_JWT_EXPIRES_SEC ||= '3600';
  process.env.ADMIN_JWT_SECRET ||= 'metadata-test-admin';
  process.env.PLAYER_JWT_SECRET ||= 'metadata-test-player';
  process.env.BOT_API_JWT_SECRET ||= 'metadata-test-bot';
  const connectionString = process.env.RECORD_METADATA_TEST_DATABASE_URL;
  const schema = `record_metadata_test_${process.pid}_${Date.now()}`;
  const admin = new Pool({ connectionString });
  await admin.query(`CREATE SCHEMA ${schema}`);
  const options = `-c search_path=${schema}`;
  const pool = new Pool({ connectionString, options });
  const databasePath = require.resolve('../config/database');
  const previousDatabase = require.cache[databasePath];
  require.cache[databasePath] = { id: databasePath, filename: databasePath, loaded: true, exports: pool };
  let server;
  try {
    await pool.query(`
      CREATE TABLE users (user_id bigint PRIMARY KEY, username text);
      CREATE TABLE events (event_id varchar(32) PRIMARY KEY, name text, description text,
        status text DEFAULT 'active', kind text DEFAULT 'event', entry_config jsonb,
        reopen_requested boolean, created_by bigint, closed_at timestamp,
        created_at timestamp DEFAULT now(), updated_at timestamp DEFAULT now());
      CREATE TABLE event_admins (event_id varchar(32), user_id bigint, role text);
      CREATE TABLE game_records (game_id varchar(16) PRIMARY KEY, record jsonb, created_at timestamp NOT NULL);
      CREATE TABLE game_player_records (game_id varchar(16), user_id bigint, username text,
        score int, rank int, pt_change numeric(12,2), original_player_index int, title_used int, character_used int,
        profile_used int, voice_used int, rule text, sub_rule text, room_type text,
        match_type text, match_tier text, event_id varchar(32), is_favorite boolean DEFAULT false,
        note text, PRIMARY KEY(game_id,user_id));
      INSERT INTO users VALUES (101,'Test player'),(201,'Other player');
      INSERT INTO events (event_id,name) VALUES ('evtA','Test event');
      INSERT INTO event_admins VALUES ('evtA',101,'owner');
    `);
    await require('../utils/duplicateTables').ensureDuplicateTables(pool);
    const walls = await pool.query(`INSERT INTO duplicate_walls
      (key,owner_user_id,scope,wall_type,rule,tiles,seed,is_unlocked)
      VALUES ('DUP_locked',101,'personal','key','guobiao','[11,12]','SECRET_SEED',false),
             ('DUP_open',101,'personal','key','guobiao','[11,12]','OPEN_SEED',true)
      RETURNING id,key`);
    const ids = Object.fromEntries(walls.rows.map((r) => [r.key, r.id]));
    const fixtures = [
      ['aNormal','match','beginner',101,null,null],
      ['bLocked','match','beginner',101,'DUP_locked','DUP_locked'],
      ['cUnlocked','match','intermediate',101,'DUP_open','DUP_open'],
      ['dTitleOnly','match','beginner',101,'DUP_locked',null],
      ['eOrphan','custom',null,101,'DUP_missing',null],
      ['fEvent','events',null,101,'DUP_locked','DUP_locked'],
      ['gOther','match','beginner',201,null,null],
    ];
    for (const [gameId, room, tier, owner, key, link] of fixtures) {
      const title = { rule: 'guobiao', ...(key ? { is_duplicate: true, duplicate_key: key, duplicate_seed: 'SECRET_SEED', duplicate_tiles: [11,12] } : {}) };
      await pool.query('INSERT INTO game_records VALUES($1,$2,$3)', [gameId,{ game_title: title, game_round: {} },'2026-09-27 12:00:00']);
      if (link) await pool.query('INSERT INTO duplicate_games(game_id,wall_id) VALUES($1,$2)', [gameId,ids[link]]);
      for (let seat=0; seat<4; seat++) {
        await pool.query(`INSERT INTO game_player_records
          (game_id,user_id,username,score,rank,original_player_index,rule,sub_rule,room_type,match_type,match_tier,event_id,is_favorite,pt_change)
          VALUES($1,$2,$3,$4,$5,$6,'guobiao','guobiao',$7,'normal',$8,$9,$10,$11)`,
          [gameId,owner+seat,`Player ${owner+seat}`,100-seat*10,seat+1,seat,room,tier,room==='events'?'evtA':null,gameId==='bLocked',room==='match'?[11.76,0,-5.15,null][seat]:null]);
      }
    }
    const { queryRecentLadderRecords } = require('./platformStats');
    const { fetchPlayerRecords, fetchPlayerRankStats, fetchPlayerScopeCounts } = require('./playerPublicApi');
    const { getPublicGameRecord } = require('./publicGameRecord');
    const { accessibleGameIds, recordIsLocked } = require('./duplicateRecordAccess');
    const safeMetadata = (data) => {
      const text = JSON.stringify(data);
      for (const secret of ['SECRET_SEED','OPEN_SEED','DUP_locked','DUP_missing','"record":','"game_round":','"duplicate_tiles":']) assert.equal(text.includes(secret),false,secret);
    };

    await t.test('all tiers, locked summaries, deterministic pages, empty-page total and event scope', async () => {
      const result = await queryRecentLadderRecords({ limit: 2 });
      assert.equal(result.total,5);
      assert.deepEqual(result.items.map((r)=>r.game_id),['gOther','dTitleOnly']);
      assert.equal(result.items[1].players.length,4);
      safeMetadata(result);
      const page = await queryRecentLadderRecords({ offset:2,limit:2 });
      assert.deepEqual(page.items.map((r)=>r.game_id),['cUnlocked','bLocked']);
      safeMetadata(page);
      assert.equal((await queryRecentLadderRecords({matchTier:'beginner'})).total,4);
      const empty = await queryRecentLadderRecords({offset:100});
      assert.equal(empty.total,5); assert.deepEqual(empty.items,[]);
      for (const eventId of ['evtA','all']) {
        const event = await queryRecentLadderRecords({eventId});
        assert.equal(event.total,1); assert.equal(event.items[0].game_id,'fEvent'); safeMetadata(event);
      }
    });

    await t.test('personal and Bot shared lists, ID filters and rank statistics include locked summaries', async () => {
      const result = await fetchPlayerRecords(101,{},0,20);
      assert.equal(result.total,6); assert.ok(result.items.some((r)=>r.game_id==='bLocked'));
      assert.ok(result.items.some((r)=>r.game_id==='eOrphan')); safeMetadata(result);
      const empty = await fetchPlayerRecords(101,{},100,20);
      assert.equal(empty.total,6); assert.deepEqual(empty.items,[]);
      const rank = await fetchPlayerRankStats(101,{});
      assert.equal(rank.total_games,6); assert.equal(rank.first_place_count,6);
      assert.equal((await fetchPlayerRecords(101,{tier:'beginner'},0,20)).total,3);
    });

    await t.test('combined scene counts preserve filtered totals and empty results', async () => {
      for (const query of [{}, {rule:'guobiao'}, {rule:'riichi'}, {date_from:'2026-09-28'}, {date_to:'2026-09-28'}, {game_type:'dongfeng'}]) {
        const result = await fetchPlayerScopeCounts(101, query);
        for (const tier of ['rank','custom','beginner','intermediate','advanced','mcrpl','elo','events']) {
          assert.equal(result[tier], (await fetchPlayerRankStats(101,{...query,tier})).total_games,JSON.stringify({query,tier}));
        }
      }
    });

    const express = require('express');
    const config = require('../config/config');
    const { signToken } = require('../utils/jwt');
    const { requireEventAdmin } = require('../middleware/requireEventAdmin');
    const app = express(); app.use(express.json());
    app.use('/platform',require('../routes/platform'));
    app.use('/player',require('../routes/player/records2d'));
    app.use('/player',require('../routes/player'));
    app.use('/bot',require('../routes/botapi'));
    app.use('/events',requireEventAdmin,require('../routes/event-admin/events'));
    server = app.listen(0,'127.0.0.1');
    await new Promise((resolve)=>server.once('listening',resolve));
    const base = `http://127.0.0.1:${server.address().port}`;
    const playerToken = signToken({user_id:101,aud:config.playerAuth.audience},config.playerAuth.jwtSecret,3600);
    const eventToken = signToken({user_id:101,aud:config.eventAdmin.audience},config.eventAdmin.jwtSecret,3600);
    const request = (url,token=playerToken,body) => fetch(base+url,{
      method:body?'POST':'GET',headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json'},
      ...(body?{body:JSON.stringify(body)}:{}),
    });
    await t.test('real personal, favourite, Bot and event HTTP lists retain authentication boundaries', async () => {
      assert.equal((await fetch(base+'/player/my-records')).status,401);
      const mine = await request('/player/my-records');assert.equal(mine.status,200);
      const data = (await mine.json()).data;assert.equal(data.total,6);safeMetadata(data);
      const favourite = (await (await request('/player/my-records?favorites_only=1')).json()).data;
      assert.deepEqual(favourite.items.map((r)=>r.game_id),['bLocked']);safeMetadata(favourite);
      const idList = (await (await request('/player/record-ids/101')).json()).data;
      assert.ok(idList.items.some((r)=>r.game_id==='bLocked'));safeMetadata(idList);
      const botToken = signToken({aud:'botapi',bot_name:'test'},config.botApi.jwtSecret,3600);
      const bot = await request('/bot/player/records/101',botToken);assert.equal(bot.status,200);safeMetadata((await bot.json()).data);
      assert.equal((await fetch(base+'/bot/player/records/101')).status,401);
      const event = await request('/events/evtA/records',eventToken);assert.equal(event.status,200);
      const eventData=(await event.json()).data;assert.equal(eventData.total,1);assert.equal(eventData.items[0].game_id,'fEvent');safeMetadata(eventData);
      const outsider=signToken({user_id:999,aud:config.eventAdmin.audience},config.eventAdmin.jwtSecret,3600);
      assert.equal((await request('/events/evtA/records',outsider)).status,403);
    });

    await t.test('Web, QQ Bot, 2D and platform return saved numeric PT, retaining zero and null', async () => {
      const botToken = signToken({aud:'botapi',bot_name:'pt-test'},config.botApi.jwtSecret,3600);
      for (const [route,token] of [['/player/records/101',playerToken],['/bot/player/records/101',botToken],['/player/my-records',playerToken],['/platform/recent-records',playerToken]]) {
        const response=await request(route,token);assert.equal(response.status,200,route);
        const body=(await response.json()).data;
        const item=body.items.find(item=>item.game_id==='aNormal');assert.ok(item,route);
        assert.deepEqual(item.players.map(player=>player.pt_change),[11.76,0,-5.15,null],route);
        safeMetadata(body);
      }
      const counts=await request('/player/scope-counts/101?rule=guobiao');assert.equal(counts.status,200);
      assert.deepEqual((await counts.json()).data,{rank:4,custom:1,beginner:3,intermediate:1,advanced:0,mcrpl:0,elo:0,events:1});
    });

    await t.test('public replay, single JSON, batch JSON and ZIP still reject locked records', async () => {
      for (const gameId of ['bLocked','dTitleOnly','eOrphan','fEvent']) {
        assert.equal(await recordIsLocked(pool,gameId),true);
        for (const route of ['/platform/record/','/platform/unity-record/','/player/record/']) {
          const r=await request(route+gameId);assert.equal(r.status,403,route+gameId);assert.equal((await r.text()).includes('SECRET_SEED'),false);
        }
      }
      assert.deepEqual(await accessibleGameIds(pool,['aNormal','bLocked','cUnlocked']),['aNormal','cUnlocked']);
      for (const endpoint of ['/player/records/fetch-json','/player/records/download']) {
        const r=await request(endpoint,playerToken,{target_user_id:101,user_id:101,game_ids:['bLocked','dTitleOnly']});
        assert.equal(r.status,404,endpoint);assert.equal((await r.text()).includes('SECRET_SEED'),false);
      }
      assert.equal((await request('/events/evtA/record/fEvent',eventToken)).status,403);
      for (const body of [{game_ids:['fEvent']},{}]) {
        const r=await request('/events/evtA/records/download',eventToken,body);
        assert.equal(r.status,403);assert.equal((await r.text()).includes('SECRET_SEED'),false);
      }
      assert.equal((await getPublicGameRecord('aNormal')).status,200);
      assert.equal((await getPublicGameRecord('cUnlocked')).status,200);
      await pool.query("UPDATE duplicate_walls SET is_unlocked=true WHERE key='DUP_locked'");
      assert.equal((await getPublicGameRecord('bLocked')).status,200);
      assert.equal((await getPublicGameRecord('dTitleOnly')).status,200);
      assert.equal((await queryRecentLadderRecords({})).total,5);
    });

    await t.test('classic summaries do not need full-record access', async () => {
      await pool.query("UPDATE duplicate_walls SET is_unlocked=false WHERE key='DUP_locked'");
      const result = await require('./classicRecordsStore').attachRecordMeta([{game_id:'bLocked'}]);
      assert.equal(result.length,1);assert.equal(result[0].players.length,4);safeMetadata(result);
    });

    await t.test('database deadline cancels work and resets after rollback; reads cannot write', async () => {
      const { withRecordMetadataQuery }=require('./recordMetadataQuery');
      const single=new Pool({connectionString,options,max:1});
      try {
        const before=(await single.query('SHOW statement_timeout')).rows[0].statement_timeout;
        await assert.rejects(withRecordMetadataQuery(single,(db)=>db.query('SELECT pg_sleep(6)')), {code:'57014'});
        assert.equal((await single.query('SHOW statement_timeout')).rows[0].statement_timeout,before);
        await assert.rejects(withRecordMetadataQuery(single,(db)=>db.query('DELETE FROM game_records')), {code:'25006'});
        assert.equal((await single.query('SELECT count(*)::int AS count FROM game_records')).rows[0].count,7);
      } finally {await single.end();}
    });

    await t.test('explicit concurrent indexes are valid and repeatable in an isolated schema', async () => {
      const sql=readFileSync(path.join(__dirname,'../../scripts/migrations/20260927_record_metadata_indexes.sql'),'utf8');
      const statements=sql.replace(/--[^\n]*/g,'').split(';').map((s)=>s.trim()).filter(Boolean);
      // One dedicated connection keeps session limits scoped to this migration.
      const client=await pool.connect();
      try {for(let repeat=0;repeat<2;repeat++) for(const statement of statements) await client.query(statement);}
      finally {client.release();}
      const result=await pool.query(`SELECT i.indisvalid FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid
        JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=$1 AND c.relname LIKE 'idx_game_%'`,[schema]);
      assert.equal(result.rowCount,4);assert.ok(result.rows.every((r)=>r.indisvalid));
      assert.equal((await queryRecentLadderRecords({})).total,5);
    });
  } finally {
    if(server) await new Promise((resolve)=>server.close(resolve));
    if(previousDatabase) require.cache[databasePath]=previousDatabase; else delete require.cache[databasePath];
    await pool.end();
    await admin.query(`DROP SCHEMA ${schema} CASCADE`);
    await admin.end();
  }
});
