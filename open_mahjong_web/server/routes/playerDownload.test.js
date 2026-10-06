const test = require('node:test');
const assert = require('node:assert/strict');
const express = require('express');

// An isolated HTTP app with no database/config import and no real accounts.
// Exercise the existing ownership/quota path around the real ZIP service.
let context;
const db = { async query(sql, params) {
  context.queries.push({ sql, params });
  if (sql.includes('SELECT DISTINCT game_id FROM game_player_records')) {
    assert.equal(params[0], 100);
    if (context.pauseSelection) {
      context.selectionStarted();
      await context.pauseSelection;
    }
    return { rows: params[1].filter((id) => context.owned.has(id)).map((id) => ({ game_id: id })) };
  }
  if (sql.includes('SELECT gr.game_id FROM game_records')) {
    return { rows: params[0].filter((id) => context.visible.has(id)).map((id) => ({ game_id: id })) };
  }
  if (sql.includes('SELECT game_id FROM (')) {
    assert.equal(params[0], 100);
    return { rows: [...context.owned].map((id) => ({ game_id: id })) };
  }
  if (sql.includes('SELECT game_id, record FROM game_records')) {
    if (context.failRecords) throw new Error('record query failed');
    return { rows: params[0].filter((id) => context.visible.has(id)).map((id) => ({ game_id: id, record: { game_id: id, text: '完整牌谱' } })) };
  }
  throw new Error(`Unexpected query: ${sql}`);
} };
const restore = [];
function stub(name, exports) {
  const id = require.resolve(name);
  const previous = require.cache[id];
  require.cache[id] = { id, filename: id, loaded: true, exports };
  restore.push(() => { if (previous) require.cache[id] = previous; else delete require.cache[id]; });
}
stub('../config/database', db);
stub('../config/config', { isProduction: false, isDebug: true });
stub('../middleware/requirePlayer', { requirePlayer(req, res, next) {
  const userId = Number(req.headers['x-test-account']);
  if (!userId) return res.status(401).json({ success: false });
  req.player = { userId }; next();
} });
stub('../services/playerPublicApi', {
  parseRecordQuery: (input) => input,
  buildRecordFilters(userId, _query, params) { params.push(userId); return ['gpr.user_id = $1']; },
});
class QuotaExceededError extends Error {}
stub('../utils/recordDownloadQuota', {
  FETCH_MAX_GAMES: 20, QuotaExceededError,
  async consumeDownloadQuota(actor, requested, opts) {
    context.quotaCalls.push({ actor, requested, opts });
    if (context.lockDuringQuota) context.visible.delete(context.lockDuringQuota);
    return { consumed: Math.min(requested, context.allow), max: 200, remaining: 200 - requested };
  },
  quotaErrorPayload: () => ({ success: false }),
});
const router = require('./player');
for (const undo of restore) undo();

function names(zip) {
  const end = zip.lastIndexOf(Buffer.from('504b0506', 'hex'));
  assert.ok(end >= 0);
  let pos = zip.readUInt32LE(end + 16);
  const result = [];
  for (let i = 0; i < zip.readUInt16LE(end + 10); i++) {
    const size = zip.readUInt16LE(pos + 28);
    result.push(zip.subarray(pos + 46, pos + 46 + size).toString());
    pos += 46 + size + zip.readUInt16LE(pos + 30) + zip.readUInt16LE(pos + 32);
  }
  return result;
}

test('download HTTP route keeps ownership, visibility, caps and account quotas', async (t) => {
  const app = express(); app.use(express.json());
  app.use((_req, res, next) => {
    const requestContext = context;
    res.once('close', () => requestContext.responseClosed?.());
    next();
  });
  app.use('/api/player', router);
  const server = app.listen(0, '127.0.0.1');
  await new Promise((resolve) => server.once('listening', resolve));
  const url = `http://127.0.0.1:${server.address().port}/api/player/records/download`;
  const post = (body, actor = 1) => fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json', ...(actor ? { 'x-test-account': String(actor) } : {}) }, body: JSON.stringify(body) });
  const reset = (overrides = {}) => {
    context = { owned: new Set(['a', 'b', 'c', 'd']), visible: new Set(['a', 'b', 'd']), allow: 200, queries: [], quotaCalls: [], ...overrides };
  };
  try {
    await t.test('unauthenticated requests do not query records or consume quota', async () => {
      reset(); assert.equal((await post({ user_id: 100, game_ids: ['a'] }, 0)).status, 401);
      assert.equal(context.queries.length, 0); assert.equal(context.quotaCalls.length, 0);
    });
    await t.test('specified IDs retain order, discard unowned/locked entries, then apply both caps', async () => {
      reset({ allow: 2 });
      const res = await post({ user_id: 100, game_ids: ['d', 'unowned', 'c', 'a', 'b'], max_games: 3 }, 17);
      assert.equal(res.status, 200);
      assert.equal(res.headers.get('cache-control'), 'no-store');
      assert.deepEqual(names(Buffer.from(await res.arrayBuffer())), ['d.json', 'a.json']);
      assert.deepEqual(context.quotaCalls, [{ actor: 17, requested: 3, opts: { allowPartial: true } }]);
    });
    await t.test('filtered mode follows the DB ordering and requested cap', async () => {
      reset(); const res = await post({ user_id: 100, max_games: 2, rule: 'hongkong' });
      assert.deepEqual(names(Buffer.from(await res.arrayBuffer())), ['a.json', 'b.json']);
      assert.equal(context.quotaCalls[0].requested, 2);
    });
    await t.test('visibility is checked again after quota selection', async () => {
      reset({ lockDuringQuota: 'a' }); const res = await post({ user_id: 100, game_ids: ['a', 'b'] });
      assert.deepEqual(names(Buffer.from(await res.arrayBuffer())), ['b.json']);
    });
    await t.test('no accessible records and zero remaining quota keep their error statuses', async () => {
      reset(); assert.equal((await post({ user_id: 100, game_ids: ['c'] })).status, 404);
      assert.equal(context.quotaCalls.length, 0);
      reset({ allow: 0 }); assert.equal((await post({ user_id: 100, game_ids: ['a'] })).status, 429);
      assert.equal(context.queries.length, 2);
    });
    await t.test('disconnect during ID selection stops subsequent reads and quota consumption', async () => {
      let releaseSelection;
      let started;
      let closed;
      const selection = new Promise((resolve) => { started = resolve; });
      const responseClosed = new Promise((resolve) => { closed = resolve; });
      reset({ selectionStarted: started, responseClosed: closed,
        pauseSelection: new Promise((resolve) => { releaseSelection = resolve; }) });
      const controller = new AbortController();
      const request = fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json', 'x-test-account': '1' },
        body: JSON.stringify({ user_id: 100, game_ids: ['a'] }), signal: controller.signal });
      const cancelled = assert.rejects(request, { name: 'AbortError' });
      await selection;
      controller.abort();
      await cancelled;
      await responseClosed;
      releaseSelection();
      await new Promise((resolve) => setImmediate(resolve));
      assert.equal(context.queries.length, 1);
      assert.equal(context.quotaCalls.length, 0);
    });
    await t.test('initial record query failure returns JSON without an attachment header', async () => {
      reset({ failRecords: true });
      const originalError = console.error;
      console.error = () => {};
      try {
        const res = await post({ user_id: 100, game_ids: ['a'] });
        assert.equal(res.status, 500);
        assert.match(res.headers.get('content-type'), /application\/json/);
        assert.equal(res.headers.get('content-disposition'), null);
        assert.equal((await res.json()).success, false);
      } finally { console.error = originalError; }
    });
  } finally {
    server.closeAllConnections();
    await new Promise((resolve) => server.close(resolve));
  }
});
