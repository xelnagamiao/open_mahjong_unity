const test = require('node:test');
const assert = require('node:assert/strict');
const { createEventRoomBotService } = require('./eventRoomBots');

function fixture(response = { status: 200, data: { success: true } }) {
  const calls = [], audits = [];
  const service = createEventRoomBotService({
    proxyToGameServer: async (...args) => { calls.push(args); return response; },
    writeAudit: async row => audits.push(row),
  });
  return { service, calls, audits };
}
const event = { event_id: 'owned-event', status: 'active' };
const body = { instance_id: 'room-instance', seat_index: 2, bot_type: 'smart' };

test('event identity comes from membership, and the chosen seat and bot type reach the signed proxy', async () => {
  for (const bot_type of ['smart', 'auto', 'heuristic']) {
    const { service, calls, audits } = fixture();
    await service.add(event, 101, '12', { ...body, bot_type, event_id: 'another-event', user_id: 999 });
    assert.deepEqual(calls, [['/admin/rooms/12/bots', { ...body, bot_type, event_id: 'owned-event' }]]);
    assert.equal(audits[0].adminUserId, 101);
    assert.equal(audits[0].targetId, 'owned-event');
  }
});

test('invalid seats, bot types, stale form identities and inactive events never send a mutation', async () => {
  const { service, calls } = fixture();
  for (const patch of [{ seat_index: -1 }, { seat_index: 8 }, { seat_index: 1.5 }, { seat_index: true },
    { seat_index: '2' }, { bot_type: 'unknown' }, { instance_id: '' }, { instance_id: null }]) {
    await assert.rejects(service.add(event, 101, '12', { ...body, ...patch }), { status: 400 });
  }
  await assert.rejects(service.add({ ...event, status: 'closed' }, 101, '12', body), { status: 400 });
  await assert.rejects(service.add(event, 101, '12', body, { platformAdmin: true }), { status: 400 });
  assert.equal(calls.length, 0);
});

test('platform administration retains its reason and audit actor', async () => {
  const { service, audits } = fixture();
  await service.add(event, 102, '12', { ...body, reason: '测试对局' }, { platformAdmin: true });
  assert.equal(audits[0].reason, '测试对局');
  assert.equal(audits[0].action, 'event.room.add_bot');
});

test('an occupied seat or changed room response never records success', async () => {
  const { service, audits } = fixture({ status: 400, data: { detail: '房间已变化，请刷新后重试' } });
  await assert.rejects(service.add(event, 101, '12', body), { status: 400, message: '房间已变化，请刷新后重试' });
  assert.deepEqual(audits, []);
});
