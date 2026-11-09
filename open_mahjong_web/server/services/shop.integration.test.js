const test = require('node:test');
const assert = require('node:assert/strict');
const express = require('express');
const config = require('../config/config');
const { requireAdmin } = require('../middleware/requireAdmin');
const { signToken } = require('../utils/jwt');

test('shop proxy requires an administrator and forwards catalog changes and transaction filters', async () => {
  const calls = [];
  const upstream = express(); upstream.use(express.json());
  upstream.use((req, res) => {
    calls.push({ path: req.path, query: req.query, token: req.headers.authorization, body: req.body });
    if (req.body?.reason === 'stale') return res.status(409).json({ detail: '商品已被其他管理员修改' });
    res.json({ success: true, data: { offers: [], items: [], total: 0 } });
  });
  const source = upstream.listen(0, '127.0.0.1');
  await new Promise(resolve => source.once('listening', resolve));
  const original = config.calcServer.baseUrl;
  config.calcServer.baseUrl = `http://127.0.0.1:${source.address().port}`;
  const app = express(); app.use(express.json());
  app.use('/shop', requireAdmin, require('../routes/admin/shop'));
  const server = app.listen(0, '127.0.0.1');
  await new Promise(resolve => server.once('listening', resolve));
  const base = `http://127.0.0.1:${server.address().port}/shop`;
  try {
    assert.equal((await fetch(base + '/offers')).status, 401);
    const ordinary = signToken({ user_id: 700000101 }, config.admin.jwtSecret, 60);
    assert.equal((await fetch(base + '/offers', { headers: { Authorization: `Bearer ${ordinary}` } })).status, 403);
    assert.equal(calls.length, 0);
    const token = signToken({ user_id: [...config.admin.userIds][0] }, config.admin.jwtSecret, 60);
    const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
    assert.equal((await fetch(base + '/offers', { headers })).status, 200);
    const body = { offer_id: 'grass_tile', item_id: 3011, unit_price_minor: 5,
      prevent_duplicate_purchase: true, enabled: true, expected_version: 1, reason: 'test price' };
    assert.equal((await fetch(base + '/offers/grass_tile', { method: 'PATCH', headers, body: JSON.stringify(body) })).status, 200);
    assert.deepEqual(calls[1], { path: '/admin/shop/offers/grass_tile', query: {}, token: `Bearer ${token}`, body });
    assert.equal((await fetch(base + '/orders?user_id=101&page=2&offer_id=grass_tile&from_time=2026-01-01T00%3A00%3A00Z', { headers })).status, 200);
    assert.deepEqual(calls[2].query, { user_id: '101', page: '2', offer_id: 'grass_tile', from_time: '2026-01-01T00:00:00Z' });
    const stale = await fetch(base + '/offers/grass_tile', { method: 'PATCH', headers, body: JSON.stringify({ reason: 'stale' }) });
    assert.equal(stale.status, 409);
    assert.equal((await stale.json()).message, '商品已被其他管理员修改');
    const count = calls.length;
    assert.equal((await fetch(base + '/offers/grass_tile', { method: 'DELETE', headers })).status, 405);
    assert.equal((await fetch(base + '/unrelated', { headers })).status, 404);
    assert.equal(calls.length, count);
  } finally {
    config.calcServer.baseUrl = original;
    await new Promise(resolve => server.close(resolve));
    await new Promise(resolve => source.close(resolve));
  }
});
