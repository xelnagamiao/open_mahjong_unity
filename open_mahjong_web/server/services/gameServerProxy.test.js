const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
process.env.ADMIN_USER_IDS = '91000001';
process.env.ADMIN_JWT_SECRET = 'proxy-test-secret';
process.env.ADMIN_JWT_EXPIRES_SEC = '60';
process.env.BOT_API_JWT_SECRET = 'proxy-bot-test-secret';
const config = require('../config/config');
const { proxyToGameServer } = require('./gameServerProxy');

for (const method of ['GET', 'POST']) {
  test(`room proxy authenticates the exact ${method} path and payload`, async t => {
    const path = method === 'GET' ? '/admin/rooms' : '/admin/rooms/123/close';
    const body = method === 'GET' ? null : { instance_id: 'instance-123' };
    t.mock.method(global, 'fetch', async (url, options) => {
      assert.equal(new URL(url).pathname, path);
      assert.equal(options.method, method);
      assert.equal(options.body, body ? JSON.stringify(body) : undefined);
      const stamp = options.headers['x-room-admin-time'];
      assert.ok(Math.abs(Date.now() / 1000 - Number(stamp)) < 3);
      const hash = crypto.createHash('sha256').update(options.body || '').digest('hex');
      const expected = crypto.createHmac('sha256', config.playerAuth.jwtSecret)
        .update(['room-admin', stamp, method, path, hash].join('\n')).digest('hex');
      assert.equal(options.headers['x-room-admin-signature'], expected);
      return { status: 200, json: async () => ({ success: true }) };
    });
    assert.deepEqual(await proxyToGameServer(path, body, method), { status: 200, data: { success: true } });
  });
}
