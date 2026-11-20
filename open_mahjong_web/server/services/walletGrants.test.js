const test = require('node:test');
const assert = require('node:assert/strict');
const { grantWallet, validateGrant } = require('./walletGrants');

const body = { amount_minor: 125, reason: '活动奖励', request_id: 'grant-request-001' };
test('reject invalid amounts, reasons and request IDs before connecting', async () => {
  for (const amount_minor of [0, -1, 1.5, '100', null, 100000001, Infinity]) {
    assert.throws(() => validateGrant({ ...body, amount_minor }));
  }
  for (const changes of [{ reason: ' ' }, { reason: 'x'.repeat(501) }, { request_id: '../bad' }]) {
    await assert.rejects(grantWallet({ connect() { throw Error('must not connect'); } }, 101, 1, { ...body, ...changes }), { status: 400 });
  }
  assert.equal(validateGrant({ ...body, amount_minor: 1 }).amount, 1);
  assert.equal(validateGrant({ ...body, amount_minor: 100000000 }).amount, 100000000);
});

test('PostgreSQL: concurrent retries, purchases, conflicts and rollback', { skip: !process.env.WALLET_TEST_DB }, async () => {
  const { Pool } = require('pg');
  const config = require('../config/config');
  const schema = `wallet_grant_test_${Date.now()}_${process.pid}`;
  const connection = { ...config.db, database: process.env.WALLET_TEST_DB };
  const control = new Pool(connection);
  let pool;
  try {
    await control.query(`CREATE SCHEMA ${schema}`);
    pool = new Pool({ ...connection, options: `-c search_path=${schema}`, max: 10 });
    await pool.query(`CREATE TABLE users(user_id BIGINT PRIMARY KEY,is_tourist BOOLEAN);
      CREATE TABLE user_wallets(user_id BIGINT PRIMARY KEY,balance_minor BIGINT NOT NULL DEFAULT 0 CHECK(balance_minor>=0),
        earned_minor BIGINT NOT NULL DEFAULT 0,spent_minor BIGINT NOT NULL DEFAULT 0,version BIGINT NOT NULL DEFAULT 0,updated_at TIMESTAMPTZ DEFAULT now());
      CREATE TABLE wallet_ledger(entry_id BIGSERIAL PRIMARY KEY,user_id BIGINT,source VARCHAR(32),source_id VARCHAR(96),
        delta_minor BIGINT,balance_after BIGINT,metadata JSONB,UNIQUE(user_id,source,source_id));
      CREATE TABLE admin_audit_log(id BIGSERIAL PRIMARY KEY,admin_user_id BIGINT,action TEXT,target_type TEXT,target_id TEXT,payload JSONB,reason TEXT CHECK(reason<>'fail-audit'));
      INSERT INTO users VALUES(101,false),(102,true),(103,false);`);
    const results = await Promise.all(Array.from({ length: 8 }, () => grantWallet(pool, 101, 1, body)));
    assert.equal(results.filter(x => !x.duplicate).length, 1);
    assert.equal((await pool.query('SELECT * FROM user_wallets')).rows[0].balance_minor, '125');
    await assert.rejects(grantWallet(pool, 101, 1, { ...body, amount_minor: 200 }), { status: 409 });
    await assert.rejects(grantWallet(pool, 101, 1, { ...body, reason: '其他原因' }), { status: 409 });
    await assert.rejects(grantWallet(pool, 102, 1, body), { status: 400 });
    await assert.rejects(grantWallet(pool, 999, 1, body), { status: 404 });
    await assert.rejects(grantWallet(pool, 103, 1, { ...body, reason: 'fail-audit' }));
    assert.equal((await pool.query('SELECT * FROM user_wallets WHERE user_id=103')).rows.length, 0);
    // Simulate the shared user -> wallet lock order used by game purchases.
    const purchase = await pool.connect();
    try {
      await purchase.query('BEGIN');
      await purchase.query('SELECT user_id FROM users WHERE user_id=101 FOR UPDATE');
      const grant = grantWallet(pool, 101, 1, { ...body, request_id: 'concurrent-grant-002' });
      await purchase.query('UPDATE user_wallets SET balance_minor=balance_minor-25,spent_minor=spent_minor+25 WHERE user_id=101');
      await purchase.query('COMMIT');
      await grant;
    } finally { purchase.release(); }
    const wallet = (await pool.query('SELECT * FROM user_wallets WHERE user_id=101')).rows[0];
    assert.equal(wallet.balance_minor, '225');
    assert.equal(wallet.earned_minor, '250');
    assert.equal(wallet.spent_minor, '25');
    assert.equal((await pool.query('SELECT * FROM wallet_ledger')).rows.length, 2);
    assert.equal((await pool.query('SELECT * FROM admin_audit_log')).rows.length, 2);
  } finally {
    if (pool) await pool.end();
    await control.query(`DROP SCHEMA ${schema} CASCADE`);
    await control.end();
  }
});
