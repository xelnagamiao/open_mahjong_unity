const assert = require('node:assert/strict');
const test = require('node:test');

const { rewriteRecordSnapshot } = require('./gameRecordUsername');

test('rewrites player objects and game_title names for the same uid', () => {
  const record = {
    game_title: { p0_uid: 10000001, p0_name: '旧名', p1_uid: 10000002, p1_name: '对手' },
    players: [{ user_id: 10000001, username: '旧名' }, { userId: 10000002, username: '对手' }],
  };
  assert.equal(rewriteRecordSnapshot(record, 10000001, { newUsername: '新名' }), true);
  assert.equal(record.game_title.p0_name, '新名');
  assert.equal(record.game_title.p1_name, '对手');
  assert.equal(record.players[0].username, '新名');
  assert.equal(record.players[1].username, '对手');
});

test('oldUsername filter only rewrites matching snapshots', () => {
  const record = {
    game_title: { p0_uid: 7, p0_name: '甲' },
    nested: { user_id: 7, username: '乙' },
  };
  assert.equal(rewriteRecordSnapshot(record, 7, { oldUsername: '甲', newUsername: '丙' }), true);
  assert.equal(record.game_title.p0_name, '丙');
  assert.equal(record.nested.username, '乙');
});

test('returns false when nothing changes', () => {
  const record = { game_title: { p0_uid: 1, p0_name: '同名' }, user_id: 1, username: '同名' };
  assert.equal(rewriteRecordSnapshot(record, 1, { newUsername: '同名' }), false);
});
