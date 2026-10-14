const test = require('node:test');
const assert = require('node:assert/strict');
const { createAdminRoomService } = require('./adminRooms');

function fixture(overrides = {}) {
  const calls = [], audits = [];
  const dependencies = {
    pool: { query: async () => ({ rows: [{ event_id: 'evt', status: 'active', kind: 'event', name: '测试赛事' }] }) },
    proxyToGameServer: async (path, body, method) => {
      calls.push({ path, body, method });
      return { status: 200, data: { success: true, room_id: String(calls.length), closed_room_ids: ['1'] } };
    },
    writeAudit: async row => audits.push(row),
    eventRoomCreation: { create: async (...args) => ({ forwarded: args }) },
    ...overrides,
  };
  return { service: createAdminRoomService(dependencies), calls, audits };
}

test('ordinary creation uses authenticated actor and audits every created room without passwords', async () => {
  const { service, calls, audits } = fixture();
  const result = await service.create(101, { room_rule: 'sichuan', room_count: 3, password: 'secret', created_by: 999, reason: '测试', room_config: { sub_rule: 'sichuan/doubles' } });
  assert.equal(result.created_count, 3);
  assert.ok(calls.every(call => call.body.created_by === 101 && call.path === '/admin/rooms/create'));
  assert.equal(audits.length, 3);
  assert.ok(audits.every(row => row.adminUserId === 101 && !JSON.stringify(row).includes('secret')));
});

test('venue creation reuses venue validation and creation service', async () => {
  const { service, calls } = fixture();
  const body = { event_id: 'evt', reason: '测试', room_count: 1 };
  const result = await service.create(101, body);
  assert.deepEqual(result.forwarded, [{ event_id: 'evt', status: 'active', kind: 'event', name: '测试赛事' }, 101, body, { platformAdmin: true }]);
  assert.equal(calls.length, 0);
});

test('partial creation reports exactly the rooms that exist and stops immediately', async () => {
  let requests = 0;
  const { service, audits } = fixture({ proxyToGameServer: async () => ++requests < 3
    ? { status: 200, data: { success: true, room_id: String(requests) } }
    : { status: 409, data: { detail: 'cannot create' } } });
  await assert.rejects(service.create(101, { room_rule: 'guobiao', room_count: 5, reason: '测试' }), error => {
    assert.equal(error.status, 409);
    assert.equal(error.data.created_count, 2);
    assert.equal(error.data.requested_count, 5);
    return true;
  });
  assert.equal(requests, 3);
  assert.equal(audits.length, 2);
});

test('closing carries the exact room instance and audits both paired room IDs', async () => {
  const { service, calls, audits } = fixture({ proxyToGameServer: async (path, body) => {
    calls.push({ path, body });
    return { status: 200, data: { success: true, closed_room_ids: ['1', '2'] } };
  } });
  await service.close(101, '1', { instance_id: 'first-instance', reason: '清理', created_by: 999 });
  assert.deepEqual(calls[0], { path: '/admin/rooms/1/close', body: { instance_id: 'first-instance' } });
  assert.deepEqual(audits[0].payload.closed_room_ids, ['1', '2']);
  assert.equal(audits[0].adminUserId, 101);
});

test('stale close failure never writes a success audit', async () => {
  const { service, audits } = fixture({ proxyToGameServer: async () => ({ status: 409, data: { detail: '房间已发生变化' } }) });
  await assert.rejects(service.close(101, '1', { instance_id: 'old', reason: '清理' }), { status: 409 });
  assert.deepEqual(audits, []);
});

test('invalid requests are rejected before creating or closing anything', async () => {
  const { service, calls } = fixture();
  for (const body of [{}, { reason: 'x', room_rule: 'sichuan', room_count: 999 }, { reason: 'x', room_rule: 'sichuan', room_config: [] }, { reason: 'x', room_rule: 'guobiao', auto_duplicate: true }]) {
    await assert.rejects(service.create(101, body), { status: 400 });
  }
  await assert.rejects(service.close(101, '1', { reason: 'x' }), { status: 400 });
  assert.equal(calls.length, 0);
});
