const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const express = require('express');
process.env.ADMIN_USER_IDS = '91000001';
process.env.ADMIN_JWT_SECRET = 'event-wall-admin-tests';
process.env.ADMIN_JWT_EXPIRES_SEC = '60';
process.env.EVENT_ADMIN_JWT_SECRET = 'event-wall-event-tests';
process.env.PLAYER_JWT_SECRET = 'event-wall-player-tests';
process.env.BOT_API_JWT_SECRET = 'event-wall-bot-tests';
const config = require('../config/config');
const jwt = require('../utils/jwt');
const { requireAdmin } = require('../middleware/requireAdmin');
const { createEventDuplicateWallService } = require('../services/eventDuplicateWalls');

function load(relative, imports) {
  const filename = path.resolve(__dirname, relative);
  const sandbox = { module: { exports: {} }, console: { error() {} }, require(name) {
    if (Object.hasOwn(imports, name)) return imports[name];
    throw new Error(`Unexpected import ${name}`);
  } };
  vm.runInNewContext(fs.readFileSync(filename, 'utf8'), sandbox, { filename });
  return sandbox.module.exports;
}

test('manager key routes require their own authentication, event membership and platform audit reason', async () => {
  const calls = [], audits = [];
  let quotaFailure = false;
  const event = { event_id: 'owned-event', kind: 'event', status: 'active' };
  const db = { async query(sql, values) {
    if (sql.includes('FROM event_admins')) return { rows: values[0] === event.event_id && values[1] === 101 ? [{ role: 'admin' }] : [] };
    return { rows: values[0] === 'missing' ? [] : [{ ...event, event_id: values[0] }] };
  } };
  const eventAuth = load('../middleware/requireEventAdmin.js', { '../config/database': db, '../config/config': config, '../utils/jwt': jwt });
  const imports = {
    express, '../../config/database': db, '../../config/config': config,
    '../../utils/audit': { writeAudit: async (audit, transaction) => { assert.equal(transaction, db); audits.push(audit); } },
    '../../utils/eventsTables': {}, '../../services/eventPlayerStats': {}, '../../services/gameServerProxy': {},
    '../../services/duplicateWalls': { createDuplicateWallService: () => ({ async createBatch(userId, input, count, options) {
      if (quotaFailure) throw Object.assign(new Error('今日额度已用尽'), { status: 429 });
      calls.push({ userId, input, count, platformAdmin: options.platformAdmin });
      const result = { walls: [{ id: 1, key: 'dup_server_key', rule: input.rule, round_count: input.round_count }], quota: { remaining_today: 24 } };
      await options.beforeCommit(result, db);
      return result;
    } }) },
    '../../services/eventDuplicateWalls': { createEventDuplicateWallService },
    '../../services/eventRoomCreation': { createEventRoomCreationService: () => ({}) },
    '../../services/eventRoomBots': { createEventRoomBotService: () => ({}) },
    '../../middleware/requireEventAdmin': eventAuth,
    '../../utils/eventAdminHelpers': {}, '../../services/recordMetadataQuery': {}, '../../services/duplicateRecordAccess': {},
    '../../services/eventRoomSettings': { createRoomSettingsService: () => ({}) },
    './roomSettings': { createSeatHandler: () => (_req, _res, next) => next() },
  };
  const app = express(); app.use(express.json());
  app.use('/event', eventAuth.requireEventAdmin, load('event-admin/events.js', imports));
  app.use('/admin', requireAdmin, load('admin/events.js', imports));
  const server = app.listen(0, '127.0.0.1');
  await new Promise(resolve => server.once('listening', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  const eventToken = user => jwt.signToken({ user_id: user, aud: config.eventAdmin.audience }, config.eventAdmin.jwtSecret, 60);
  const adminToken = user => jwt.signToken({ user_id: user }, config.admin.jwtSecret, 60);
  const playerToken = jwt.signToken({ user_id: 101, aud: config.playerAuth.audience }, config.playerAuth.jwtSecret, 60);
  const body = { rule: 'sichuan/sbr', wall_type: 'key', round_count: 8, use_flowers: false, event_id: 'foreign', scope: 'personal' };
  const post = (route, token, data = body) => fetch(`${base}${route}`, { method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: JSON.stringify(data) });
  try {
    assert.equal((await post('/event/owned-event/duplicate-walls')).status, 401);
    assert.equal((await post('/event/owned-event/duplicate-walls', playerToken)).status, 401);
    assert.equal((await post('/event/owned-event/duplicate-walls', eventToken(102))).status, 403);
    assert.equal((await post('/event/foreign/duplicate-walls', eventToken(101))).status, 403);
    assert.equal(calls.length, 0);
    const created = await post('/event/owned-event/duplicate-walls', eventToken(101));
    assert.equal(created.status, 201);
    assert.equal((await created.json()).data.wall.key, 'dup_server_key');
    assert.equal(calls[0].input.event_id, event.event_id);
    assert.equal(calls[0].input.scope, 'event');
    assert.equal(calls[0].userId, 101);
    assert.equal(calls[0].platformAdmin, false);
    assert.equal((await post('/admin/owned-event/duplicate-walls')).status, 401);
    assert.equal((await post('/admin/owned-event/duplicate-walls', adminToken(102))).status, 403);
    assert.equal((await post('/admin/owned-event/duplicate-walls', adminToken(91000001))).status, 400);
    assert.equal((await post('/admin/missing/duplicate-walls', adminToken(91000001), { ...body, reason: '建房' })).status, 404);
    assert.equal((await post('/admin/owned-event/duplicate-walls', adminToken(91000001), { ...body, reason: '建房' })).status, 201);
    assert.equal(calls[1].platformAdmin, true);
    assert.equal(audits[1].reason, '建房');
    quotaFailure = true;
    const denied = await post('/event/owned-event/duplicate-walls', eventToken(101));
    assert.equal(denied.status, 429);
    assert.equal((await denied.json()).message, '今日额度已用尽');
    assert.equal(audits.length, 2);
  } finally {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  }
});
