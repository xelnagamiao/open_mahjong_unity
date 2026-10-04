const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { Pool } = require('pg');
const { TITLE_SCHEMA_SQL, ensureTitleTables } = require('../utils/titleTables');

test('title schema agrees between Python and Node', () => {
  const normalize = sql => sql.replace(/--[^\n]*/g, '').replace(/\s+/g, ' ').trim();
  assert.equal(normalize(TITLE_SCHEMA_SQL), normalize(fs.readFileSync(path.resolve(__dirname, '../../../open_mahjong_server/server/database/title_schema.sql'), 'utf8')));
});

test('PostgreSQL title management, authorization and rollback', { skip: !process.env.TITLES_TEST_DATABASE_URL }, async t => {
  const connectionString = process.env.TITLES_TEST_DATABASE_URL;
  const schema = `title_test_${process.pid}_${Date.now()}`;
  const admin = new Pool({ connectionString });
  const db = new Pool({ connectionString, options: `-c search_path=${schema}`, max: 8 });
  await admin.query(`CREATE SCHEMA ${schema}`);
  // The imported audit helper/router use this isolated pool exclusively.
  const databasePath = require.resolve('../config/database');
  const previous = require.cache[databasePath];
  require.cache[databasePath] = { id:databasePath, filename:databasePath, loaded:true, exports:db };
  const service = require('./titles');
  const config = require('../config/config');
  const adminId = [...config.admin.userIds][0];
  const uid = 700000101;
  try {
    await db.query(`CREATE TABLE users (user_id bigint PRIMARY KEY, username text);
      CREATE TABLE user_settings (user_id bigint PRIMARY KEY REFERENCES users ON DELETE CASCADE, title_id int DEFAULT 1, updated_at timestamp DEFAULT NOW());
      CREATE TABLE admin_audit_log (id bigserial PRIMARY KEY,admin_user_id bigint,action text,target_type text,target_id text,payload jsonb,reason text,created_at timestamp DEFAULT NOW());`);
    await db.query('INSERT INTO users VALUES ($1, $2), ($3, $4);', [adminId,'test-admin',uid,'test-player']);
    await db.query('INSERT INTO user_settings (user_id,title_id) VALUES ($1,2)', [uid]);
    await ensureTitleTables(db);
    await ensureTitleTables(db);
    await t.test('migration retains legacy equipment without duplicate grants', async () => {
      assert.equal((await service.userTitles(db,uid)).length,1);
      assert.equal((await service.userTitles(db,uid))[0].equipped,true);
    });
    await t.test('reject malformed IDs, rich text and blank reasons', async () => {
      for (const invalid of ['3oops',0,-1,2.5,{},true]) assert.throws(() => service.id(invalid));
      assert.throws(() => service.definition({name:'<b>管理员</b>'}));
      assert.throws(() => service.definition({name:'x',is_enabled:'false'}));
      await assert.rejects(service.saveTitle(db,adminId,null,{name:'测试',reason:'  '}), /原因/);
    });
    const body = {name:'测试冠军',description:'赛事授予',sort_order:5,is_enabled:true,reason:'集成测试'};
    const {title} = await service.saveTitle(db,adminId,null,body);
    await t.test('created IDs avoid sentinel and legacy IDs', async () => {
      assert.ok(title.title_id >= 3);
      assert.equal((await service.listTitles(db)).length,2);
    });
    await t.test('duplicate names and missing targets reject without partial grants', async () => {
      await assert.rejects(service.saveTitle(db,adminId,null,body), {code:'23505'});
      await assert.rejects(service.changeGrant(db,adminId,700000999,title.title_id,{reason:'test'},true), /用户不存在/);
      await assert.rejects(service.changeGrant(db,adminId,uid,9999,{reason:'test'},true), /头衔不存在/);
    });
    await t.test('repeated grant is idempotent and does not change equipment', async () => {
      assert.equal((await service.changeGrant(db,adminId,uid,title.title_id,{reason:'test'},true)).changed,true);
      assert.equal((await service.changeGrant(db,adminId,uid,title.title_id,{reason:'again'},true)).changed,false);
      assert.equal((await db.query('SELECT title_id FROM user_settings WHERE user_id=$1',[uid])).rows[0].title_id,2);
    });
    await t.test('revoke atomically clears worn title and survives migration', async () => {
      await service.changeGrant(db,adminId,uid,2,{reason:'撤销测试'},false);
      assert.equal((await db.query('SELECT title_id FROM user_settings WHERE user_id=$1',[uid])).rows[0].title_id,1);
      await ensureTitleTables(db);
      assert.deepEqual((await service.userTitles(db,uid)).map(t => t.title_id),[title.title_id]);
    });
    await t.test('disable unequips, preserves ownership, and rejects new grants', async () => {
      await db.query('UPDATE user_settings SET title_id=$1 WHERE user_id=$2',[title.title_id,uid]);
      await service.saveTitle(db,adminId,title.title_id,{...body,is_enabled:false});
      assert.equal((await db.query('SELECT title_id FROM user_settings WHERE user_id=$1',[uid])).rows[0].title_id,1);
      assert.equal((await service.userTitles(db,uid))[0].is_enabled,false);
      await assert.rejects(service.changeGrant(db,adminId,uid,title.title_id,{reason:'test'},true), /停用/);
      await service.saveTitle(db,adminId,title.title_id,{...body,name:'新冠军'});
      assert.equal((await service.userTitles(db,uid))[0].equipped,false);
    });
    await t.test('audit failure rolls the business mutation back', async () => {
      await db.query("ALTER TABLE admin_audit_log ADD CONSTRAINT test_reason CHECK (reason <> 'fail-audit')");
      await assert.rejects(service.saveTitle(db,adminId,title.title_id,{...body,name:'不应写入',reason:'fail-audit'}));
      assert.equal((await db.query('SELECT name FROM titles WHERE title_id=$1',[title.title_id])).rows[0].name,'新冠军');
      assert.ok((await db.query('SELECT COUNT(*)::int AS count FROM admin_audit_log')).rows[0].count >= 6);
    });
    await t.test('real HTTP middleware rejects anonymous and ordinary users', async () => {
      const express = require('express');
      const {requireAdmin} = require('../middleware/requireAdmin');
      const {signToken} = require('../utils/jwt');
      const app = express(); app.use(express.json()); app.use('/titles',requireAdmin,require('../routes/admin/titles'));
      const server = app.listen(0,'127.0.0.1'); await new Promise(resolve => server.once('listening',resolve));
      const base = `http://127.0.0.1:${server.address().port}/titles`;
      try {
        assert.equal((await fetch(base)).status,401);
        const ordinary=signToken({user_id:uid},config.admin.jwtSecret,60);
        assert.equal((await fetch(base,{method:'POST',headers:{Authorization:`Bearer ${ordinary}`,'Content-Type':'application/json'},body:JSON.stringify(body)})).status,403);
        const token=signToken({user_id:adminId},config.admin.jwtSecret,60);
        const response=await fetch(base,{headers:{Authorization:`Bearer ${token}`}});
        assert.equal(response.status,200);
        assert.equal((await response.json()).data.length,2);
      } finally { await new Promise(resolve => server.close(resolve)); }
    });
  } finally {
    if(previous) require.cache[databasePath]=previous; else delete require.cache[databasePath];
    await db.end();
    await admin.query(`DROP SCHEMA ${schema} CASCADE`);
    await admin.end();
  }
});
