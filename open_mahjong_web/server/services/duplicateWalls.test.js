const test = require('node:test');
const assert = require('node:assert/strict');
const { catalog, fullWall, validateTiles, generateTiles, normalizeInput, wallDto, createDuplicateWallService } = require('./duplicateWalls');
const { sharedSourceIsLocked } = require('./duplicateRecordAccess');

// Stateful transactional adapter: a queued advisory lock makes concurrent creates exercise the real service boundary.
function memoryPool() {
  const walls = [];
  const admins = new Map([['evt_A:10', 'active'], ['evt_closed:10', 'closed'], ['evt_registered:10', 'registered'], ['evt_pending:10', 'pending']]);
  const linked = new Set();
  const locks = new Map();
  let nextId = 1;
  function scoped(params, sql) {
    return walls.filter((row) => sql.includes("scope = 'event'")
      ? row.scope === 'event' && row.event_id === params[0]
      : row.scope === 'personal' && row.owner_user_id === params[0]);
  }
  async function query(sql, params = [], client = {}) {
    if (sql === 'BEGIN') { client.inserted = []; return { rows: [] }; }
    if (sql === 'COMMIT' || sql === 'ROLLBACK') {
      if (sql === 'ROLLBACK') for (const row of client.inserted || []) walls.splice(walls.indexOf(row), 1);
      client.unlock?.(); client.unlock = null;
      return { rows: [] };
    }
    if (sql.includes('pg_advisory_xact_lock')) {
      const key = params[0];
      const previous = locks.get(key) || Promise.resolve();
      let release;
      locks.set(key, new Promise((resolve) => { release = resolve; }));
      await previous;
      client.unlock = release;
      return { rows: [] };
    }
    if (sql.includes('FROM events e JOIN event_admins')) {
      const status = admins.get(`${params[0]}:${params[1]}`);
      return { rows: status ? [{ status }] : [] };
    }
    if (sql.includes('COUNT(*) FILTER')) {
      const rows = scoped(params, sql);
      return { rows: [{ created_today: rows.filter((r) => r.today !== false).length, stored: rows.filter((r) => !r.deleted_at).length }] };
    }
    if (sql.startsWith('INSERT INTO duplicate_walls')) {
      const [key, owner_user_id, event_id, scope, wall_type, rule, name, tiles, seed, use_flowers, round_count, round_tiles] = params;
      const row = { id: String(nextId++), key, owner_user_id, event_id, scope, wall_type, rule, name, tiles: JSON.parse(tiles), seed, use_flowers, round_count, round_tiles: JSON.parse(round_tiles), is_unlocked: false, created_at: new Date(), deleted_at: null };
      walls.push(row); client.inserted?.push(row);
      return { rows: [{ ...row }] };
    }
    if (sql.startsWith('SELECT * FROM duplicate_walls WHERE id')) return { rows: walls.filter((r) => String(r.id) === String(params[0]) && !r.deleted_at).map((r) => ({ ...r })) };
    if (sql.startsWith('SELECT 1 FROM duplicate_games')) return { rows: linked.has(String(params[0])) ? [{}] : [] };
    if (sql.startsWith('UPDATE duplicate_walls')) {
      const row = walls.find((r) => String(r.id) === String(params[0]));
      if (sql.includes('deleted_at =')) row.deleted_at = new Date();
      else { row.is_unlocked = params[1]; row.unlocked_at ??= new Date(); }
      return { rows: [{ ...row }] };
    }
    if (sql.startsWith('SELECT * FROM duplicate_walls WHERE')) return { rows: scoped(params, sql).filter((r) => !r.deleted_at) };
    throw new Error(`Unexpected query: ${sql}`);
  }
  return { walls, admins, linked, query, async connect() { const client = { release() {} }; client.query = (sql, params) => query(sql, params, client); return client; } };
}
const input = (overrides = {}) => ({ scope: 'personal', wall_type: 'key', rule: 'guobiao', ...overrides });

test('series persist 1/4/8/12/16 independent legal rounds, with stable seed prefixes', async () => {
  assert.deepEqual(catalog().round_counts, [1, 4, 8, 12, 16]);
  const pool = memoryPool(); const service = createDuplicateWallService(pool);
  for (const count of catalog().round_counts) {
    for (const flowers of [false, true]) {
      const body = input({ wall_type: 'seed', seed: 'series-001', round_count: count, use_flowers: flowers });
      const series = normalizeInput(body);
      assert.equal(series.round_tiles.length, count);
      assert.deepEqual(series.round_tiles[0], generateTiles('guobiao', body.seed, flowers));
      assert.deepEqual(series.round_tiles, normalizeInput({ ...body, round_count: 16 }).round_tiles.slice(0, count));
      assert.equal(new Set(series.round_tiles.map(tiles => JSON.stringify(tiles))).size, count);
      for (const tiles of series.round_tiles) validateTiles('guobiao', tiles, flowers);
    }
    const { wall, quota } = await service.create(10, input({ round_count: count, use_flowers: false }));
    assert.equal(wall.round_count, count);
    assert.equal(pool.walls.at(-1).round_tiles.length, count);
    assert.equal(quota.created_today, pool.walls.length);
    for (const owner of [true, false]) {
      const dto = wallDto(pool.walls.at(-1), owner);
      for (const field of ['tiles', 'round_tiles', 'seed']) assert.equal(field in dto, false);
    }
  }
  assert.equal((await service.mine(10)).items.length, 5);
});

test('manual series require every round and keep every chosen tile order', () => {
  const generated = normalizeInput(input({ wall_type: 'seed', seed: 'manual-series', round_count: 16, use_flowers: false }));
  const body = input({ wall_type: 'manual', round_count: 16, round_tiles: generated.round_tiles, use_flowers: false });
  assert.deepEqual(normalizeInput(body).round_tiles, generated.round_tiles);
  assert.equal(normalizeInput(body).seed, null);
  assert.deepEqual(wallDto(normalizeInput(body), true).round_tiles, generated.round_tiles);
  assert.throws(() => normalizeInput({ ...body, round_tiles: body.round_tiles.slice(1) }), { status: 400 });
  const invalid = structuredClone(body);
  invalid.round_tiles[15].pop();
  assert.throws(() => normalizeInput(invalid), /第 16 局/);
  for (const count of [0, 2, 3, 17, '4', true, 4.5]) {
    assert.throws(() => normalizeInput(input({ round_count: count })), { status: 400 });
  }
  const legacy = normalizeInput(input({ wall_type: 'manual', tiles: fullWall('guobiao') }));
  assert.equal(legacy.round_count, 1);
  assert.deepEqual(legacy.round_tiles, [legacy.tiles]);
});

test('new walls support only Guobiao and Lanshi with exact tile multiplicities', () => {
  const sizes = { guobiao: 144, 'guobiao/lanshi': 136 };
  assert.deepEqual(catalog().rules.map((item) => item.rule), Object.keys(sizes));
  for (const [rule, size] of Object.entries(sizes)) {
    assert.equal(fullWall(rule).length, size);
    assert.deepEqual(validateTiles(rule, fullWall(rule)), fullWall(rule));
    assert.equal(size % 4, 0);
  }
  const invalid = fullWall('guobiao'); invalid[0] = 12;
  assert.throws(() => validateTiles('guobiao', invalid), { status: 400 });
  assert.throws(() => validateTiles('guobiao', fullWall('guobiao').map(String)), { status: 400 });
  for (const rule of ['qingque', 'classical', 'jiandan', 'riichi', 'taiwan', 'changsha', 'sichuan', 'hongque', 'free']) {
    assert.throws(() => fullWall(rule), { status: 400 });
    for (const wall_type of ['manual', 'seed', 'key']) {
      assert.throws(() => normalizeInput(input({ rule, wall_type, seed: '42', tiles: fullWall('guobiao') })), { status: 400 });
    }
  }
  assert.throws(() => normalizeInput(input({ wall_type: 'seed', seed: '' })), { status: 400 });
  assert.throws(() => normalizeInput(input({ scope: '__proto__' })), { status: 400 });
  assert.throws(() => fullWall('__proto__'), { status: 400 });
});

test('legacy non-Guobiao DTOs remain readable without opening new creation or leaking key-wall secrets', () => {
  for (const rule of ['riichi', 'taiwan', 'sichuan', 'future-legacy-rule']) {
    const row = { id: 'legacy', rule, wall_type: 'key', tiles: [11, 12, 13], seed: 'must-stay-private' };
    for (const ownerView of [false, true]) {
      const dto = wallDto(row, ownerView);
      assert.equal(dto.rule, rule);
      assert.equal(dto.tile_count, 3);
      assert.equal('tiles' in dto || 'seed' in dto, false);
      assert.equal(wallDto({ ...row, tiles: undefined }, ownerView).tile_count, null);
    }
  }
});

test('seed walls repeat deterministically and random walls hide both seed and tile order in every DTO', () => {
  assert.deepEqual(generateTiles('guobiao', '示例42'), generateTiles('guobiao', '示例42'));
  assert.notDeepEqual(generateTiles('guobiao', '示例42'), generateTiles('guobiao', '示例43'));
  const generated = normalizeInput(input());
  validateTiles(generated.rule, generated.tiles);
  for (const owner of [true, false]) {
    const dto = wallDto(generated, owner);
    assert.equal(Object.hasOwn(dto, 'tiles'), false);
    assert.equal(Object.hasOwn(dto, 'seed'), false);
  }
  assert.equal(wallDto(normalizeInput(input({ wall_type: 'seed', seed: '42' })), true).seed, '42');
});

test('concurrent personal requests cannot exceed five creates; delete does not restore daily allowance', async () => {
  const pool = memoryPool(); const service = createDuplicateWallService(pool);
  const results = await Promise.allSettled(Array.from({ length: 12 }, () => service.create(10, input())));
  assert.equal(results.filter((r) => r.status === 'fulfilled').length, 5);
  assert.ok(results.filter((r) => r.status === 'rejected').every((r) => r.reason.status === 429));
  await assert.rejects(service.manage(10, pool.walls[0].id, { remove: true }), { status: 409 });
  await service.manage(10, pool.walls[0].id, { is_unlocked: true });
  await service.manage(10, pool.walls[0].id, { remove: true });
  await assert.rejects(service.create(10, input()), { status: 429 });
  const mine = await service.mine(10);
  assert.equal(mine.quota.created_today, 5);
  assert.equal(mine.quota.stored, 4);
  assert.equal(mine.items.some((w) => 'tiles' in w || 'seed' in w), false);
});

test('personal storage ceiling survives a date rollover', async () => {
  const pool = memoryPool(); const service = createDuplicateWallService(pool);
  for (let day = 0; day < 5; day += 1) {
    for (let n = 0; n < 5; n += 1) await service.create(10, input());
    for (const row of pool.walls) row.today = false;
  }
  await assert.rejects(service.create(10, input()), { status: 409 });
  await service.manage(10, pool.walls[0].id, { is_unlocked: true });
  await service.manage(10, pool.walls[0].id, { remove: true });
  assert.equal((await service.create(10, input())).quota.stored, 25);
});

test('competition authorization, closed events, shared daily quota and owner lock permissions are enforced', async () => {
  const pool = memoryPool(); const service = createDuplicateWallService(pool);
  await assert.rejects(service.create(11, input({ scope: 'event', event_id: 'evt_A' })), { status: 403 });
  await assert.rejects(service.create(10, input({ scope: 'event', event_id: 'evt_closed' })), { status: 409 });
  await assert.rejects(service.create(10, input({ scope: 'event', event_id: 'evt_pending' })), { status: 409 });
  assert.equal((await service.create(10, input({ scope: 'event', event_id: 'evt_registered' }))).wall.event_id, 'evt_registered');
  const results = await Promise.allSettled(Array.from({ length: 23 }, () => service.create(10, input({ scope: 'event', event_id: 'evt_A' }))));
  assert.equal(results.filter((r) => r.status === 'fulfilled').length, 20);
  assert.equal((await service.mine(10, 'event', 'evt_A')).quota.storage_limit, 100);
  const personal = await service.create(10, input());
  await assert.rejects(service.manage(11, personal.wall.id, { is_unlocked: true }), { status: 403 });
  await assert.rejects(service.manage(10, personal.wall.id, { is_unlocked: 'true' }), { status: 400 });
  pool.linked.add(personal.wall.id);
  await assert.rejects(service.manage(10, personal.wall.id, { remove: true }), { status: 409 });
  assert.equal((await service.manage(10, personal.wall.id, { is_unlocked: false })).wall.is_unlocked, false);
  const unlocked = await service.manage(10, personal.wall.id, { is_unlocked: true });
  assert.equal(unlocked.wall.is_unlocked, true);
  const repeated = await service.manage(10, personal.wall.id, { is_unlocked: true });
  assert.equal(repeated.wall.unlocked_at, unlocked.wall.unlocked_at);
  await assert.rejects(service.manage(10, personal.wall.id, { is_unlocked: false }), { status: 409 });
  const removed = await service.manage(10, personal.wall.id, { remove: true });
  assert.equal(removed.wall.is_unlocked, true);
  assert.equal(removed.wall.unlocked_at, unlocked.wall.unlocked_at);
});

test('standard Guobiao flower toggle validates exact 136/144 tiles and preserves old seed order', () => {
  const crypto = require('node:crypto');
  const digest = (tiles) => crypto.createHash('sha256').update(JSON.stringify(tiles)).digest('hex');
  assert.equal(digest(generateTiles('guobiao', 'legacy-check')), 'd6f973e9e7b9736af23abbed87a80849555dd010b619e93da7e44a64fd3c495d');
  assert.equal(digest(generateTiles('guobiao/lanshi', 'legacy-check')), '6b70d5a62a35d4d0357be3fce008c6e68377d0f61b6f1bc4baba1c82ac85f577');
  assert.deepEqual(catalog().rules[0].flower_options.map((option) => [option.use_flowers, option.tile_count]), [[true, 144], [false, 136]]);
  for (const use_flowers of [false, true]) {
    for (const wall_type of ['manual', 'seed', 'key']) {
      const wall = normalizeInput(input({ wall_type, use_flowers, seed: 'text seed', tiles: fullWall('guobiao', use_flowers) }));
      assert.equal(wall.tiles.length, use_flowers ? 144 : 136);
      assert.equal(wall.use_flowers, use_flowers);
      validateTiles('guobiao', wall.tiles, use_flowers);
    }
    assert.throws(() => validateTiles('guobiao', fullWall('guobiao', !use_flowers), use_flowers), { status: 400 });
  }
  assert.deepEqual(generateTiles('guobiao', 'text seed', false), generateTiles('guobiao', 'text seed', false));
  assert.equal(normalizeInput(input({ rule: 'guobiao/lanshi', use_flowers: true })).use_flowers, false);
  for (const use_flowers of [null, 0, 1, 'false']) assert.throws(() => normalizeInput(input({ use_flowers })), { status: 400 });
});

test('every locked wall is undeletable, including a fresh wall with no games', async () => {
  const pool = memoryPool(); const service = createDuplicateWallService(pool);
  for (const wall_type of ['manual', 'seed', 'key']) {
    const { wall } = await service.create(10, input({ wall_type, tiles: fullWall('guobiao'), seed: 'text seed' }));
    await assert.rejects(service.manage(10, wall.id, { remove: true }), { status: 409 });
    assert.equal((await service.mine(10)).items.find((item) => item.id === wall.id).is_deleted, false);
    await service.manage(10, wall.id, { is_unlocked: true });
    const removed = (await service.manage(10, wall.id, { remove: true })).wall;
    assert.equal(removed.is_deleted, true);
    assert.ok(removed.deleted_at);
  }
});

test('sharing native records checks current key state and denies missing keys', async () => {
  let unlocked = false;
  const db = { async query() { return { rows: unlocked ? [{}] : [] }; } };
  const source = JSON.stringify({ game_title: { duplicate_key: 'dup_test' } });
  assert.equal(await sharedSourceIsLocked(db, source), true);
  unlocked = true;
  assert.equal(await sharedSourceIsLocked(db, source), false);
  const missingKeyDb = { async query() { return { rows: [] }; } };
  assert.equal(await sharedSourceIsLocked(missingKeyDb, source), true);
  assert.equal(await sharedSourceIsLocked(() => { throw new Error('should not need DB'); }, '[{"type":"start_game"}]'), false);
});
