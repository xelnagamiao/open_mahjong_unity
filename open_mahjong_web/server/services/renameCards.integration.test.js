const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createRequire } = require('node:module');
const { Pool } = require('pg');
const express = require('express');

const dsn = process.env.RENAME_CARDS_TEST_DATABASE_URL;

test('account rename consumes inventory cards transactionally in PostgreSQL', { skip: !dsn }, async t => {
  const schema = 'rename_cards_test_' + crypto.randomUUID().replaceAll('-', '');
  const admin = new Pool({ connectionString: dsn });
  await admin.query(`CREATE SCHEMA ${schema}`);
  const pool = new Pool({ connectionString: dsn, options: `-c search_path=${schema}`, max: 8 });
  let server;
  try {
    await pool.query(`CREATE TABLE users (user_id bigint PRIMARY KEY, username text NOT NULL UNIQUE,
      is_tourist boolean DEFAULT FALSE, email text, email_verified_at timestamp,
      rename_count integer NOT NULL DEFAULT 0);
      CREATE TABLE game_player_records (game_id text, user_id bigint REFERENCES users, username text);
      CREATE TABLE game_records (game_id text PRIMARY KEY, record jsonb);`);
    await pool.query(fs.readFileSync(path.join(__dirname, '../../../open_mahjong_server/server/database/inventory_schema.sql'), 'utf8'));
    await pool.query(`INSERT INTO item_definitions(item_id,code,name,category,asset_key,max_quantity)
      VALUES (3001,'rename_coupon','改名卡','item','item.rename_credit',1000000),
             (4001,'custom_rename','活动改名卡','item','item.rename_credit',1000000)`);

    const authPath = path.join(__dirname, '../routes/player/auth.js');
    const realRequire = createRequire(authPath);
    const overrides = {
      '../../config/database': pool,
      '../../config/config': {
        playerAuth: { audience: 'player-test', jwtSecret: 'test-only-secret', jwtExpiresSec: 3600 },
        eventAdmin: { audience: 'event-test', jwtSecret: 'event-test-secret', jwtExpiresSec: 60 },
        calcServer: { baseUrl: 'http://127.0.0.1', timeoutMs: 100 }, smtp: { enabled: false },
      },
      '../../middleware/requirePlayer': { requirePlayer(req, _res, next) {
        req.player = { userId: Number(req.headers['x-test-user'] || 101), username: 'OldName',
          expiresAt: Math.floor(Date.now() / 1000) + 3600 };
        next();
      } },
      '../../middleware/rateLimit': { createWindowLimiter: () => (_req, _res, next) => next() },
      '../../utils/eventAdminHelpers': { listUserEvents: async () => [] },
      '../../utils/passwordReset': { createPasswordResetHandlers: () => ({ send() {}, reset() {} }) },
      '../../utils/mailer': {},
      '../../utils/loginAccount': {},
      '../../services/loginHistory': {},
    };
    const context = {
      module: { exports: {} }, require: id => overrides[id] || realRequire(id),
      console: { error() {} }, Date, AbortController, setTimeout, clearTimeout,
      fetch: async () => ({ status: 200, text: async () => '{"online":true}' }),
    };
    vm.runInNewContext(fs.readFileSync(authPath, 'utf8'), context, { filename: authPath });
    const app = express(); app.use(express.json()); app.use('/auth', context.module.exports);
    server = app.listen(0, '127.0.0.1');
    await new Promise(resolve => server.once('listening', resolve));
    const base = `http://127.0.0.1:${server.address().port}`;
    async function rename(name, uid = 101) {
      const res = await fetch(base + '/auth/rename', { method: 'POST',
        headers: { 'Content-Type': 'application/json', 'x-test-user': String(uid) },
        body: JSON.stringify({ new_username: name }) });
      return { status: res.status, body: await res.json() };
    }
    async function reset(cards = 2) {
      await pool.query(`TRUNCATE users, game_records CASCADE;
        INSERT INTO users(user_id,username,is_tourist,rename_count)
        VALUES (101,'OldName',FALSE,7),(102,'TakenName',FALSE,0),(103,'GuestName',TRUE,0);
        UPDATE item_definitions SET use_enabled=TRUE,grant_enabled=TRUE;`);
      if (cards) await pool.query('INSERT INTO user_inventory(user_id,item_id,quantity) VALUES(101,3001,$1)', [cards]);
    }
    async function balance(uid = 101, iid = 3001) {
      const res = await pool.query('SELECT quantity FROM user_inventory WHERE user_id=$1 AND item_id=$2', [uid, iid]);
      return res.rows[0]?.quantity || 0;
    }
    async function operationCount() {
      return Number((await pool.query('SELECT COUNT(*) FROM inventory_operations')).rows[0].count);
    }

    await t.test('success debits one card, records the ledger, and updates history and account payload', async () => {
      await reset();
      await pool.query(`INSERT INTO game_records VALUES ('g1','{"game_title":{"p0_uid":101,"p0_name":"OldName"}}');
        INSERT INTO game_player_records VALUES ('g1',101,'OldName')`);
      const res = await rename('NewName');
      assert.equal(res.status, 200);
      assert.equal(res.body.data.rename_card_count, 1);
      assert.equal(res.body.data.rename_card_usable_count, 1);
      assert.equal(res.body.data.rename_count, undefined);
      assert.equal(await balance(), 1);
      assert.deepEqual((await pool.query('SELECT username,rename_count FROM users WHERE user_id=101')).rows[0],
        { username: 'NewName', rename_count: 7 });
      assert.equal((await pool.query('SELECT username FROM game_player_records')).rows[0].username, 'NewName');
      assert.equal((await pool.query('SELECT record FROM game_records')).rows[0].record.game_title.p0_name, 'NewName');
      assert.deepEqual((await pool.query('SELECT item_id,delta,quantity_after FROM inventory_ledger')).rows,
        [{ item_id: 3001, delta: -1, quantity_after: 1 }]);
      assert.equal((await pool.query('SELECT action,result FROM inventory_operations')).rows[0].action, 'rename');
    });
    for (const [label, name, uid, cards, status] of [
      ['same name', 'OldName', 101, 2, 400], ['duplicate name', 'TakenName', 101, 2, 409],
      ['invalid name', 'A', 101, 2, 400], ['guest', 'NewName', 103, 2, 403],
      ['missing user', 'NewName', 999, 2, 404], ['no cards despite legacy credits', 'NewName', 101, 0, 400],
    ]) {
      await t.test(`${label} leaves cards and operations unchanged`, async () => {
        await reset(cards);
        assert.equal((await rename(name, uid)).status, status);
        assert.equal(await balance(), cards);
        assert.equal(await operationCount(), 0);
        assert.equal((await pool.query('SELECT username FROM users WHERE user_id=101')).rows[0].username, 'OldName');
      });
    }
    await t.test('disabled cards are preserved and enabled custom cards can pay for rename', async () => {
      await reset(3);
      await pool.query(`UPDATE item_definitions SET use_enabled=FALSE WHERE item_id=3001;
        INSERT INTO user_inventory(user_id,item_id,quantity) VALUES(101,4001,2)`);
      const res = await rename('NewName');
      assert.equal(res.status, 200);
      assert.equal(await balance(), 3);
      assert.equal(await balance(101, 4001), 1);
      assert.equal(res.body.data.rename_card_count, 4);
      assert.equal(res.body.data.rename_card_usable_count, 1);
      assert.equal((await pool.query('SELECT item_id FROM inventory_ledger')).rows[0].item_id, 4001);
    });
    await t.test('all disabled cards reject rename without spending them', async () => {
      await reset();
      await pool.query('UPDATE item_definitions SET use_enabled=FALSE');
      assert.equal((await rename('NewName')).status, 400);
      assert.equal(await balance(), 2);
      assert.equal(await operationCount(), 0);
    });
    await t.test('the built-in card is spent first and stopping grants does not prevent use', async () => {
      await reset(1);
      await pool.query(`UPDATE item_definitions SET grant_enabled=FALSE WHERE item_id=3001;
        UPDATE item_definitions SET sort_order=-1 WHERE item_id=4001;
        INSERT INTO user_inventory(user_id,item_id,quantity) VALUES(101,4001,2)`);
      assert.equal((await rename('NewName')).status, 200);
      assert.equal(await balance(), 0);
      assert.equal(await balance(101, 4001), 2);
    });
    await t.test('history failure rolls back the username, card debit, operation and ledger', async () => {
      await reset();
      await pool.query(`INSERT INTO game_player_records VALUES ('g1',101,'OldName');
        ALTER TABLE game_player_records ADD CONSTRAINT reject_name CHECK(username<>'BrokenName')`);
      try {
        assert.equal((await rename('BrokenName')).status, 500);
        assert.equal(await balance(), 2);
        assert.equal(await operationCount(), 0);
        assert.equal((await pool.query('SELECT username FROM users WHERE user_id=101')).rows[0].username, 'OldName');
      } finally { await pool.query('ALTER TABLE game_player_records DROP CONSTRAINT reject_name'); }
    });
    await t.test('two concurrent renames cannot spend the last card twice', async () => {
      await reset(1);
      const results = await Promise.all([rename('FirstName'), rename('OtherName')]);
      assert.deepEqual(results.map(r => r.status).sort(), [200, 400]);
      assert.equal(await balance(), 0);
      assert.equal(await operationCount(), 1);
    });
    await t.test('repeated submission of the same new name spends one card', async () => {
      await reset(2);
      const results = await Promise.all([rename('FirstName'), rename('FirstName')]);
      assert.deepEqual(results.map(r => r.status).sort(), [200, 400]);
      assert.equal(await balance(), 1);
      assert.equal(await operationCount(), 1);
    });
    await t.test('a username uniqueness race rolls back the losing debit', async () => {
      await reset(1);
      await pool.query('INSERT INTO user_inventory(user_id,item_id,quantity) VALUES(102,3001,1)');
      const results = await Promise.all([rename('RacedName', 101), rename('RacedName', 102)]);
      assert.deepEqual(results.map(r => r.status).sort(), [200, 409]);
      assert.equal(await balance(101) + await balance(102), 1);
      assert.equal(await operationCount(), 1);
    });
  } finally {
    if (server) await new Promise(resolve => server.close(resolve));
    await pool.end();
    await admin.query(`DROP SCHEMA ${schema} CASCADE`);
    await admin.end();
  }
});
