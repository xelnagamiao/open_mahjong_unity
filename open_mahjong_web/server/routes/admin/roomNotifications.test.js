const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const express = require('express');
process.env.ADMIN_USER_IDS = '91000001';
process.env.ADMIN_JWT_SECRET = 'room-tests-local-only';
process.env.ADMIN_JWT_EXPIRES_SEC = '60';
process.env.BOT_API_JWT_SECRET = 'room-tests-bot-local-only';
const { requireAdmin } = require('../../middleware/requireAdmin');
const { signToken } = require('../../utils/jwt');
const config = require('../../config/config');

function applicationRouter(pool) {
  const filename = path.join(__dirname, 'eventApplications.js');
  const sandbox = { module: { exports: {} }, console, require(name) {
    if (name === '../../config/database') return pool;
    if (name === 'express') return express;
    if (name === '../../utils/audit') return { writeAudit: async () => {} };
    if (name === '../../utils/eventsTables') return {};
    if (name === '../../utils/applicationRemarks') return {};
    throw new Error(`Unexpected import ${name}`);
  } };
  vm.runInNewContext(fs.readFileSync(filename, 'utf8'), sandbox, { filename });
  return sandbox.module.exports;
}

test('badge count API is administrator-only, uncached, and counts pending event/base separately', async () => {
  let queries = 0;
  const app = express();
  app.use('/api/admin/event-applications', requireAdmin, applicationRouter({ query: async sql => {
    queries += 1;
    assert.match(sql, /WHERE status = 'pending'/);
    assert.match(sql, /kind = 'event'/);
    assert.match(sql, /kind = 'base'/);
    return { rows: [{ event: 2, base: 3 }] };
  } }));
  // The same guard used by the admin router must block the room-management route too.
  app.use('/api/admin/room-control', requireAdmin, (_req, res) => res.json({ success: true }));
  const server = app.listen(0, '127.0.0.1');
  await new Promise(resolve => server.once('listening', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  try {
    assert.equal((await fetch(`${base}/api/admin/event-applications/pending-counts`)).status, 401);
    assert.equal((await fetch(`${base}/api/admin/room-control`)).status, 401);
    assert.equal(queries, 0);
    const token = signToken({ user_id: [...config.admin.userIds][0], username: 'test-admin' }, config.admin.jwtSecret, 60);
    const response = await fetch(`${base}/api/admin/event-applications/pending-counts`, { headers: { Authorization: `Bearer ${token}` } });
    assert.equal(response.status, 200);
    assert.equal(response.headers.get('cache-control'), 'no-store');
    assert.deepEqual((await response.json()).data, { event: 2, base: 3 });
    assert.equal(queries, 1);
    const player = signToken({ user_id: 99999999 }, config.admin.jwtSecret, 60);
    assert.equal((await fetch(`${base}/api/admin/room-control`, { headers: { Authorization: `Bearer ${player}` } })).status, 403);
  } finally {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  }
});
