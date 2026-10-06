const test = require('node:test');
const assert = require('node:assert/strict');
const { ROOM_COUNTS, createEventRoomCreationService } = require('./eventRoomCreation');

const event = { event_id: 'evt_A', kind: 'event', status: 'active' };
function fixture({ failAt, quotaError, failAudit } = {}) {
  const calls = [], audits = [], batches = [];
  const service = createEventRoomCreationService({
    duplicateWalls: {
      async createBatch(userId, body, count, options) {
        batches.push({ userId, body, count, options });
        if (quotaError) throw Object.assign(new Error('剩余密钥额度不足'), { status: 429 });
        return { walls: Array.from({ length: count }, (_, i) => ({ key: `key_${i}` })), quota: { daily_limit: 25 } };
      },
    },
    async proxyToGameServer(path, body) {
      calls.push({ path, body });
      if (calls.length === failAt) return { status: 400, data: { detail: '游戏服拒绝创建' } };
      return { status: 200, data: { success: true, room_info: { room_id: String(calls.length) } } };
    },
    async writeAudit(audit) { if (failAudit) throw new Error('audit unavailable'); audits.push(audit); },
  });
  return { service, calls, audits, batches };
}

test('every room count creates that many rooms with the same settings, password and audit', async () => {
  assert.deepEqual(ROOM_COUNTS, [1, 3, 5, 8, 10, 20]);
  for (const room_count of ROOM_COUNTS) {
    const { service, calls, audits, batches } = fixture();
    const body = { room_count, room_rule: 'riichi', room_config: { starting_score: 30000 }, password: 'test', reason: '测试' };
    const result = await service.create(event, 10, body, { platformAdmin: true });
    assert.equal(result.created_count, room_count);
    assert.equal(result.items.length, room_count);
    assert.equal(result.generated_key_count, 0);
    assert.equal(batches.length, 0);
    assert.equal(audits.length, room_count);
    assert.ok(audits.every(audit => audit.action === 'event.room.create' && audit.reason === '测试'));
    assert.ok(calls.every(call => call.body.password === 'test' && call.body.room_config.starting_score === 30000));
  }
  const { service } = fixture();
  const single = await service.create(event, 10, { room_rule: 'guobiao' });
  assert.equal(single.room_info.room_id, '1');
});

test('automatic duplicate rooms each bind a different event key with the selected rounds and flowers', async () => {
  for (const sub_rule of ['guobiao/standard', 'guobiao/lanshi']) {
    const { service, calls, batches, audits } = fixture();
    const body = { room_count: 3, room_rule: 'guobiao', room_config: { sub_rule, use_flowers: false }, duplicate_enabled: true, auto_duplicate: true, duplicate_round_count: 12 };
    const original = structuredClone(body);
    const result = await service.create(event, 10, body);
    assert.equal(result.generated_key_count, 3);
    assert.equal(result.created_count, 3);
    assert.deepEqual(calls.map(call => call.body.room_config.duplicate_key), ['key_0', 'key_1', 'key_2']);
    assert.deepEqual(batches[0].body, { scope: 'event', event_id: 'evt_A', wall_type: 'key', rule: sub_rule === 'guobiao/lanshi' ? sub_rule : 'guobiao', name: '', round_count: 12, use_flowers: false });
    assert.ok(audits.every(audit => audit.action === 'event_admin.room.create'));
    assert.deepEqual(body, original);
  }
});

test('insufficient quota creates no rooms; invalid automatic options create no keys or rooms', async () => {
  const automatic = { room_count: 3, room_rule: 'guobiao', duplicate_enabled: true, auto_duplicate: true };
  const blocked = fixture({ quotaError: true });
  await assert.rejects(blocked.service.create(event, 10, automatic), { status: 429 });
  assert.equal(blocked.calls.length, 0);
  for (const body of [
    { ...automatic, room_count: 2 }, { ...automatic, room_count: '3' },
    { ...automatic, auto_duplicate: 'true' }, { ...automatic, duplicate_round_count: 3 },
    { ...automatic, duplicate_enabled: false }, { ...automatic, duplicate_enabled: undefined },
    { ...automatic, duplicate_enabled: 'true' },
    { ...automatic, room_rule: 'riichi' }, { ...automatic, room_config: { duplicate_key: 'existing' } },
    { ...automatic, room_config: { random_seed: 42 } },
    ...['guobiao/sanma', 'guobiao/blood_battle'].map(sub_rule => ({ ...automatic, room_config: { sub_rule } })),
  ]) {
    const { service, calls, batches } = fixture();
    await assert.rejects(service.create(event, 10, body), { status: 400 });
    assert.equal(calls.length + batches.length, 0);
  }
  const { service, batches } = fixture();
  await assert.rejects(service.create({ ...event, kind: 'base' }, 10, automatic), { status: 400 });
  await assert.rejects(service.create({ ...event, status: 'closed' }, 10, automatic), { status: 400 });
  await assert.rejects(service.create(event, 10, automatic, { platformAdmin: true }), { status: 400 });
  assert.equal(batches.length, 0);
});

test('duplicate mode validates manual keys; ordinary East-West rooms do not create keys', async () => {
  for (const body of [
    { room_rule: 'guobiao', duplicate_enabled: true },
    { room_rule: 'riichi', duplicate_enabled: true, room_config: { duplicate_key: 'existing' } },
    { room_rule: 'guobiao', duplicate_enabled: false, room_config: { duplicate_key: 'existing' } },
  ]) {
    const { service, calls, batches } = fixture();
    await assert.rejects(service.create(event, 10, body), { status: 400 });
    assert.equal(calls.length + batches.length, 0);
  }
  const ordinary = fixture();
  const result = await ordinary.service.create(event, 10, { room_count: 3, room_rule: 'guobiao', duplicate_enabled: false, room_config: { game_round: 3 } });
  assert.equal(result.created_count, 3);
  assert.equal(ordinary.batches.length, 0);
  assert.ok(ordinary.calls.every(call => call.body.room_config.game_round === 3 && !call.body.room_config.duplicate_key));
  for (const duplicate_enabled of [true, undefined]) {
    const manual = fixture();
    await manual.service.create(event, 10, { room_rule: 'guobiao', duplicate_enabled, room_config: { duplicate_key: 'existing' } });
    assert.equal(manual.batches.length, 0);
    assert.equal(manual.calls[0].body.room_config.duplicate_key, 'existing');
  }
});

test('partial room and audit failures report committed rooms and retained keys, then stop', async () => {
  const { service, calls, audits } = fixture({ failAt: 2 });
  await assert.rejects(service.create(event, 10, { room_count: 5, room_rule: 'guobiao', duplicate_enabled: true, auto_duplicate: true }), error => {
    assert.equal(error.status, 400);
    assert.equal(error.data.created_count, 1);
    assert.equal(error.data.generated_key_count, 5);
    assert.equal(error.data.requested_count, 5);
    return true;
  });
  assert.equal(calls.length, 2);
  assert.equal(audits.length, 1);
  const failedAudit = fixture({ failAudit: true });
  await assert.rejects(failedAudit.service.create(event, 10, { room_count: 3, room_rule: 'guobiao' }), error => {
    assert.equal(error.data.created_count, 1);
    return true;
  });
  assert.equal(failedAudit.calls.length, 1);
});
