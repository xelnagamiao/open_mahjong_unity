const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const express = require('express');
process.env.ADMIN_USER_IDS = '91000001';
process.env.ADMIN_JWT_SECRET = 'profile-tests-local-only';
process.env.ADMIN_JWT_EXPIRES_SEC = '60';
process.env.BOT_API_JWT_SECRET = 'profile-tests-bot-local-only';
const { requireAdmin } = require('../../middleware/requireAdmin');
const { signToken } = require('../../utils/jwt');
const config = require('../../config/config');

function loadRouter(relative, imports) {
  const filename = path.join(__dirname, relative);
  const sandbox = { module: { exports: {} }, console: { error() {} }, require(name) {
    if (name === 'express') return express;
    if (Object.hasOwn(imports, name)) return imports[name];
    throw new Error(`Unexpected import ${name}`);
  } };
  vm.runInNewContext(fs.readFileSync(filename, 'utf8'), sandbox, { filename });
  return sandbox.module.exports;
}

async function withServer(app, callback) {
  const server = app.listen(0, '127.0.0.1');
  await new Promise(resolve => server.once('listening', resolve));
  try { await callback(`http://127.0.0.1:${server.address().port}`); }
  finally {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  }
}

function setupAdmin(kind = 'event') {
  const state = {
    event: { event_id: 'venue-test', kind, name: '原名称', description: '原介绍', status: 'active', reopen_requested: false },
    audit: [], connections: 0, released: 0, failAudit: false,
  };
  const client = {
    async query(sql, params = []) {
      if (sql === 'BEGIN') { state.before = { ...state.event }; return { rows: [] }; }
      if (sql === 'COMMIT') { state.before = null; return { rows: [] }; }
      if (sql === 'ROLLBACK') { if (state.before) state.event = state.before; state.before = null; return { rows: [] }; }
      if (sql.startsWith('SELECT')) return { rows: params[0] === state.event.event_id ? [{ ...state.event }] : [] };
      if (sql.startsWith('UPDATE events')) {
        state.event = { ...state.event, name: params[0], description: params[1] };
        return { rows: [{ ...state.event }] };
      }
      throw new Error(`Unexpected query ${sql}`);
    },
    release() { state.released += 1; },
  };
  const router = loadRouter('events.js', {
    '../../config/database': { async connect() { state.connections += 1; return client; } },
    '../../config/config': { calcServer: { baseUrl: 'http://127.0.0.1:8081', timeoutMs: 1000 } },
    '../../utils/audit': { async writeAudit(audit, db) {
      assert.equal(db, client, 'profile update and audit must use the same transaction');
      if (state.failAudit) throw new Error('audit failed');
      state.audit.push(audit);
    } },
    '../../utils/eventsTables': {},
    '../../services/eventPlayerStats': {},
    '../../services/duplicateWalls': { createDuplicateWallService: () => ({}) },
    '../../services/eventRoomCreation': { createEventRoomCreationService: () => ({}) },
    '../../services/eventDuplicateWalls': { createEventDuplicateWallService: () => ({}) },
    '../../services/eventRoomBots': { createEventRoomBotService: () => ({}) },
    '../../services/gameServerProxy': {},
  });
  const app = express();
  app.use(express.json());
  app.use('/events', requireAdmin, router);
  const token = signToken({ user_id: 91000001, username: 'test-admin' }, config.admin.jwtSecret, 60);
  const put = (base, body, eventId = 'venue-test', auth = token) => fetch(`${base}/events/${eventId}/profile`, {
    method: 'PUT', headers: { 'Content-Type': 'application/json', ...(auth ? { Authorization: `Bearer ${auth}` } : {}) },
    body: JSON.stringify(body),
  });
  return { state, app, put };
}

for (const kind of ['event', 'base']) {
  test(`administrator updates ${kind} profile directly, preserving lifecycle and recording before/after`, async () => {
    const { state, app, put } = setupAdmin(kind);
    await withServer(app, async base => {
      const response = await put(base, { name: ' 新名称 ', description: ' 新介绍\n第二行 ', reason: ' 修正介绍 ', kind: 'ignored', status: 'closed' });
      assert.equal(response.status, 200);
      const { data } = await response.json();
      assert.equal(data.event.name, '新名称');
      assert.equal(data.event.description, '新介绍\n第二行');
      assert.equal(data.event.kind, kind);
      assert.equal(data.event.status, 'active');
      assert.equal(state.audit.length, 1);
      assert.equal(state.audit[0].adminUserId, 91000001);
      assert.equal(state.audit[0].payload.before.name, '原名称');
      assert.equal(state.audit[0].payload.after.name, '新名称');
      assert.equal(state.audit[0].reason, '修正介绍');
      assert.equal(state.released, 1);
    });
  });
}

test('profile update rejects unauthenticated/non-admin requests and invalid inputs without mutations', async () => {
  const { state, app, put } = setupAdmin();
  await withServer(app, async base => {
    const valid = { name: '新名称', description: '新介绍', reason: '修正' };
    assert.equal((await put(base, valid, 'venue-test', '')).status, 401);
    const playerToken = signToken({ user_id: 91000002 }, config.admin.jwtSecret, 60);
    assert.equal((await put(base, valid, 'venue-test', playerToken)).status, 403);
    for (const patch of [{ name: ' ' }, { name: '名'.repeat(129) }, { description: '文'.repeat(2001) }, { reason: '' }]) {
      assert.equal((await put(base, { ...valid, ...patch })).status, 400);
    }
    assert.equal(state.connections, 0);
    assert.equal((await put(base, valid, 'missing')).status, 404);
    assert.equal((await put(base, { name: '原名称', description: '原介绍', reason: '无变化' })).status, 400);
    assert.equal(state.event.name, '原名称');
    assert.equal(state.audit.length, 0);
  });
});

test('profile update rolls back when the audit cannot be saved', async () => {
  const { state, app, put } = setupAdmin();
  state.failAudit = true;
  await withServer(app, async base => {
    assert.equal((await put(base, { name: '新名称', description: '新介绍', reason: '修正' })).status, 500);
    assert.equal(state.event.name, '原名称');
    assert.equal(state.event.description, '原介绍');
    assert.equal(state.released, 1);
  });
});

test('only the latest rejected profile request is offered for editing, never an older rejection', async () => {
  let latestStatus = 'rejected';
  const router = loadRouter('../event-admin/announcements.js', {
    '../../config/database': { async query(sql) {
      if (sql.includes("r.status = 'pending'")) return { rows: [] };
      assert.doesNotMatch(sql, /WHERE[^`]*r.status = 'rejected'/);
      return { rows: [{ request_id: 2, status: latestStatus, proposed_name: '拟改名称', proposed_description: '拟改介绍', review_note: '请补充资料' }] };
    } },
    '../../utils/audit': {},
    '../../middleware/requireEventAdmin': {
      requireEventMembership(req, _res, next) { req.event = { event_id: 'venue-test', name: '当前名称', description: '当前介绍' }; next(); },
      requireEventOwner(_req, _res, next) { next(); },
    },
  });
  const app = express();
  app.use('/events/:eventId', router);
  await withServer(app, async base => {
    const read = async () => (await (await fetch(`${base}/events/venue-test/profile-change`)).json()).data;
    assert.equal((await read()).last_rejected.proposed_name, '拟改名称');
    latestStatus = 'approved';
    assert.equal((await read()).last_rejected, null);
    latestStatus = 'cancelled';
    assert.equal((await read()).last_rejected, null);
  });
});
