const test = require('node:test');
const assert = require('node:assert/strict');
// Query fixtures exercise the public boundary without opening a database.
const pool = { query: async () => ({ rows: [] }) };
const database = require.resolve('../config/database');
require.cache[database] = { id: database, filename: database, loaded: true, exports: pool };
const { getPublicGameRecord, getPublicUnityGameRecord } = require('./publicGameRecord');

test('山西公开牌谱保留锁听、暗扣弃牌、杠摸与规则书分值', async () => {
  const record = {
    game_title: { rule: 'shanxi', sub_rule: 'shanxi/mil2023', round_time: 20, step_time: 5 },
    game_round: { round_index_1: { action_ticks: [
      ['c', 16, 'F', 'C'], ['state', 'ready', 0, 'public', 'T'],
      ['state', 'concealed_discard', 0, 0], ['gd', 19],
      ['hu_self', 0, 49, ['豪华七对', '和牌张（9分）'], [147, -49, -49, -49]],
    ] } },
  };
  const mock = test.mock.method(pool, 'query', async sql => {
    if (sql.startsWith('SELECT 1 FROM game_records')) return { rows: [] };
    if (sql.startsWith('SELECT game_id, record')) return { rowCount: 1, rows: [{ game_id: 'ShanxiTest1', record: JSON.stringify(record), created_at: '2026-10-11' }] };
    return { rows: [0, 1, 2, 3].map(i => ({ rule: 'shanxi', sub_rule: 'shanxi/mil2023', user_id: String(i + 100), username: String(i), score: String(i ? -49 : 147), rank: i + 1, original_player_index: i })) };
  });
  try {
    for (const fetch of [getPublicGameRecord, getPublicUnityGameRecord]) {
      const result = await fetch('ShanxiTest1');
      assert.equal(result.status, 200);
      assert.equal(result.data.rule, 'shanxi');
      assert.equal(result.data.sub_rule, 'shanxi/mil2023');
      assert.deepEqual(result.data.record, record);
      assert.deepEqual(result.data.players.map(p => p.score), [147, -49, -49, -49]);
    }
  } finally { mock.mock.restore(); }
});

test('山西牌谱入口仍校验编号与复式封锁，不放开未知规则', async () => {
  let locked = true;
  const mock = test.mock.method(pool, 'query', async sql => {
    if (sql.startsWith('SELECT 1 FROM game_records')) return { rows: locked ? [{}] : [] };
    if (sql.startsWith('SELECT game_id, record')) return { rowCount: 1, rows: [{ game_id: 'UnknownTest', record: { game_title: { rule: 'unknown' } } }] };
    return { rows: [] };
  });
  try {
    assert.equal((await getPublicGameRecord('../invalid')).status, 400);
    assert.equal((await getPublicGameRecord('UnknownTest')).status, 403);
    locked = false;
    assert.equal((await getPublicGameRecord('UnknownTest')).status, 400);
  } finally { mock.mock.restore(); }
});
