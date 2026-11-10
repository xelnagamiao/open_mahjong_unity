const test = require('node:test');
const assert = require('node:assert/strict');
const { createEventDuplicateWallService } = require('./eventDuplicateWalls');

const event = { event_id: 'event-a', kind: 'event', status: 'active' };
const body = { rule: 'sichuan/sbr', wall_type: 'key', round_count: 8, use_flowers: false };
function fixture(error) {
  const calls = [], audits = [], db = {};
  const wall = { id: '12', key: 'dup_fixed', rule: body.rule, round_count: 8 };
  const quota = { remaining_today: 24, remaining_storage: 99 };
  const service = createEventDuplicateWallService({
    duplicateWalls: { async createBatch(userId, input, count, options) {
      calls.push({ userId, input, count, platformAdmin: options.platformAdmin });
      if (error) throw error;
      await options.beforeCommit({ walls: [wall], quota }, db);
      return { walls: [wall], quota };
    } },
    writeAudit: async (audit, transaction) => { assert.equal(transaction, db); audits.push(audit); },
  });
  return { service, calls, audits, wall, quota };
}

test('random keys use only the authenticated event scope and audit within creation transaction', async () => {
  for (const kind of ['event', 'base']) {
    const { service, calls, audits, wall, quota } = fixture();
    const result = await service.create({ ...event, kind }, 101, { ...body, event_id: 'foreign', scope: 'personal', user_id: 999, seed: 'ignored', key: 'spoofed' });
    assert.deepEqual(result, { wall, quota });
    assert.deepEqual(calls, [{ userId: 101, input: { ...body, scope: 'event', event_id: event.event_id, name: '' }, count: 1, platformAdmin: false }]);
    assert.equal(audits[0].adminUserId, 101);
    assert.equal(audits[0].targetId, event.event_id);
    assert.equal(audits[0].action, 'event_admin.duplicate_wall.create');
    assert.equal(audits[0].payload.wall_id, wall.id);
    assert.equal(audits[0].payload.key, undefined);
  }
});

test('platform creation requires a reason and uses the platform authorization path', async () => {
  const { service, calls, audits } = fixture();
  await assert.rejects(service.create(event, 201, body, { platformAdmin: true }), { status: 400 });
  assert.equal(calls.length, 0);
  await service.create(event, 201, { ...body, rule: 'sichuan/xueliu', reason: ' 管理员建房 ' }, { platformAdmin: true });
  assert.equal(calls[0].platformAdmin, true);
  assert.equal(calls[0].input.rule, 'sichuan/xueliu');
  assert.equal(audits[0].reason, '管理员建房');
  assert.equal(audits[0].action, 'event.duplicate_wall.create');
});

test('invalid sources, rules, rounds, flowers and closed venues never consume a key', async () => {
  const { service, calls } = fixture();
  for (const patch of [{ wall_type: 'seed' }, { wall_type: 'manual' }, { rule: 'guobiao' }, { rule: 'sichuan/doubles_duplicate' }, { round_count: 2 }, { round_count: '8' }, { use_flowers: true }]) {
    await assert.rejects(service.create(event, 101, { ...body, ...patch }), { status: 400 });
  }
  await assert.rejects(service.create(null, 101, body), { status: 404 });
  await assert.rejects(service.create({ ...event, kind: 'hall' }, 101, body), { status: 400 });
  await assert.rejects(service.create({ ...event, status: 'closed' }, 101, body), { status: 409 });
  assert.equal(calls.length, 0);
});

test('membership and quota failures preserve their actionable API errors without a success audit', async () => {
  for (const status of [403, 409, 429]) {
    const error = Object.assign(new Error('permission or quota failure'), { status });
    const { service, audits } = fixture(error);
    await assert.rejects(service.create(event, 101, body), error);
    assert.deepEqual(audits, []);
  }
});
