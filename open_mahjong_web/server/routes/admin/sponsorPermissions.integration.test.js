const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { Pool } = require('pg');
const express = require('express');

const dsn = process.env.SPONSOR_SETTINGS_TEST_DATABASE_URL;

test('sponsor and special permissions remain independent across admin edits', { skip: !dsn }, async t => {
  // A single connection owns the temporary tables. Even unqualified queries
  // from the real router cannot reach the application's users or audit table.
  const pool = new Pool({ connectionString: dsn, options: '-c search_path=pg_temp', max: 1, idleTimeoutMillis: 0 });
  t.after(() => pool.end());
  const dbPath = require.resolve('../../config/database');
  require.cache[dbPath] = { id: dbPath, loaded: true, exports: pool };
  require('dotenv').config({ path: path.join(__dirname, '../../../.env') });
  const { ensureUserLadderPassColumns } = require('../../utils/userLadderPass');
  await pool.query(`
    CREATE TEMP TABLE users (user_id integer PRIMARY KEY, username text, email text,
      email_verified_at timestamp, is_tourist boolean, sponsor_expires_at timestamp,
      sponsor_amount numeric DEFAULT 0, admin_note text, ban_expires_at timestamp,
      ban_type text, ban_reason text, created_at timestamp, is_beginner_qualified boolean DEFAULT false);
    CREATE TEMP TABLE game_player_records (user_id integer);
    CREATE TEMP TABLE admin_audit_log (admin_user_id bigint, action text,
      target_type text, target_id text, payload jsonb, reason text);
    INSERT INTO users (user_id,username,sponsor_amount,sponsor_expires_at,is_beginner_qualified) VALUES
      (1,'ordinary',10,NULL,false), (2,'special',0,NULL,true), (3,'both',0,'2030-01-01',true),
      (4,'neither',0,NULL,false), (5,'historical',0,'2020-01-01',false);
  `);
  await ensureUserLadderPassColumns();

  const app = express();
  app.use(express.json());
  app.use((req, _res, next) => { req.admin = { userId: 10 }; next(); });
  app.use('/users', require('./users'));
  const server = app.listen(0, '127.0.0.1');
  await new Promise(resolve => server.once('listening', resolve));
  t.after(() => new Promise(resolve => server.close(resolve)));
  const base = `http://127.0.0.1:${server.address().port}/users/`;
  const list = async query => (await (await fetch(base + 'sponsors?' + query)).json()).data;
  async function patch(route, fields, status = 200) {
    const response = await fetch(base + route, { method: 'PATCH',
      headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reason: 'regression test', ...fields }) });
    const body = await response.json();
    assert.equal(response.status, status, body.message);
    return body.data;
  }
  const advanced = { is_beginner_qualified: false, is_intermediate_qualified: false,
    is_advanced_qualified: true, is_mcrpl_qualified: false };
  const ids = group => group.items.map(row => row.user_id).sort((a, b) => a - b);

  assert.deepEqual(ids(await list('group=sponsor')), [1, 3, 5]);
  assert.deepEqual(ids(await list('group=special')), [2, 3]);
  const filtered = await list('group=special&q=both');
  assert.deepEqual(filtered.totals, { sponsor: 3, special: 2, overlap: 1 });
  assert.equal(filtered.total, 1);
  assert.equal(filtered.items[0].has_sponsorship, true);
  assert.equal(filtered.items[0].has_special_settings, true);

  await patch('sponsors/permissions', { ...advanced, sync_special_settings: 'false' }, 400);
  assert.equal((await patch('sponsors/permissions', advanced)).affected_count, 2);
  await ensureUserLadderPassColumns(); // Re-starting the service must not reclassify bulk grants.
  assert.deepEqual(ids(await list('group=special')), [2, 3]);
  const permissions = async () => (await pool.query(`
    SELECT user_id, is_beginner_qualified, is_advanced_qualified, has_special_settings
    FROM users ORDER BY user_id
  `)).rows.map(row => Object.values(row));
  assert.deepEqual(await permissions(), [
    [1, false, true, false], [2, true, false, true], [3, true, false, true],
    [4, false, false, false], [5, false, true, false],
  ]);
  assert.equal((await patch('sponsors/permissions', { ...advanced, sync_special_settings: true })).affected_count, 3);
  assert.deepEqual(await permissions(), [
    [1, false, true, false], [2, true, false, true], [3, false, true, true],
    [4, false, false, false], [5, false, true, false],
  ]);
  const audits = await pool.query('SELECT payload FROM admin_audit_log ORDER BY (payload->>\'affected_count\')::int');
  assert.deepEqual(audits.rows.map(({ payload }) => [payload.affected_count, payload.sync_special_settings]),
    [[2, false], [3, true]]);
  assert.equal((await patch('1', { ...advanced, admin_note: 'note only' })).has_special_settings, false);
  assert.equal((await patch('1', { is_advanced_qualified: false })).has_special_settings, true);
  assert.equal((await patch('sponsors/permissions', advanced)).affected_count, 1);
  assert.equal((await patch('1', { has_special_settings: false })).has_special_settings, false);
  assert.equal((await patch('2', { sponsor_amount: 25 })).has_special_settings, true);
  assert.equal((await patch('5', { sponsor_expires_at: null })).has_special_settings, true);
  await patch('5', { has_special_settings: false }, 400);
  assert.equal((await patch('5', { is_advanced_qualified: false, has_special_settings: false })).has_special_settings, false);
  assert.deepEqual(ids(await list('group=sponsor')), [1, 2, 3]);
  assert.deepEqual(ids(await list('group=special')), [2, 3]);
});
