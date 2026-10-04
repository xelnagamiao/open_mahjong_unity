const test = require('node:test')
const assert = require('node:assert/strict')
const { randomUUID } = require('node:crypto')

test('real PostgreSQL migrations and Elo settlements in disposable schemas',
  { skip: process.env.GUESS_FAN_TEST_DATABASE !== '1' }, async t => {
  require('dotenv').config()
  const config = require('../config/config')
  assert.ok(['localhost','127.0.0.1','::1'].includes(config.db.host), 'Tests require a local database')
  const { Pool } = require('pg')
  const connection={host:config.db.host,user:config.db.user,password:config.db.password,database:config.db.database,port:config.db.port}
  const admin=new Pool(connection)
  async function fixture(run) {
    const schema='guess_fan_test_'+randomUUID().replaceAll('-','')
    await admin.query('CREATE SCHEMA '+schema)
    const pool=new Pool({...connection, options:'-c search_path='+schema})
    const databasePath=require.resolve('../config/database')
    const modulePath=require.resolve('./guessFanTables')
    const previous=require.cache[databasePath]
    require.cache[databasePath]={id:databasePath,filename:databasePath,loaded:true,exports:pool}
    delete require.cache[modulePath]
    const store=require('./guessFanTables')
    try {await run(pool,store)} finally {
      delete require.cache[modulePath]
      if(previous)require.cache[databasePath]=previous;else delete require.cache[databasePath]
      await pool.end();await admin.query('DROP SCHEMA '+schema+' CASCADE')
    }
  }
  const oldTable=`CREATE TABLE guess_fan_ratings (
    user_id BIGINT NOT NULL, rule_set VARCHAR(16) NOT NULL DEFAULT 'mixed',username VARCHAR(64) NOT NULL,
    wins INTEGER NOT NULL DEFAULT 0,matches INTEGER NOT NULL DEFAULT 0,rating INTEGER NOT NULL DEFAULT 1000,
    streak INTEGER NOT NULL DEFAULT 0,best_streak INTEGER NOT NULL DEFAULT 0,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,PRIMARY KEY(user_id,rule_set))`
  const players=[{userId:1,username:'one'},{userId:2,username:'two'}]
  try {
    await t.test('all pools migrate once, preserve other data, and retain exact originals',()=>fixture(async(db,s)=>{
      await db.query(oldTable)
      await db.query("INSERT INTO guess_fan_ratings (user_id,rule_set,username,rating,wins,matches,streak,best_streak) VALUES (1,'mixed','one',1100,7,12,2,4),(1,'riichi','one',900,3,10,1,3),(2,'mixed','two',1000,0,0,0,0)")
      const before=(await db.query('SELECT * FROM guess_fan_ratings ORDER BY rule_set,user_id')).rows
      await Promise.all([s.ensureGuessFanTables(),s.ensureGuessFanTables()])
      const after=(await db.query('SELECT * FROM guess_fan_ratings ORDER BY rule_set,user_id')).rows
      assert.deepEqual(after,before.map(r=>({...r,rating:1500+(r.rating-1000)*5})))
      const marker=(await db.query('SELECT * FROM guess_fan_rating_migrations')).rows
      assert.equal(marker.length,1);assert.equal(marker[0].previous_rows.length,3)
      assert.deepEqual(marker[0].previous_rows.map(r=>r.rating),before.map(r=>r.rating))
      await s.ensureGuessFanTables()
      assert.deepEqual((await db.query('SELECT * FROM guess_fan_ratings ORDER BY rule_set,user_id')).rows,after)
      assert.equal((await s.fetchLeaderboardTop(20,'mixed'))[0].rating,2000)
      assert.equal((await s.fetchLeaderboardTop(20,'riichi'))[0].rating,1000)
    }))
    await t.test('fresh database defaults to 1500 and settles both winner positions',()=>fixture(async(db,s)=>{
      await s.ensureGuessFanTables()
      await db.query("INSERT INTO guess_fan_ratings (user_id,username) VALUES (9,'default')")
      assert.equal((await db.query('SELECT rating FROM guess_fan_ratings WHERE user_id=9')).rows[0].rating,1500)
      let rows=await s.applyMatchRating({winnerUserId:1,players})
      assert.deepEqual(rows.map(r=>r.rating),[1516,1484])
      rows=await s.applyMatchRating({winnerUserId:2,players,ruleSet:'riichi'})
      assert.deepEqual(rows.map(r=>r.rating),[1484,1516])
      assert.equal(rows[1].wins,1)
      assert.equal(rows[0].matches,1)
      await db.query("UPDATE guess_fan_ratings SET rating=CASE WHEN user_id=1 THEN 2100 ELSE 1500 END WHERE rule_set='mixed' AND user_id IN(1,2)")
      rows=await s.applyMatchRating({winnerUserId:1,players})
      assert.deepEqual(rows.map(r=>r.rating),[2111,1489])
      assert.equal(rows[0].rating+rows[1].rating,3600)
      await db.query("UPDATE guess_fan_ratings SET rating=0 WHERE user_id=9")
      assert.equal((await s.fetchLeaderboardTop()).find(r=>r.userId==='9').rating,0)
    }))
    await t.test('old single-pool schema remains upgradeable',()=>fixture(async(db,s)=>{
      await db.query(oldTable.replace("rule_set VARCHAR(16) NOT NULL DEFAULT 'mixed',",'').replace('PRIMARY KEY(user_id,rule_set)','PRIMARY KEY(user_id)'))
      await db.query("INSERT INTO guess_fan_ratings(user_id,username,rating) VALUES(1,'legacy',1200)")
      await s.ensureGuessFanTables()
      assert.equal((await s.fetchLeaderboardTop())[0].rating,2500)
    }))
    await t.test('failed migration rolls back originals and marker; retry converts once',()=>fixture(async(db,s)=>{
      await db.query(oldTable)
      await db.query("INSERT INTO guess_fan_ratings(user_id,username,rating) VALUES(1,'legacy',1200)")
      await db.query('ALTER TABLE guess_fan_ratings ADD CONSTRAINT fail_migration CHECK(rating<2000)')
      await assert.rejects(s.ensureGuessFanTables())
      assert.equal((await db.query('SELECT rating FROM guess_fan_ratings')).rows[0].rating,1200)
      assert.equal((await db.query("SELECT to_regclass('guess_fan_rating_migrations') AS name")).rows[0].name,null)
      await db.query('ALTER TABLE guess_fan_ratings DROP CONSTRAINT fail_migration')
      await s.ensureGuessFanTables();assert.equal((await s.fetchLeaderboardTop())[0].rating,2500)
    }))
    await t.test('failed settlement rolls back both players',()=>fixture(async(db,s)=>{
      await s.ensureGuessFanTables()
      await db.query('ALTER TABLE guess_fan_ratings ADD CONSTRAINT fail_settlement CHECK(rating>=1500)')
      await assert.rejects(s.applyMatchRating({winnerUserId:1,players}))
      assert.equal((await db.query('SELECT count(*)::int AS count FROM guess_fan_ratings')).rows[0].count,0)
    }))
  } finally {await admin.end()}
})
