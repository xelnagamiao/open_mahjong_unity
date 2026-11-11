const test = require('node:test');
const assert = require('node:assert/strict');

const pool = { query: async () => ({ rows: [] }) };
require.cache[require.resolve('../../config/database')] = { exports: pool };
require.cache[require.resolve('../../config/config')] = { exports: { calcServer: { baseUrl: 'http://isolated-game-server.invalid', timeoutMs: 1000 } } };
require.cache[require.resolve('../../utils/audit')] = { exports: { writeAudit: async () => {} } };
const router = require('./game_control');
const list = router.stack.find(layer => layer.route?.path === '/list').route.stack[0].handle;
async function invoke() {
  const res = { statusCode: 200, status(code) { this.statusCode = code; return this; }, json(body) { this.body = body; return this; } };
  await list({}, res);
  return res;
}

test('game list adds current venue names and kinds in one deduplicated lookup', async t => {
  const items = [{ gamestate_id: 'one', event_id: 'event-a' }, { gamestate_id: 'two', event_id: 'base-b' }, { gamestate_id: 'three', event_id: 'event-a' }, { gamestate_id: 'lobby' }];
  t.mock.method(global, 'fetch', async () => ({ status: 200, text: async () => JSON.stringify({ items, total: 4 }) }));
  let lookups = 0;
  t.mock.method(pool, 'query', async (sql, values) => {
    lookups++;
    assert.match(sql, /event_id = ANY/);
    assert.deepEqual(values, [['event-a', 'base-b']]);
    return { rows: [{ event_id: 'event-a', name: '赛事', kind: 'event' }, { event_id: 'base-b', name: '基地', kind: 'base' }] };
  });
  const res = await invoke();
  assert.equal(res.statusCode, 200);
  assert.equal(lookups, 1);
  assert.equal(res.body.data.total, 4);
  assert.deepEqual(res.body.data.items.map(game => game.venue_kind), ['event', 'base', 'event', '']);
  assert.equal(res.body.data.items[1].event_name, '基地');
  assert.equal(items[0].venue_kind, undefined);
});

test('lobby-only lists skip the venue database query', async t => {
  t.mock.method(global, 'fetch', async () => ({ status: 200, text: async () => JSON.stringify({ items: [{ gamestate_id: 'lobby' }] }) }));
  t.mock.method(pool, 'query', async () => { throw Error('No lookup expected'); });
  const res = await invoke();
  assert.equal(res.statusCode, 200);
  assert.equal(res.body.data.items[0].venue_kind, '');
});

test('upstream failures remain errors rather than an empty successful list', async t => {
  t.mock.method(global, 'fetch', async () => ({ status: 503, text: async () => JSON.stringify({ detail: '游戏服维护中' }) }));
  const res = await invoke();
  assert.equal(res.statusCode, 503);
  assert.equal(res.body.message, '游戏服维护中');
});
