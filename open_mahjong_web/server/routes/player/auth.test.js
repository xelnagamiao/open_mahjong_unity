const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createRequire } = require('node:module');
const { signToken, verifyToken } = require('../../utils/jwt');
const { hashPassword } = require('../../utils/password');

const authPath = path.join(__dirname, 'auth.js');
const config = {
  playerAuth: { audience: 'player-test', jwtSecret: 'local-test-only-secret', jwtExpiresSec: 8 * 60 * 60, rememberJwtExpiresSec: 30 * 24 * 60 * 60 },
  eventAdmin: { audience: 'event-test', jwtSecret: 'local-event-test-secret', jwtExpiresSec: 60 },
  smtp: { enabled: false },
};

const loginUser = { user_id: 101, username: '测试账号', password: hashPassword('test-password'), is_tourist: false };

function loadAuthRoutes(user, { events = [], account = loginUser } = {}) {
  const routes = new Map();
  const router = {
    get(route, ...handlers) { routes.set(`GET ${route}`, handlers.at(-1)); },
    post(route, ...handlers) { routes.set(`POST ${route}`, handlers.at(-1)); },
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
    '../../utils/eventAdminHelpers': { listUserEvents: async () => events },
    '../../utils/loginAccount': { findLoginAccount: async () => ({ user: account }) },
    '../../utils/mailer': {},
    '../../utils/passwordReset': { createPasswordResetHandlers: () => ({}) },
  };
  const realRequire = createRequire(authPath);
  const context = {
    module: { exports: {} },
    require: id => Object.hasOwn(overrides, id) ? overrides[id] : realRequire(id),
    console: { error() {} },
    Date,
  };
  vm.runInNewContext(fs.readFileSync(authPath, 'utf8'), context, { filename: authPath });
  return routes;
}

function response() {
  return {
    statusCode: 200,
    status(value) { this.statusCode = value; return this; },
    json(value) { this.body = value; return this; },
  };
}

async function fetchMe(username, user = { username: '新名字', rename_count: 0 }, expiresAt) {
  const res = response();
  await loadAuthRoutes(user).get('GET /me')({ player: { userId: 101, username, expiresAt } }, res);
  return res;
}

function loadPlayerMiddleware() {
  const middlewarePath = path.join(__dirname, '../../middleware/requirePlayer.js');
  const context = {
    module: { exports: {} },
    require: id => id === '../config/config' ? config : createRequire(middlewarePath)(id),
  };
  vm.runInNewContext(fs.readFileSync(middlewarePath, 'utf8'), context, { filename: middlewarePath });
  return context.module.exports;
}

async function login(body, options) {
  const res = response();
  await loadAuthRoutes(loginUser, options).get('POST /login')({ body }, res);
  return res;
}

for (const [label, keepLoggedIn, ttl] of [
  ['30 days when selected', true, 30 * 24 * 60 * 60],
  ['8 hours when unselected', false, 8 * 60 * 60],
  ['8 hours for older clients', undefined, 8 * 60 * 60],
  ['8 hours for a string instead of a boolean', 'true', 8 * 60 * 60],
]) {
  test(`/login issues ${label}`, async (t) => {
    const now = 1800000000;
    t.mock.method(Date, 'now', () => now * 1000);
    const res = await login({ username: loginUser.username, password: 'test-password', keep_logged_in: keepLoggedIn });
    assert.equal(res.statusCode, 200);
    assert.equal(res.body.data.expires_in, ttl);
    const claims = verifyToken(res.body.data.token, config.playerAuth.jwtSecret);
    assert.equal(claims.user_id, 101);
    assert.equal(claims.aud, config.playerAuth.audience);
    assert.equal(claims.exp, now + ttl);
  });
}

test('/login rejects incorrect credentials even when 30 days is selected', async () => {
  const res = await login({ username: loginUser.username, password: 'incorrect-password', keep_logged_in: true });
  assert.equal(res.statusCode, 401);
  assert.equal(res.body.success, false);
  assert.equal(res.body.data, undefined);
});

test('30-day player login keeps event-admin credentials at their own configured lifetime', async (t) => {
  const now = 1800000000;
  t.mock.method(Date, 'now', () => now * 1000);
  const res = await login({ username: loginUser.username, password: 'test-password', keep_logged_in: true }, {
    events: [{ event_id: 'test-event' }],
  });
  const claims = verifyToken(res.body.data.event_admin_token, config.eventAdmin.jwtSecret);
  assert.equal(claims.aud, config.eventAdmin.audience);
  assert.equal(claims.exp, now + config.eventAdmin.jwtExpiresSec);
});

for (const remaining of [7 * 24 * 60 * 60, 60]) {
  test(`/me repairs a username while preserving ${remaining} seconds of the original session`, async (t) => {
    const now = 1800000000;
    t.mock.method(Date, 'now', () => now * 1000);
    const res = await fetchMe('旧名字', { username: '新名字', rename_count: 0 }, now + remaining);
    const claims = verifyToken(res.body.data.token, config.playerAuth.jwtSecret);
    assert.equal(claims.exp, now + remaining);
    assert.equal(res.body.data.expires_in, remaining);
  });
}

test('player middleware accepts a 30-day token after 8 hours and rejects it after expiry', (t) => {
  const now = 1800000000;
  t.mock.method(Date, 'now', () => now * 1000);
  const token = signToken({ user_id: 101, username: '测试账号', aud: config.playerAuth.audience }, config.playerAuth.jwtSecret, config.playerAuth.rememberJwtExpiresSec);
  const middleware = loadPlayerMiddleware();
  t.mock.method(Date, 'now', () => (now + 9 * 60 * 60) * 1000);
  const req = { headers: { authorization: `Bearer ${token}` } };
  let passed = false;
  middleware.requirePlayer(req, response(), () => { passed = true; });
  assert.equal(passed, true);
  assert.equal(req.player.expiresAt, now + config.playerAuth.rememberJwtExpiresSec);
  t.mock.method(Date, 'now', () => (now + config.playerAuth.rememberJwtExpiresSec + 1) * 1000);
  const res = response();
  middleware.requirePlayer({ headers: req.headers }, res, () => assert.fail('Expired token must not pass'));
  assert.equal(res.statusCode, 401);
});

test('player middleware still rejects an expired short session', (t) => {
  const now = 1800000000;
  t.mock.method(Date, 'now', () => now * 1000);
  const token = signToken({ user_id: 101, aud: config.playerAuth.audience }, config.playerAuth.jwtSecret, config.playerAuth.jwtExpiresSec);
  t.mock.method(Date, 'now', () => (now + config.playerAuth.jwtExpiresSec + 1) * 1000);
  const res = response();
  loadPlayerMiddleware().requirePlayer({ headers: { authorization: `Bearer ${token}` } }, res, () => assert.fail('Expired token must not pass'));
  assert.equal(res.statusCode, 401);
});

/* Existing username-recovery regressions. */

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
