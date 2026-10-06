const test = require('node:test');
const assert = require('node:assert/strict');
const { Pool } = require('pg');
const { ensureDuplicateTables } = require('../utils/duplicateTables');
const { createDuplicateWallService, fullWall } = require('./duplicateWalls');
const { recordIsLocked, accessibleGameIds, sharedSourceIsLocked } = require('./duplicateRecordAccess');

// Use an explicitly supplied disposable database; each run owns only its unique schema.
test('PostgreSQL duplicate quotas, record permissions and public results', {
  skip: !process.env.DUPLICATE_TEST_DATABASE_URL,
}, async (t) => {
  const connectionString = process.env.DUPLICATE_TEST_DATABASE_URL;
  const schema = `duplicate_test_${process.pid}_${Date.now()}`;
  const admin = new Pool({ connectionString });
  await admin.query(`CREATE SCHEMA ${schema}`);
  const pool = new Pool({ connectionString, options: `-c search_path=${schema}`, max: 25 });
  try {
    await ensureDuplicateTables(pool);
    await ensureDuplicateTables(pool); // The startup migration is idempotent.
    await pool.query(`
      CREATE TABLE users (user_id bigint PRIMARY KEY, username text);
      CREATE TABLE events (event_id varchar(32) PRIMARY KEY, name text, kind text, status text);
      CREATE TABLE event_admins (event_id varchar(32), user_id bigint);
      CREATE TABLE game_records (game_id varchar(16) PRIMARY KEY, record jsonb, created_at timestamp DEFAULT NOW());
      CREATE TABLE game_player_records (game_id varchar(16), user_id bigint, username text, score int, rank int, original_player_index int,
        title_used int, character_used int, profile_used int, voice_used int, rule text, sub_rule text, room_type text, match_type text);
      INSERT INTO events VALUES ('event-a','Test event','event','active');
      INSERT INTO events VALUES ('registered-a','Approved application','event','registered'), ('closed-a','Closed event','event','closed'), ('pending-a','Unapproved status','event','pending'), ('base-a','Not a competition','base','active');
      INSERT INTO event_admins VALUES ('event-a', 101), ('event-a', 102);
      INSERT INTO event_admins VALUES ('registered-a', 101), ('closed-a', 101), ('pending-a', 101), ('base-a', 101);
    `);
    const service = createDuplicateWallService(pool);
    const body = { scope: 'personal', wall_type: 'key', rule: 'guobiao' };

    await t.test('real HTTP routes enforce player JWT, ownership and public key locks', async () => {
      process.env.ADMIN_USER_IDS ||= '1';
      process.env.ADMIN_JWT_EXPIRES_SEC ||= '3600';
      process.env.ADMIN_JWT_SECRET ||= 'duplicate-http-test-admin';
      process.env.BOT_API_JWT_SECRET ||= 'duplicate-http-test-bot';
      process.env.PLAYER_JWT_SECRET ||= 'duplicate-http-test-player';
      // Load the actual router and middleware using only the explicitly isolated test pool.
      const databasePath = require.resolve('../config/database');
      const previous = require.cache[databasePath];
      require.cache[databasePath] = { id: databasePath, filename: databasePath, loaded: true, exports: pool };
      let router, publicRouter;
      try {
        router = require('../routes/duplicateWalls');
        publicRouter = require('../routes/platform');
      }
      finally { if (previous) require.cache[databasePath] = previous; else delete require.cache[databasePath]; }
      const express = require('express');
      const config = require('../config/config');
      const { signToken } = require('../utils/jwt');
      const token = (userId, extra = {}) => signToken({ user_id: userId, aud: config.playerAuth.audience, ...extra }, config.playerAuth.jwtSecret, 3600);
      const app = express(); app.use(express.json()); app.use('/walls', router); app.use('/public', publicRouter);
      const server = app.listen(0, '127.0.0.1');
      await new Promise((resolve) => server.once('listening', resolve));
      const base = `http://127.0.0.1:${server.address().port}/walls`;
      const request = (path, method = 'GET', auth, payload) => fetch(`${base}${path}`, {
        method, headers: { 'Content-Type': 'application/json', ...(auth ? { Authorization: `Bearer ${auth}` } : {}) },
        ...(payload ? { body: JSON.stringify(payload) } : {}),
      });
      try {
        const catalogResponse = await request('/catalog');
        assert.equal(catalogResponse.status, 200);
        assert.deepEqual((await catalogResponse.json()).data.rules.map((item) => item.rule), ['guobiao', 'guobiao/lanshi']);
        assert.equal((await request('/mine')).status, 401);
        assert.equal((await request('', 'POST', undefined, body)).status, 401);
        assert.equal((await request('', 'POST', 'invalid-token', body)).status, 401);
        assert.equal((await request('', 'POST', token(901, { aud: 'wrong' }), body)).status, 401);
        assert.equal((await request('', 'POST', token(901), { ...body, scope: 'event', event_id: 'event-a' })).status, 403);
        for (const rule of ['riichi', 'qingque', 'classical', 'changsha', 'sichuan', 'taiwan', 'jiandan']) {
          assert.equal((await request('', 'POST', token(901), { ...body, rule })).status, 400);
        }
        const created = await request('', 'POST', token(901), body);
        assert.equal(created.status, 201);
        assert.equal(created.headers.get('cache-control'), 'no-store');
        const { data: { wall } } = await created.json();
        assert.equal(wall.is_unlocked, false);
        assert.equal(wall.use_flowers, true);
        assert.equal('seed' in wall || 'tiles' in wall, false);
        const internal = (await pool.query('SELECT seed,tiles FROM duplicate_walls WHERE id=$1', [wall.id])).rows[0];
        const record = { game_title: { rule: 'guobiao', is_duplicate: true, duplicate_key: wall.key,
          duplicate_seed: internal.seed, duplicate_tiles: internal.tiles }, game_round: {} };
        await pool.query('INSERT INTO game_records(game_id,record) VALUES($1,$2)', ['HttpSecretWall', record]);
        await pool.query('INSERT INTO duplicate_games(game_id,wall_id) VALUES($1,$2)', ['HttpSecretWall', wall.id]);
        const publicUrl = `http://127.0.0.1:${server.address().port}/public`;
        for (const route of ['record', 'unity-record']) {
          const lockedRecord = await fetch(`${publicUrl}/${route}/HttpSecretWall`);
          assert.equal(lockedRecord.status, 403);
          assert.equal((await lockedRecord.text()).includes(internal.seed), false);
        }
        assert.equal((await request(`/public/${wall.key}`)).status, 404);
        assert.equal((await request(`/${wall.id}`, 'PATCH', token(902), { is_unlocked: true })).status, 403);
        assert.equal((await request(`/${wall.id}`, 'DELETE')).status, 401);
        const lockedDelete = await request(`/${wall.id}`, 'DELETE', token(901));
        assert.equal(lockedDelete.status, 409);
        assert.match((await lockedDelete.json()).message, /锁定的密钥不能删除/);
        assert.equal((await request(`/${wall.id}`, 'PATCH', token(901), { is_unlocked: 'true' })).status, 400);
        assert.equal((await request(`/${wall.id}`, 'PATCH', token(901), { is_unlocked: true })).status, 200);
        const unlocked = await request(`/public/${wall.key}`);
        assert.equal(unlocked.status, 200);
        const publicWall = (await unlocked.json()).data.wall;
        assert.equal('seed' in publicWall, false);
        for (const route of ['record', 'unity-record']) {
          const publicRecord = await fetch(`${publicUrl}/${route}/HttpSecretWall`);
          assert.equal(publicRecord.status, 200);
          assert.equal(publicRecord.headers.get('cache-control'), 'no-store');
          assert.deepEqual((await publicRecord.json()).data.record, record);
        }
        const repeated = await request(`/${wall.id}`, 'PATCH', token(901), { is_unlocked: true });
        assert.equal(repeated.status, 200);
        assert.equal((await repeated.json()).data.wall.unlocked_at, publicWall.unlocked_at);
        const relock = await request(`/${wall.id}`, 'PATCH', token(901), { is_unlocked: false });
        assert.equal(relock.status, 409);
        assert.match((await relock.json()).message, /不能再次锁定/);
        assert.equal((await request(`/${wall.id}`, 'DELETE', token(901))).status, 200);
        const deletedPublic = await request(`/public/${wall.key}`);
        assert.equal(deletedPublic.status, 200);
        const deletedDto = (await deletedPublic.json()).data.wall;
        assert.equal(deletedDto.is_deleted, true);
        assert.ok(deletedDto.deleted_at && deletedDto.created_at && deletedDto.unlocked_at);
        assert.deepEqual((await (await fetch(`${publicUrl}/record/HttpSecretWall`)).json()).data.record, record);
        const noFlowers = await request('', 'POST', token(901), { ...body, wall_type: 'seed', use_flowers: false, seed: 'public-known-text' });
        assert.equal(noFlowers.status, 201);
        const noFlowerDto = (await noFlowers.json()).data.wall;
        assert.equal(noFlowerDto.use_flowers, false);
        assert.equal(noFlowerDto.tiles.length, 136);
        assert.equal(noFlowerDto.seed, 'public-known-text');
        assert.equal((await request('', 'POST', token(901), { ...body, use_flowers: 'false' })).status, 400);
      } finally { await new Promise((resolve) => server.close(resolve)); }
    });

    await t.test('concurrent creation and deletion cannot bypass the five-per-day limit', async () => {
      const results = await Promise.allSettled(Array.from({ length: 12 }, () => service.create(101, body)));
      assert.equal(results.filter((r) => r.status === 'fulfilled').length, 5);
      for (const result of results.filter((r) => r.status === 'rejected')) assert.equal(result.reason.status, 429);
      const mine = await service.mine(101);
      assert.equal(mine.quota.created_today, 5);
      assert.equal(mine.quota.stored, 5);
      assert.ok(mine.items.every((wall) => wall.is_unlocked === false && !('seed' in wall) && !('tiles' in wall)));
      await assert.rejects(service.manage(101, mine.items[0].id, { remove: true }), { status: 409 });
      await service.manage(101, mine.items[0].id, { is_unlocked: true });
      await service.manage(101, mine.items[0].id, { remove: true });
      await assert.rejects(service.create(101, body), { status: 429 });
      assert.equal((await service.mine(101)).quota.stored, 4);
      await assert.rejects(service.manage(999, mine.items[1].id, { is_unlocked: true }), { status: 403 });
    });

    await t.test('event administrators share twenty-five daily walls and outsiders cannot use the quota', async () => {
      const input = { ...body, scope: 'event', event_id: 'event-a' };
      await assert.rejects(service.create(999, input), { status: 403 });
      assert.equal((await service.create(101, { ...input, event_id: 'registered-a' })).wall.event_id, 'registered-a');
      for (const event_id of ['closed-a', 'pending-a']) await assert.rejects(service.create(101, { ...input, event_id }), { status: 409 });
      await assert.rejects(service.create(101, { ...input, event_id: 'base-a' }), { status: 403 });
      const results = await Promise.allSettled(Array.from({ length: 26 }, (_, i) => service.create(i % 2 ? 101 : 102, input)));
      assert.equal(results.filter((r) => r.status === 'fulfilled').length, 25);
      const mine = await service.mine(102, 'event', 'event-a');
      assert.equal(mine.quota.created_today, 25);
      assert.equal(mine.items.length, 25);
      assert.equal(mine.quota.storage_limit, 100);
    });

    await t.test('whole batches share the event quota with manual creates under the real PostgreSQL lock', async () => {
      await pool.query("INSERT INTO events VALUES ('event-batch', 'Batch test', 'event', 'active')");
      await pool.query("INSERT INTO event_admins VALUES ('event-batch', 101), ('event-batch', 102)");
      const input = { ...body, scope: 'event', event_id: 'event-batch', round_count: 8, use_flowers: false };
      await service.create(101, input);
      const batch = await service.createBatch(102, input, 20);
      assert.equal(batch.quota.created_today, 21);
      assert.equal(batch.walls.length, 20);
      assert.equal(new Set(batch.walls.map(wall => wall.key)).size, 20);
      await assert.rejects(service.createBatch(101, input, 5), { status: 429 });
      assert.equal((await service.getQuota(102, 'event', 'event-batch')).created_today, 21);
      const concurrent = await Promise.allSettled([service.createBatch(101, input, 3), service.createBatch(102, input, 3)]);
      assert.equal(concurrent.filter(result => result.status === 'fulfilled').length, 1);
      assert.equal(concurrent.find(result => result.status === 'rejected').reason.status, 429);
      assert.equal((await service.create(101, input)).quota.created_today, 25);
      await assert.rejects(service.createBatch(102, input, 1), { status: 429 });
      const stored = await pool.query("SELECT COUNT(DISTINCT seed)::int AS seeds FROM duplicate_walls WHERE event_id = 'event-batch'");
      assert.equal(stored.rows[0].seeds, 25);
    });

    await t.test('storage limits still apply after the daily count resets', async () => {
      for (const [scope, owner, eventId, limit] of [['personal', 201, null, 25], ['event', 101, 'event-a', 100]]) {
        await pool.query(`INSERT INTO duplicate_walls (key,owner_user_id,event_id,scope,wall_type,rule,tiles,created_at)
          SELECT 'fixture_' || $1 || '_' || i, $2, $3, $1, 'manual', 'guobiao', $4::jsonb, NOW() - INTERVAL '2 days'
          FROM generate_series(1,$5::int) i`, [scope, owner, eventId, JSON.stringify(fullWall('guobiao')), limit - (scope === 'event' ? 25 : 0)]);
        if (scope === 'event') await pool.query("UPDATE duplicate_walls SET created_at = NOW() - INTERVAL '2 days' WHERE event_id = 'event-a'");
        await assert.rejects(service.create(owner, { ...body, scope, event_id: eventId }), { status: 409 });
      }
    });

    await t.test('unlock permanently enables direct, batch, shared and public reads even after wall deletion', async () => {
      const { wall } = await service.create(301, { ...body, wall_type: 'seed', seed: 'same-wall' });
      const record = { game_title: { duplicate_key: wall.key, is_duplicate: true, duplicate_seed: 'same-wall', duplicate_tiles: fullWall('guobiao') } };
      await pool.query('INSERT INTO game_records (game_id,record) VALUES ($1,$2)', ['game-locked', record]);
      await pool.query('INSERT INTO game_records (game_id,record) VALUES ($1,$2)', ['game-title', record]);
      await pool.query('INSERT INTO game_records (game_id,record) VALUES ($1,$2)', ['game-normal', { game_title: {} }]);
      await pool.query('INSERT INTO game_records (game_id,record) VALUES ($1,$2)', ['game-link', { game_title: {} }]);
      await pool.query('INSERT INTO game_records (game_id,record) VALUES ($1,$2)', ['game-missing', { game_title: { duplicate_key: 'missing-wall' } }]);
      await pool.query('INSERT INTO duplicate_games VALUES ($1,$2,NOW())', ['game-locked', wall.id]);
      await pool.query('INSERT INTO duplicate_games VALUES ($1,$2,NOW())', ['game-link', wall.id]);
      await pool.query('UPDATE duplicate_walls SET ended_at = NOW() WHERE id = $1', [wall.id]);
      await pool.query("INSERT INTO game_player_records (game_id,user_id,username,score,rank,original_player_index) VALUES ('game-locked',301,'Test player',0,1,0)");
      assert.equal(await recordIsLocked(pool, 'game-locked'), true);
      assert.equal(await recordIsLocked(pool, 'game-title'), true); // Fail closed even with no association.
      assert.equal(await recordIsLocked(pool, 'game-link'), true); // The association also protects missing title metadata.
      assert.equal(await recordIsLocked(pool, 'game-missing'), true);
      assert.equal(await sharedSourceIsLocked(pool, JSON.stringify(record)), true);
      assert.deepEqual(await accessibleGameIds(pool, ['game-locked', 'game-title', 'game-normal']), ['game-normal']);
      await assert.rejects(service.publicDetail(wall.key), { status: 404 });
      const unlocked = await service.manage(301, wall.id, { is_unlocked: true });
      const detail = await service.publicDetail(wall.key);
      assert.equal(detail.games.length, 3); // Include historical title-only records as well as reserved games.
      assert.equal(detail.games.find((game) => game.game_id === 'game-locked').players[0].username, 'Test player');
      assert.ok(detail.wall.ended_at);
      assert.equal(await recordIsLocked(pool, 'game-locked'), false);
      assert.equal(await sharedSourceIsLocked(pool, record), false);
      await assert.rejects(service.manage(301, wall.id, { is_unlocked: false }), { status: 409 });
      assert.equal(await recordIsLocked(pool, 'game-locked'), false);
      assert.equal((await service.publicList(wall.key)).items.length, 1);
      assert.deepEqual(await accessibleGameIds(pool, ['game-locked', 'game-title', 'game-normal']), ['game-locked', 'game-title', 'game-normal']);
      await service.manage(301, wall.id, { remove: true });
      const deleted = await service.publicDetail(wall.key);
      assert.equal(deleted.wall.is_unlocked, true);
      assert.equal(deleted.wall.is_deleted, true);
      assert.ok(deleted.wall.deleted_at && deleted.wall.created_at);
      assert.equal(deleted.games.length, 3);
      assert.equal((await service.publicList(wall.key)).items[0].is_deleted, true);
      assert.equal(deleted.wall.unlocked_at.getTime(), unlocked.wall.unlocked_at.getTime());
      assert.equal(await recordIsLocked(pool, 'game-locked'), false);
      assert.equal(await sharedSourceIsLocked(pool, record), false);
    });

    await t.test('standard no-flower walls persist and historical columns migrate without changing secrets or locks', async () => {
      const seed = await service.create(601, { ...body, wall_type: 'seed', seed: 'legacy-text', use_flowers: false });
      assert.equal(seed.wall.use_flowers, false);
      assert.equal(seed.wall.tiles.length, 136);
      assert.equal(seed.wall.seed, 'legacy-text');
      const random = await service.create(601, { ...body, use_flowers: false });
      assert.equal(random.wall.use_flowers, false);
      assert.equal('seed' in random.wall || 'tiles' in random.wall, false);
      const before = (await pool.query('SELECT id, seed, tiles, is_unlocked, unlocked_at FROM duplicate_walls ORDER BY id')).rows;
      // Recreate the pre-use_flowers schema shape, then execute the real startup migration.
      await pool.query('ALTER TABLE duplicate_walls DROP COLUMN use_flowers');
      await ensureDuplicateTables(pool);
      await ensureDuplicateTables(pool);
      const after = (await pool.query('SELECT id, seed, tiles, is_unlocked, unlocked_at FROM duplicate_walls ORDER BY id')).rows;
      assert.deepEqual(after, before);
      const stored = (await pool.query('SELECT use_flowers FROM duplicate_walls WHERE id = $1', [random.wall.id])).rows[0];
      assert.equal(stored.use_flowers, false);
      assert.equal((await service.mine(601)).items.find((wall) => wall.id === random.wall.id).use_flowers, false);
      await assert.rejects(service.manage(601, random.wall.id, { remove: true }), { status: 409 });
    });

    await t.test('orphan duplicate markers deny reads and shares until a verified unlocked association exists', async () => {
      const { wall } = await service.create(701, body);
      const markers = [{ is_duplicate: true }, { duplicate_seed: 'private-seed' }, { duplicate_tiles: [11, 12] }];
      for (let index = 0; index < markers.length; index++) {
        const gameId = `orphan${index}`;
        const record = { game_id: gameId, game_title: markers[index] };
        await pool.query('INSERT INTO game_records (game_id,record) VALUES ($1,$2)', [gameId, record]);
        assert.equal(await recordIsLocked(pool, gameId), true);
        assert.equal(await sharedSourceIsLocked(pool, record), true);
        assert.equal(await sharedSourceIsLocked(pool, { game_title: markers[index] }), true);
        await pool.query('INSERT INTO duplicate_games VALUES ($1,$2,NOW())', [gameId, wall.id]);
        assert.equal(await recordIsLocked(pool, gameId), true);
      }
      await service.manage(701, wall.id, { is_unlocked: true });
      for (let index = 0; index < markers.length; index++) {
        assert.equal(await recordIsLocked(pool, `orphan${index}`), false);
        assert.equal(await sharedSourceIsLocked(pool, { game_id: `orphan${index}`, game_title: markers[index] }), false);
      }
    });

    await t.test('concurrent repeated unlocks are idempotent and cannot race with a relock', async () => {
      const { wall } = await service.create(501, body);
      const firstWave = await Promise.all(Array.from({ length: 8 }, () => service.manage(501, wall.id, { is_unlocked: true })));
      const firstUnlockedAt = firstWave[0].wall.unlocked_at.getTime();
      assert.ok(firstWave.every((result) => result.wall.is_unlocked && result.wall.unlocked_at.getTime() === firstUnlockedAt));
      const mixed = await Promise.allSettled(Array.from({ length: 20 }, (_, index) => service.manage(501, wall.id, { is_unlocked: index % 2 === 0 })));
      assert.equal(mixed.filter((result) => result.status === 'fulfilled').length, 10);
      assert.ok(mixed.filter((result) => result.status === 'rejected').every((result) => result.reason.status === 409));
      const after = (await service.mine(501)).items[0];
      assert.equal(after.is_unlocked, true);
      assert.equal(after.unlocked_at.getTime(), firstUnlockedAt);
    });

    await t.test('legacy non-Guobiao walls keep management and lock protection without accepting new creation', async () => {
      const legacyKey = `dup_${'e'.repeat(32)}`;
      const result = await pool.query(`INSERT INTO duplicate_walls
        (key, owner_user_id, scope, wall_type, rule, tiles, seed)
        VALUES ($1, 401, 'personal', 'key', 'riichi', $2, 'legacy-secret') RETURNING id`,
      [legacyKey, JSON.stringify(fullWall('guobiao/lanshi'))]);
      const id = result.rows[0].id;
      await ensureDuplicateTables(pool); // The migration must not automatically unlock any legacy wall.
      await pool.query('INSERT INTO game_records (game_id, record) VALUES ($1,$2)', ['legacyRiichi', { game_title: { rule: 'riichi', duplicate_key: legacyKey } }]);
      await pool.query('INSERT INTO duplicate_games VALUES ($1,$2,NOW())', ['legacyRiichi', id]);
      const mine = await service.mine(401);
      assert.equal(mine.items[0].rule, 'riichi');
      assert.equal(mine.items[0].tile_count, 136);
      assert.equal('seed' in mine.items[0] || 'tiles' in mine.items[0], false);
      await assert.rejects(service.create(401, { ...body, rule: 'riichi' }), { status: 400 });
      assert.equal(await recordIsLocked(pool, 'legacyRiichi'), true);
      await service.manage(401, id, { is_unlocked: true });
      assert.equal((await service.publicDetail(legacyKey)).games[0].game_id, 'legacyRiichi');
      assert.equal(await recordIsLocked(pool, 'legacyRiichi'), false);
      await assert.rejects(service.manage(401, id, { is_unlocked: false }), { status: 409 });
      assert.equal(await recordIsLocked(pool, 'legacyRiichi'), false);
      assert.equal((await pool.query('SELECT COUNT(*)::int AS count FROM duplicate_walls WHERE id = $1', [id])).rows[0].count, 1);
    });
    await t.test('multi-round keys persist the whole match and unlock all its record data together', async () => {
      for (const count of [1, 4, 8, 12, 16]) {
        const { wall } = await service.create(70000071, { ...body, round_count: count, use_flowers: false });
        assert.equal(wall.round_count, count);
        assert.equal('round_tiles' in wall, false);
        const saved = (await pool.query('SELECT * FROM duplicate_walls WHERE id=$1', [wall.id])).rows[0];
        assert.equal(saved.round_tiles.length, count);
        assert.ok(saved.round_tiles.every(tiles => tiles.length === 136));
        const gameId = `series${count}`;
        const record = { game_title: { is_duplicate: true, duplicate_key: wall.key, duplicate_seed: saved.seed,
          duplicate_round_count: count, duplicate_round_tiles: saved.round_tiles } };
        await pool.query('INSERT INTO game_records (game_id, record) VALUES ($1,$2)', [gameId, record]);
        await pool.query('INSERT INTO duplicate_games VALUES ($1,$2,NOW())', [gameId, wall.id]);
        assert.equal(await recordIsLocked(pool, gameId), true);
        await assert.rejects(service.publicDetail(wall.key), { status: 404 });
        await service.manage(70000071, wall.id, { is_unlocked: true });
        assert.equal(await recordIsLocked(pool, gameId), false);
        const detail = await service.publicDetail(wall.key);
        assert.equal(detail.wall.round_count, count);
        assert.equal(detail.games[0].game_id, gameId);
        await service.manage(70000071, wall.id, { remove: true });
        assert.equal(await recordIsLocked(pool, gameId), false);
      }
    });
    await t.test('public results identify the creator or owning event without games and preserve the source after deletion', async () => {
      await pool.query("INSERT INTO users VALUES (80000081, '复式创建者')");
      await pool.query("INSERT INTO events VALUES ('source-event', '复式来源赛事', 'event', 'registered')");
      await pool.query("INSERT INTO event_admins VALUES ('source-event', 80000081)");
      const personal = (await service.create(80000081, body)).wall;
      const event = (await service.create(80000081, { ...body, scope: 'event', event_id: 'source-event' })).wall;
      for (const wall of [personal, event]) {
        assert.deepEqual((await service.publicList(wall.key)).items, []);
        await assert.rejects(service.publicDetail(wall.key), { status: 404 });
        await service.manage(80000081, wall.id, { is_unlocked: true });
        const detail = await service.publicDetail(wall.key);
        assert.deepEqual(detail.games, []);
        for (const result of [detail.wall, (await service.publicList(wall.key)).items[0]]) {
          assert.equal(result.owner_user_id, wall.scope === 'personal' ? '80000081' : null);
          assert.equal(result.owner_username, wall.scope === 'personal' ? '复式创建者' : null);
          assert.equal(result.event_name, wall.scope === 'event' ? '复式来源赛事' : null);
          for (const secret of ['seed', 'tiles', 'round_tiles']) assert.equal(secret in result, false);
        }
        await service.manage(80000081, wall.id, { remove: true });
      }
      await pool.query("UPDATE users SET username = '创建者新昵称' WHERE user_id = 80000081");
      await pool.query("UPDATE events SET name = '赛事新名称' WHERE event_id = 'source-event'");
      assert.equal((await service.publicList(personal.key)).items[0].owner_username, '创建者新昵称');
      assert.equal((await service.publicDetail(personal.key)).wall.owner_username, '创建者新昵称');
      assert.equal((await service.publicList(event.key)).items[0].event_name, '赛事新名称');
      assert.equal((await service.publicDetail(event.key)).wall.event_name, '赛事新名称');
      await pool.query('DELETE FROM users WHERE user_id = 80000081');
      await pool.query("DELETE FROM events WHERE event_id = 'source-event'");
      for (const wall of [personal, event]) {
        for (const result of [(await service.publicList(wall.key)).items[0], (await service.publicDetail(wall.key)).wall]) {
          assert.equal(result.is_deleted, true);
          assert.equal(result.owner_user_id, wall.scope === 'personal' ? '80000081' : null);
          assert.equal(result.event_id, wall.scope === 'event' ? 'source-event' : null);
          assert.equal(result.owner_username, null);
          assert.equal(result.event_name, null);
        }
      }
    });
  } finally {
    await pool.end();
    await admin.query(`DROP SCHEMA ${schema} CASCADE`);
    await admin.end();
  }
});
