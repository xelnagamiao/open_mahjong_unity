const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

// Keep cache tests independent of application secrets and the shared database.
let queries = 0;
const databasePath = require.resolve('../config/database');
require.cache[databasePath] = {
  id: databasePath, filename: databasePath, loaded: true,
  exports: { query: async () => {
    queries += 1;
    return { rows: [
      { user_id: 101, username: 'PT gainer', total_pt_change: '14.70', games: 2, missing_pt_records: 1 },
      { user_id: 102, username: 'PT loser', total_pt_change: '-5.15', games: 1, missing_pt_records: 0 },
      { user_id: 103, username: 'Unknown', total_pt_change: null, games: 0, missing_pt_records: 3 },
    ] };
  } },
};
const { getWeeklyScoreboard, queryWeeklyPtChanges, pickPtLeaders } = require('./weeklyScoreboard');

test('old score cache is invalidated, decimal PT is cached, and missing PT stays unknown', async () => {
  const root = path.resolve(__dirname, '../../../.om_workspace/weekly-pt-tests');
  fs.mkdirSync(root, { recursive: true });
  const dir = fs.mkdtempSync(path.join(root, 'cache-'));
  const previousDir = process.env.FUN_DATA_DIR;
  process.env.FUN_DATA_DIR = dir;
  try {
    const now = new Date('2026-09-25T12:00:00+08:00');
    fs.writeFileSync(path.join(dir, 'weekly-scoreboard.json'), JSON.stringify({
      date_from: '2026-09-18', date_to: '2026-09-24',
      gainers: [{ user_id: 101, total_score: 99999 }], losers: [],
    }));
    const fresh = await getWeeklyScoreboard(now);
    assert.equal(fresh.from_cache, false);
    assert.equal(fresh.metric, 'pt_change');
    assert.equal(fresh.missing_pt_records, 4);
    assert.equal(fresh.gainers[0].total_pt_change, 14.7);
    assert.equal(fresh.losers[0].total_pt_change, -5.15);
    assert.equal(fresh.gainers.length, 1);
    assert.match(fresh.note, /PT 净变更/);
    assert.equal(queries, 1);
    const cached = await getWeeklyScoreboard(now);
    assert.equal(cached.from_cache, true);
    assert.deepEqual(cached.gainers, fresh.gainers);
    assert.equal(queries, 1);
    const nextDay = await getWeeklyScoreboard(new Date('2026-09-26T04:00:00+08:00'));
    assert.equal(nextDay.from_cache, false);
    assert.equal(nextDay.date_to, '2026-09-25');
    assert.equal(queries, 2);
  } finally {
    if (previousDir === undefined) delete process.env.FUN_DATA_DIR;
    else process.env.FUN_DATA_DIR = previousDir;
  }
});

// Opt in with a test PostgreSQL connection. Only session-local tables are written.
test('PostgreSQL weekly totals use settlement PT and Shanghai 04:00 boundaries', {
  skip: !process.env.WEEKLY_PT_TEST_DATABASE_URL,
}, async () => {
  const { Client } = require('pg');
  const db = new Client({ connectionString: process.env.WEEKLY_PT_TEST_DATABASE_URL });
  await db.connect();
  try {
    await db.query('BEGIN');
    await db.query("SET LOCAL search_path = pg_temp; SET LOCAL TIME ZONE 'UTC'");
    await db.query(`
      CREATE TEMP TABLE users (user_id bigint, username text);
      CREATE TEMP TABLE game_records (game_id text PRIMARY KEY, created_at timestamp);
      CREATE TEMP TABLE game_player_records (
        game_id text, user_id bigint, username text, room_type text, rule text,
        score int, pt_change numeric(12, 2)
      );
      INSERT INTO users VALUES (101, 'Current name');
      INSERT INTO game_records VALUES
        ('before', '2026-09-18 03:59:59'), ('first', '2026-09-18 04:00:00'),
        ('during', '2026-09-24 12:00:00'), ('last', '2026-09-25 03:59:59'),
        ('today', '2026-09-25 04:00:00');
      INSERT INTO game_player_records VALUES
        ('before', 101, 'Old name', 'match', 'guobiao', 900, 999),
        ('first', 101, 'Old name', 'match', 'guobiao', -400, 11.76),
        ('during', 101, 'Old name', 'match', 'guobiao', 900, -5.15),
        ('last', 101, 'Old name', 'match', 'guobiao', 500, 0),
        ('today', 101, 'Old name', 'match', 'guobiao', 900, 999),
        ('first', 102, 'Loser', 'match', 'guobiao', 1000, -3.15),
        ('last', 102, 'Loser', 'match', 'guobiao', 1000, -5.15),
        ('during', 102, 'Loser', 'match', 'guobiao', -9000, NULL),
        ('first', 103, 'No PT', 'match', 'guobiao', 99999, NULL),
        ('during', 104, 'Zero PT', 'match', 'guobiao', 9000, 0),
        ('during', 105, 'Custom', 'custom', 'guobiao', 9000, 999),
        ('during', 106, 'Event', 'events', 'guobiao', 9000, 999),
        ('during', 107, 'Other rule', 'match', 'riichi', 9000, 999),
        ('during', 10, 'Bot', 'match', 'guobiao', 9000, 999);
    `);
    const rows = await queryWeeklyPtChanges({ dateFrom: '2026-09-18', dateTo: '2026-09-24' }, db);
    const { gainers, losers } = pickPtLeaders(rows);
    assert.deepEqual(gainers, [{ user_id: 101, username: 'Current name', total_pt_change: 6.61, games: 3, place: 1 }]);
    assert.deepEqual(losers, [{ user_id: 102, username: 'Loser', total_pt_change: -8.3, games: 2, place: 1 }]);
    assert.equal(rows.length, 4);
    assert.equal(rows.reduce((sum, row) => sum + row.missing_pt_records, 0), 2);
    assert.equal(rows.find(row => Number(row.user_id) === 103).total_pt_change, null);
    // A PostgreSQL session's display timezone must not move local TIMESTAMP records.
    await db.query("SET LOCAL TIME ZONE 'Asia/Shanghai'");
    const shanghaiRows = await queryWeeklyPtChanges({ dateFrom: '2026-09-18', dateTo: '2026-09-24' }, db);
    assert.deepEqual(pickPtLeaders(shanghaiRows), { gainers, losers });
  } finally {
    await db.query('ROLLBACK');
    await db.end();
  }
});
