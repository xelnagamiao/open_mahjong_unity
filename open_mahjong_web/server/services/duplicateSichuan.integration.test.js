const test = require('node:test');
const assert = require('node:assert/strict');
const { Pool } = require('pg');
const { ensureDuplicateTables } = require('../utils/duplicateTables');
const { createDuplicateWallService, fullWall } = require('./duplicateWalls');

test('Sichuan keys use authenticated routes and PostgreSQL for personal, event and base scopes', {
  skip: !process.env.DUPLICATE_TEST_DATABASE_URL,
}, async (t) => {
  const connectionString = process.env.DUPLICATE_TEST_DATABASE_URL;
  const schema = `sichuan_keys_${process.pid}_${Date.now()}`;
  const admin = new Pool({ connectionString });
  await admin.query(`CREATE SCHEMA ${schema}`);
  const pool = new Pool({ connectionString, options: `-c search_path=${schema}` });
  let server;
  try {
    await ensureDuplicateTables(pool);
    await pool.query(`
      CREATE TABLE events (event_id text PRIMARY KEY, kind text, status text);
      CREATE TABLE event_admins (event_id text, user_id bigint);
      INSERT INTO events VALUES ('test-event','event','active'),('test-base','base','active');
      INSERT INTO event_admins VALUES ('test-event',101),('test-base',101);
    `);
    process.env.ADMIN_USER_IDS ||= '1';
    process.env.ADMIN_JWT_EXPIRES_SEC ||= '3600';
    process.env.ADMIN_JWT_SECRET ||= 'sichuan-keys-test-admin';
    process.env.BOT_API_JWT_SECRET ||= 'sichuan-keys-test-bot';
    process.env.PLAYER_JWT_SECRET ||= 'sichuan-keys-test-player';
    const dbPath = require.resolve('../config/database');
    const oldDb = require.cache[dbPath];
    require.cache[dbPath] = { id: dbPath, filename: dbPath, loaded: true, exports: pool };
    let createRouter;
    try { ({ createRouter } = require('../routes/duplicateWalls')); }
    finally { if (oldDb) require.cache[dbPath] = oldDb; else delete require.cache[dbPath]; }
    const config = require('../config/config');
    const { signToken } = require('../utils/jwt');
    const token = signToken({ user_id: 101, aud: config.playerAuth.audience }, config.playerAuth.jwtSecret, 3600);
    const express = require('express');
    const app = express(); app.use(express.json({ limit: '1mb' }));
    app.use('/walls', createRouter(createDuplicateWallService(pool)));
    server = app.listen(0, '127.0.0.1');
    await new Promise(resolve => server.once('listening', resolve));
    const base = `http://127.0.0.1:${server.address().port}/walls`;
    const post = (body, auth = token) => fetch(base, { method: 'POST', headers: {
      'Content-Type': 'application/json', ...(auth ? { Authorization: `Bearer ${auth}` } : {}),
    }, body: JSON.stringify(body) });
    for (const event_id of [null, 'test-event', 'test-base']) {
      for (const rule of ['sichuan/sbr', 'sichuan/xueliu']) {
        const scope = event_id ? 'event' : 'personal';
        const auth = !event_id && rule === 'sichuan/xueliu'
          ? signToken({ user_id: 201, aud: config.playerAuth.audience }, config.playerAuth.jwtSecret, 3600) : token;
        for (const wall_type of ['manual', 'seed', 'key']) {
          await t.test(`${event_id} / ${rule} / ${wall_type}`, async () => {
            const body = { scope, event_id, rule, wall_type, round_count: 4,
              use_flowers: false, seed: 'same-series', round_tiles: Array.from({ length: 4 }, () => fullWall(rule)) };
            const response = await post(body, auth);
            assert.equal(response.status, 201);
            const wall = (await response.json()).data.wall;
            assert.equal(wall.wall_layout, rule === 'sichuan/sbr' ? 'sbr' : 'xueliu');
            assert.equal(wall.tile_count, 108);
            assert.equal(wall.use_flowers, false);
            assert.equal(wall.round_count, 4);
            assert.equal(wall.is_unlocked, false);
            assert.equal(wall.scope, scope);
            assert.equal(wall.event_id, event_id);
            if (wall_type === 'key') assert.ok(!('tiles' in wall) && !('seed' in wall) && !('round_tiles' in wall));
            const stored = (await pool.query('SELECT * FROM duplicate_walls WHERE id=$1', [wall.id])).rows[0];
            for (const tiles of stored.round_tiles) assert.deepEqual([...tiles].sort((a,b)=>a-b), fullWall(rule));
            if (wall_type === 'manual') assert.deepEqual(stored.round_tiles, body.round_tiles);
            const listed = await fetch(`${base}/mine?scope=${scope}${event_id ? '&event_id=' + event_id : ''}`, { headers: { Authorization: `Bearer ${auth}` } });
            assert.equal(listed.status, 200);
            const readback = (await listed.json()).data.items.find(item => item.id === wall.id);
            assert.equal(readback.rule, rule);
            assert.equal(readback.wall_layout, wall.wall_layout);
            if (wall_type !== 'key') assert.deepEqual(readback.round_tiles, stored.round_tiles);
            else assert.ok(!('tiles' in readback) && !('seed' in readback));
          });
        }
      }
    }
    await t.test('rejects honors, flowers, cross-venue authority and anonymous creation without writes', async () => {
      const body = { scope: 'event', event_id: 'test-event', rule: 'sichuan/sbr', wall_type: 'manual', tiles: fullWall('sichuan/sbr') };
      const before = Number((await pool.query('SELECT count(*) FROM duplicate_walls')).rows[0].count);
      for (const rule of ['sichuan/sbr', 'sichuan/xueliu']) {
        for (const tile of [41, 47, 51, '11', true]) {
          const tiles = [...body.tiles]; tiles[0] = tile;
          assert.equal((await post({ ...body, rule, tiles })).status, 400);
        }
        assert.equal((await post({ ...body, rule, wall_type: 'key', use_flowers: true })).status, 400);
      }
      for (const rule of [['sichuan/sbr'], { rule: 'sichuan/sbr' }, 0, false]) {
        assert.equal((await post({ ...body, rule, wall_type: 'key' })).status, 400);
      }
      assert.equal((await post(body, '')).status, 401);
      assert.equal((await post({ ...body, event_id: 'not-owned' })).status, 403);
      assert.equal((await post({ ...body, scope: 'personal' }, '')).status, 401);
      assert.equal(Number((await pool.query('SELECT count(*) FROM duplicate_walls')).rows[0].count), before);
    });
  } finally {
    if (server) { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
    await pool.end();
    await admin.query(`DROP SCHEMA ${schema} CASCADE`);
    await admin.end();
  }
});
