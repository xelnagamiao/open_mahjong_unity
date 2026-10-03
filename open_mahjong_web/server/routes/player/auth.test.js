const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createRequire } = require('node:module');
const { verifyToken } = require('../../utils/jwt');

const authPath = path.join(__dirname, 'auth.js');
const config = {
  playerAuth: { audience: 'player-test', jwtSecret: 'local-test-only-secret', jwtExpiresSec: 60 },
  eventAdmin: { audience: 'event-test', jwtSecret: 'local-event-test-secret', jwtExpiresSec: 60 },
  smtp: { enabled: false },
};

function loadMeHandler(user) {
  const routes = new Map();
  const router = {
    get(route, ...handlers) { routes.set(route, handlers.at(-1)); },
    post() {},
  };
  const pool = {
    async query(_sql, params) {
      assert.deepEqual([...params], [101]);
      if (user instanceof Error) throw user;
      return { rows: user ? [user] : [] };
    },
  };
  const overrides = {
    express: { Router: () => router },
    '../../config/database': pool,
    '../../config/config': config,
    '../../middleware/requirePlayer': { requirePlayer() {} },
    '../../middleware/rateLimit': { createWindowLimiter: () => () => {}, getClientIp: () => '127.0.0.1' },
    '../../utils/eventAdminHelpers': { listUserEvents: async () => [] },
    '../../utils/mailer': {},
    '../../utils/passwordReset': { createPasswordResetHandlers: () => ({}) },
  };
  const realRequire = createRequire(authPath);
  const context = {
    module: { exports: {} },
    require: id => Object.hasOwn(overrides, id) ? overrides[id] : realRequire(id),
    console: { error() {} },
  };
  vm.runInNewContext(fs.readFileSync(authPath, 'utf8'), context, { filename: authPath });
  return routes.get('/me');
}

async function fetchMe(username, user = { username: '新名字', rename_count: 0 }) {
  const res = {
    statusCode: 200,
    status(value) { this.statusCode = value; return this; },
    json(value) { this.body = value; return this; },
  };
  await loadMeHandler(user)({ player: { userId: 101, username } }, res);
  return res;
}

test('/me refreshes the signed username after a lost rename response', async () => {
  const res = await fetchMe('旧名字');
  assert.equal(res.statusCode, 200);
  assert.equal(res.body.data.username, '新名字');
  assert.equal(res.body.data.rename_count, 0);
  const claims = verifyToken(res.body.data.token, config.playerAuth.jwtSecret);
  assert.equal(claims.user_id, 101);
  assert.equal(claims.username, '新名字');
  assert.equal(claims.aud, config.playerAuth.audience);
});

test('/me does not rotate an unchanged player token', async () => {
  const res = await fetchMe('新名字');
  assert.equal(res.statusCode, 200);
  assert.equal(res.body.success, true);
  assert.equal(res.body.data.token, undefined);
});

test('/me cannot issue a refreshed token for a missing account', async () => {
  const res = await fetchMe('旧名字', null);
  assert.equal(res.statusCode, 404);
  assert.equal(res.body.success, false);
});

test('/me cannot confirm a rename when the account query fails', async () => {
  const res = await fetchMe('旧名字', new Error('database unavailable'));
  assert.equal(res.statusCode, 500);
  assert.equal(res.body.success, false);
});
