const assert = require('node:assert/strict');
const test = require('node:test');
const express = require('express');
const { createRoomSettingsService } = require('./eventRoomSettings');
const { createRoomSettingsRouter, createSeatHandler } = require('../routes/event-admin/roomSettings');
const { readRoomSettings } = require('../utils/eventRoomSettings');

/** An isolated transactional store: pending changes publish only on COMMIT. */
function transactionPool({ status = 'active', settings = readRoomSettings({}) } = {}) {
  const state = {
    event: { status, room_settings: structuredClone(settings), entry_config: { join_code: 'untouched' } },
    members: new Set([10001, 10002]), audits: [], clients: [], failAudit: false, reads: [],
  };
  let lockTail = Promise.resolve();
  async function lock() {
    const previous = lockTail;
    let unlock;
    lockTail = new Promise((resolve) => { unlock = resolve; });
    await previous;
    return unlock;
  }
  const pool = {
    async query(sql, args = []) {
      state.reads.push(sql);
      if (sql.startsWith('SELECT e.status')) {
        return { rows: state.event ? [{ ...structuredClone(state.event), role: state.members.has(args[1]) ? 'admin' : null }] : [] };
      }
      assert.match(sql, /^SELECT room_settings FROM events/);
      return { rows: state.event ? [{ room_settings: structuredClone(state.event.room_settings) }] : [] };
    },
    async connect() {
      const record = { statements: [], released: false };
      state.clients.push(record);
      let pending;
      let pendingAudits = [];
      let unlock;
      let active = false;
      return {
        async query(sql, args = []) {
          const statement = sql.replace(/\s+/g, ' ').trim();
          record.statements.push(statement);
          if (statement === 'BEGIN') { active = true; return { rows: [] }; }
          assert.ok(active, 'all mutation queries use this transaction client');
          if (statement.endsWith('FOR UPDATE')) {
            unlock = await lock();
            pending = state.event ? structuredClone(state.event) : null;
            return { rows: pending ? [structuredClone(pending)] : [] };
          }
          if (statement.startsWith('SELECT role FROM event_admins')) {
            assert.ok(unlock, 'membership is rechecked after locking the current event');
            return { rows: state.members.has(args[1]) ? [{ role: 'admin' }] : [] };
          }
          if (statement.startsWith('UPDATE events SET room_settings')) {
            pending.room_settings = JSON.parse(args[0]);
            return { rows: [], rowCount: 1 };
          }
          if (statement.startsWith('INSERT INTO admin_audit_log')) {
            if (state.failAudit) throw new Error('audit insert unavailable');
            pendingAudits.push({ userId: args[0], action: args[1], payload: JSON.parse(args[3]) });
            return { rows: [], rowCount: 1 };
          }
          if (statement === 'COMMIT' || statement === 'ROLLBACK') {
            if (statement === 'COMMIT') {
              state.event = pending;
              state.audits.push(...pendingAudits);
            }
            active = false;
            pending = undefined;
            pendingAudits = [];
            if (unlock) unlock();
            unlock = undefined;
            return { rows: [] };
          }
          assert.fail(`Unexpected SQL in test store: ${statement}`);
        },
        release() {
          assert.equal(active, false, 'the transaction must finish before release');
          record.released = true;
        },
      };
    },
  };
  return { pool, state, service: createRoomSettingsService({ pool }) };
}

function statusError(statusCode) {
  return (error) => { assert.equal(error.statusCode, statusCode); return true; };
}

test('row locking lets exactly one of two same-revision concurrent saves commit', async () => {
  const { service, state } = transactionPool();
  const outcomes = await Promise.allSettled([
    service.update('event-1', 10001, { kind: 'manual', body: { revision: 0, room_config: { step_timer: 2 } } }),
    service.update('event-1', 10002, { kind: 'manual', body: { revision: 0, room_config: { step_timer: 8 } } }),
  ]);
  assert.equal(outcomes.filter((outcome) => outcome.status === 'fulfilled').length, 1);
  const rejected = outcomes.find((outcome) => outcome.status === 'rejected').reason;
  assert.equal(rejected.statusCode, 409);
  assert.equal(rejected.data.revision, 1);
  assert.equal(state.event.room_settings.revision, 1);
  assert.equal(state.event.room_settings.manual.room_config.step_timer, 2);
  assert.equal(state.event.room_settings.auto_match.room_config.step_timer, 2);
  assert.equal(state.audits.length, 1);
  assert.deepEqual(state.event.entry_config, { join_code: 'untouched' });
  assert.ok(state.clients.every((client) => client.released));
  assert.equal(state.clients[0].statements.at(-1), 'COMMIT');
  assert.equal(state.clients[1].statements.at(-1), 'ROLLBACK');
});

test('an audit failure rolls back settings and releases the lock for the next save', async () => {
  const { service, state } = transactionPool();
  state.failAudit = true;
  await assert.rejects(service.update('event-1', 10001, {
    kind: 'manual', body: { revision: 0, room_config: { step_timer: 50 } },
  }), /audit insert unavailable/);
  assert.equal(state.event.room_settings.revision, 0);
  assert.equal(state.event.room_settings.manual.room_config.step_timer, 5);
  assert.equal(state.event.room_settings.auto_match.room_config.step_timer, 5);
  assert.equal(state.audits.length, 0);
  assert.equal(state.clients[0].statements.at(-1), 'ROLLBACK');
  assert.equal(state.clients[0].released, true);
  state.failAudit = false;
  const saved = await service.update('event-1', 10001, { kind: 'manual', body: { revision: 0 } });
  assert.equal(saved.revision, 1);
  assert.equal(state.audits.length, 1);
});

test('service rechecks revoked membership and current venue status within the transaction', async () => {
  const { service, state } = transactionPool({ status: 'closed' });
  state.members.delete(10001);
  await assert.rejects(service.update('event-1', 10001, { kind: 'manual', body: { revision: 0 } }), statusError(403));
  state.members.add(10001);
  await assert.rejects(service.update('event-1', 10001, {
    kind: 'auto', body: { revision: 0, enabled: true },
  }), statusError(400));
  assert.equal(state.event.room_settings.revision, 0);
  const saved = await service.update('event-1', 10001, {
    kind: 'auto', body: { revision: 0, enabled: false },
  });
  assert.equal(saved.auto_match.enabled, false);
  assert.equal(saved.revision, 1);
  assert.equal(state.audits.length, 1);
  assert.ok(state.clients.every((client) => client.released));
});

test('read returns a detached document and missing events return 404 for reads and writes', async () => {
  const { service, state } = transactionPool();
  const read = await service.read('event-1');
  read.manual.room_config.step_timer = 90;
  assert.equal(state.event.room_settings.manual.room_config.step_timer, 5);
  state.event = null;
  await assert.rejects(service.read('missing'), statusError(404));
  await assert.rejects(service.update('missing', 10001, { kind: 'manual', body: { revision: 0 } }), statusError(404));
  assert.equal(state.clients[0].released, true);
});

async function httpFixture(t, {
  autoMatchService = async () => ({ enabled: true, waiting_count: 3 }),
  seatProxy = async () => ({ status: 200, data: { success: true, message: '组桌成功', room_info: { room_id: 'room-1' } } }),
  seatAudit,
} = {}) {
  const store = transactionPool();
  const membershipCalls = [];
  const app = express();
  app.use(express.json());
  const seatCalls = [];
  const seatAudits = [];
  const logErrors = [];
  const membership = (req, res, next) => {
    membershipCalls.push(req.params.eventId);
    if (req.headers['x-deny'] === 'true') return res.status(403).json({ success: false, message: '无权操作' });
    req.event = { event_id: req.params.eventId };
    req.eventAdmin = { userId: 10001 };
    next();
  };
  app.post('/api/event-admin/events/:eventId/seat', membership, createSeatHandler({
    service: store.service,
    proxyToGameServer: async (path, body) => {
      seatCalls.push({ path, body: structuredClone(body) });
      assert.ok(store.state.clients.every((client) => client.released), 'no database transaction is held while calling Python');
      return seatProxy(path, body, store);
    },
    writeAudit: seatAudit || (async (record) => { seatAudits.push(structuredClone(record)); }),
    logger: { error: (...args) => logErrors.push(args) },
  }));
  app.use('/api/event-admin/events/:eventId', createRoomSettingsRouter({
    service: store.service,
    membership,
    autoMatchService,
  }));
  const server = await new Promise((resolve) => {
    const listening = app.listen(0, '127.0.0.1', () => resolve(listening));
  });
  t.after(() => new Promise((resolve, reject) => {
    server.close((error) => error ? reject(error) : resolve());
    server.closeAllConnections();
  }));
  const base = `http://127.0.0.1:${server.address().port}/api/event-admin/events/event-1`;
  async function request(path, method = 'GET', body, headers = {}) {
    const response = await fetch(`${base}${path}`, {
      method,
      headers: { 'Content-Type': 'application/json', ...headers },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    return { status: response.status, ...await response.json() };
  }
  return { ...store, request, membershipCalls, seatCalls, seatAudits, logErrors };
}

test('HTTP nested routes retain eventId and carry revisions through manual, auto and preset CRUD', async (t) => {
  const runtimeCalls = [];
  const { request, state, membershipCalls } = await httpFixture(t, {
    autoMatchService: async (eventId, refresh = false) => {
      runtimeCalls.push({ eventId, refresh });
      return { enabled: true, waiting_count: 3 };
    },
  });
  const initial = await request('/room-settings');
  assert.equal(initial.status, 200);
  assert.equal(initial.data.revision, 0);
  const manual = await request('/room-settings', 'PUT', { revision: 0, room_config: { step_timer: 0 } });
  assert.equal(manual.data.revision, 1);
  assert.equal(manual.data.manual.room_config.step_timer, 0);
  const created = await request('/room-presets', 'POST', { revision: 1, name: '快速赛', room_config: { step_timer: 3 } });
  assert.equal(created.status, 200);
  assert.equal(created.data.revision, 2);
  const id = created.data.saved_preset_id;
  assert.ok(id);
  assert.equal(created.data.presets[0].preset_id, id);
  const edited = await request(`/room-presets/${id}`, 'PUT', { revision: 2, name: '快速训练' });
  assert.equal(edited.data.revision, 3);
  assert.equal(edited.data.presets[0].name, '快速训练');
  const automatic = await request('/auto-match', 'PUT', {
    revision: 3, enabled: true, preset_id: id,
  });
  assert.equal(automatic.data.revision, 4);
  assert.equal(automatic.data.auto_match.preset_id, id);
  assert.equal(automatic.data.runtime.waiting_count, 3);
  const boundEdit = await request(`/room-presets/${id}`, 'PUT', { revision: 4, room_config: { step_timer: 11 } });
  assert.equal(boundEdit.data.revision, 5);
  assert.equal(boundEdit.data.auto_match.room_config.step_timer, 11);
  assert.equal(state.event.room_settings.auto_match.room_config.step_timer, 11);
  const blocked = await request(`/room-presets/${id}`, 'DELETE', { revision: 5 });
  assert.equal(blocked.status, 400);
  assert.match(blocked.message, /先切换/);
  const switched = await request('/auto-match', 'PUT', { revision: 5, preset_id: null });
  assert.equal(switched.data.revision, 6);
  const deleted = await request(`/room-presets/${id}`, 'DELETE', { revision: 6 });
  assert.equal(deleted.data.revision, 7);
  assert.equal(deleted.data.auto_match.preset_id, null);
  assert.equal(deleted.data.auto_match.room_config.step_timer, 0);
  assert.deepEqual(deleted.data.presets, []);
  const conflict = await request('/room-settings', 'PUT', { revision: 4 });
  assert.equal(conflict.status, 409);
  assert.equal(conflict.success, false);
  assert.equal(conflict.data.revision, 7);
  const runtime = await request('/auto-match');
  assert.equal(runtime.status, 200);
  assert.deepEqual(runtimeCalls, [
    { eventId: 'event-1', refresh: true }, { eventId: 'event-1', refresh: true },
    { eventId: 'event-1', refresh: true }, { eventId: 'event-1', refresh: false },
  ]);
  assert.ok(membershipCalls.every((eventId) => eventId === 'event-1'));
  assert.equal('runtime' in state.event.room_settings, false);
  assert.equal('saved_preset_id' in state.event.room_settings, false);
  assert.equal(state.audits.length, 7);
});

test('HTTP membership denial blocks every route before service writes or runtime requests', async (t) => {
  let runtimeCalls = 0;
  const { request, state } = await httpFixture(t, { autoMatchService: async () => { runtimeCalls += 1; return {}; } });
  for (const [path, method] of [
    ['/room-settings', 'GET'], ['/auto-match', 'GET'], ['/room-settings', 'PUT'],
    ['/auto-match', 'PUT'], ['/room-presets', 'POST'], ['/room-presets/preset-1', 'PUT'],
    ['/room-presets/preset-1', 'DELETE'],
    ['/seat', 'POST'],
  ]) {
    const response = await request(path, method, method === 'GET' ? undefined : { revision: 0 }, { 'x-deny': 'true' });
    assert.equal(response.status, 403);
  }
  assert.equal(state.clients.length, 0);
  assert.equal(state.event.room_settings.revision, 0);
  assert.equal(runtimeCalls, 0);
});

test('runtime outage returns 503 for status but a successful durable auto save returns 200 with a warning', async (t) => {
  const { request, state } = await httpFixture(t, {
    autoMatchService: async () => { throw new Error('runtime offline'); },
  });
  const runtime = await request('/auto-match');
  assert.equal(runtime.status, 503);
  assert.equal(runtime.success, false);
  const saved = await request('/auto-match', 'PUT', { revision: 0, enabled: true });
  assert.equal(saved.status, 200);
  assert.equal(saved.success, true);
  assert.match(saved.message, /设置已保存/);
  assert.match(saved.message, /暂未响应/);
  assert.equal(saved.data.revision, 1);
  assert.equal(saved.data.auto_match.enabled, true);
  assert.equal(state.event.room_settings.auto_match.enabled, true);
  assert.equal(state.audits.length, 1);
});

function tableSettings() {
  return readRoomSettings({
    version: 2, revision: 4,
    manual: { room_rule: 'guobiao', room_config: { step_timer: 20 } },
    presets: [{ preset_id: 'fast', name: '快速赛', room_rule: 'guobiao', room_config: { step_timer: 2 } }],
  });
}

test('HTTP seating resolves the selected stored preset for this table without changing any shared settings', async (t) => {
  const { request, state, seatCalls, seatAudits } = await httpFixture(t);
  state.event.room_settings = tableSettings();
  const original = structuredClone(state.event.room_settings);
  const response = await request('/seat', 'POST', { revision: 4, preset_id: 'fast', user_ids: [101, 102, 103, 104], reason: '本桌快赛' });
  assert.equal(response.status, 200);
  assert.equal(response.success, true);
  assert.equal(seatCalls.length, 1);
  assert.equal(seatCalls[0].path, '/admin/event/rooms/seat');
  assert.equal(seatCalls[0].body.room_config.step_timer, 2);
  assert.equal(seatCalls[0].body.created_by, 10001);
  assert.equal(seatAudits[0].payload.preset_name, '快速赛');
  assert.equal(seatAudits[0].payload.settings_revision, 4);
  assert.equal(seatAudits[0].payload.room_config.step_timer, 2);
  assert.deepEqual(state.event.room_settings, original);
  assert.equal(state.clients.length, 0);
  const defaultTable = await request('/seat', 'POST', { revision: 4, preset_id: null, user_ids: [105, 106, 107, 108] });
  assert.equal(defaultTable.status, 200);
  assert.equal(seatCalls[1].body.room_config.step_timer, 20);
  assert.equal(seatAudits[1].payload.preset_name, '默认配置');
  assert.deepEqual(state.event.room_settings, original);
});

test('a concurrent preset edit after confirmation leaves the in-flight table bound to its confirmed snapshot', async (t) => {
  const { request, state, seatCalls, seatAudits } = await httpFixture(t, {
    seatProxy: async (_path, body, store) => {
      await store.service.update('event-1', 10002, {
        kind: 'preset-update', presetId: 'fast', body: { revision: 4, room_config: { step_timer: 11 } },
      });
      assert.equal(body.room_config.step_timer, 2);
      return { status: 200, data: { success: true, room_info: { room_id: 'confirmed-table' } } };
    },
  });
  state.event.room_settings = tableSettings();
  const response = await request('/seat', 'POST', { revision: 4, preset_id: 'fast', user_ids: [101, 102, 103, 104] });
  assert.equal(response.status, 200);
  assert.equal(seatCalls[0].body.room_config.step_timer, 2);
  assert.equal(seatAudits[0].payload.settings_revision, 4);
  assert.equal(state.event.room_settings.presets[0].room_config.step_timer, 11);
  assert.equal(state.event.room_settings.manual.room_config.step_timer, 20);
  assert.equal(state.event.room_settings.revision, 5);
});

test('seat validation rejects stale selection, removed presets, invalid IDs, raw overrides and revoked access before Python', async (t) => {
  const { request, state, seatCalls } = await httpFixture(t);
  state.event.room_settings = tableSettings();
  const valid = { revision: 4, preset_id: 'fast', user_ids: [101, 102, 103, 104] };
  const stale = await request('/seat', 'POST', { ...valid, revision: 3 });
  assert.equal(stale.status, 409);
  assert.equal(stale.data.revision, 4);
  for (const body of [
    { ...valid, preset_id: 'missing' }, { ...valid, user_ids: [101, 102, 103, 103] },
    { ...valid, user_ids: ['101x', 102, 103, 104] }, { ...valid, room_config: { step_timer: 1 } },
    { ...valid, room_rule: 'riichi' },
  ]) assert.equal((await request('/seat', 'POST', body)).status, 400);
  state.members.delete(10001);
  assert.equal((await request('/seat', 'POST', valid)).status, 403);
  state.members.add(10001);
  state.event.status = 'closed';
  assert.equal((await request('/seat', 'POST', valid)).status, 400);
  assert.equal(seatCalls.length, 0);
  assert.equal(state.event.room_settings.revision, 4);
});

test('Python business failure inside HTTP 200 is returned as failure and does not generate a successful-seat audit', async (t) => {
  const { request, seatAudits, state } = await httpFixture(t, {
    seatProxy: async () => ({ status: 200, data: { success: false, message: '准备玩家已变化' } }),
  });
  const response = await request('/seat', 'POST', { revision: 0, preset_id: null, user_ids: [101, 102, 103, 104] });
  assert.equal(response.status, 400);
  assert.equal(response.success, false);
  assert.equal(response.message, '准备玩家已变化');
  assert.equal(seatAudits.length, 0);
  assert.equal(state.event.room_settings.revision, 0);
});

test('a seat audit failure cannot turn an already successful table into a retryable failure', async (t) => {
  const { request, seatCalls, logErrors } = await httpFixture(t, {
    seatAudit: async () => { throw new Error('audit unavailable'); },
  });
  const response = await request('/seat', 'POST', { revision: 0, preset_id: null, user_ids: [101, 102, 103, 104] });
  assert.equal(response.status, 200);
  assert.equal(response.success, true);
  assert.equal(response.audit_warning, true);
  assert.equal(response.data.room_info.room_id, 'room-1');
  assert.match(response.message, /请勿重复组桌/);
  assert.equal(seatCalls.length, 1);
  assert.equal(logErrors.length, 1);
});

test('v1 migration is read-only until a successful revision-checked mutation persists the shared preset', async () => {
  const legacy = readRoomSettings({});
  legacy.version = 1;
  legacy.auto_match = { ...legacy.auto_match, enabled: true, room_config: { step_timer: 17 } };
  const { service, state } = transactionPool({ settings: legacy });
  const migrated = await service.read('event-1');
  assert.equal(migrated.version, 2);
  assert.equal(migrated.presets.length, 1);
  assert.equal(state.event.room_settings.version, 1);
  assert.equal(state.event.room_settings.presets.length, 0);
  assert.equal(state.clients.length, 0);
  const selection = await service.resolveForSeat('event-1', 10001, {
    revision: 0, preset_id: migrated.auto_match.preset_id, user_ids: [101, 102, 103, 104],
  });
  assert.equal(selection.room_config.step_timer, 17);
  assert.equal(state.event.room_settings.version, 1);
  const saved = await service.update('event-1', 10001, { kind: 'manual', body: { revision: 0, room_config: { step_timer: 9 } } });
  assert.equal(saved.version, 2);
  assert.equal(saved.revision, 1);
  assert.equal(saved.auto_match.room_config.step_timer, 17);
  assert.equal(state.event.room_settings.presets[0].preset_id, migrated.auto_match.preset_id);
  assert.equal(state.audits.length, 1);
});
