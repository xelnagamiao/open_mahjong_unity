const test = require('node:test');
const assert = require('node:assert/strict');
// This contract uses a query spy and never opens the shared development database.
const pool = { query: async () => ({ rows: [] }) };
require.cache[require.resolve('../config/database')] = { id: require.resolve('../config/database'), filename: require.resolve('../config/database'), loaded: true, exports: pool };
const { getPublicGameRecord, getPublicUnityGameRecord } = require('./publicGameRecord');

test('公开牌谱读取保留红中规则、版本、实体鸟与替代解释', async () => {
  const record = { game_title: { rule: 'hongzhong', sub_rule: 'hongzhong/mil2024', detailed_config: { rule_version: 'mil-hongzhong-2024-om1' } }, game_round: { round_index_1: { action_ticks: [['hongzhong', 'birds', { tiles: [45, 15], hits: 2, detail: { joker_substitutions: [[45, 11]] } }]] } } };
  const mock = test.mock.method(pool, 'query', async (sql) => {
    if (sql.startsWith('SELECT 1 FROM game_records')) return { rowCount: 0, rows: [] };
    if (sql.startsWith('SELECT game_id, record')) return { rowCount: 1, rows: [{ game_id: 'HongzhongTest1', record, created_at: '2026-10-02' }] };
    return { rowCount: 4, rows: [0, 1, 2, 3].map(i => ({ rule: 'hongzhong', sub_rule: 'hongzhong/mil2024', user_id: String(i+100), username: String(i), score: '0', rank: i+1, original_player_index: i })) };
  });
  try {
    for (const fetch of [getPublicGameRecord, getPublicUnityGameRecord]) {
      const result = await fetch('HongzhongTest1');
      assert.equal(result.status, 200);
      assert.equal(result.data.rule, 'hongzhong');
      assert.equal(result.data.sub_rule, 'hongzhong/mil2024');
      assert.deepEqual(result.data.record, record);
      assert.equal(result.data.players.length, 4);
    }
  } finally { mock.mock.restore(); }
});

test('红中公开牌谱仍执行格式、复式锁和未支持规则检查', async () => {
  let locked = true;
  const mock = test.mock.method(pool, 'query', async sql => {
    if (sql.startsWith('SELECT 1 FROM game_records')) return { rows: locked ? [{}] : [] };
    if (sql.startsWith('SELECT game_id, record')) return { rowCount: 1, rows: [{ game_id: 'RecordTest', record: { game_title: { rule: 'unknown' } } }] };
    return { rows: [] };
  });
  try {
    assert.equal((await getPublicGameRecord('../invalid')).status, 400);
    assert.equal((await getPublicGameRecord('RecordTest')).status, 403);
    locked = false;
    assert.equal((await getPublicGameRecord('RecordTest')).status, 400);
    assert.equal((await getPublicUnityGameRecord('RecordTest')).status, 200);
  } finally { mock.mock.restore(); }
});

test('红中玩家记录筛选保持参数化，统计数来自通用记录索引', async () => {
  const { fetchPlayerInfo, buildRecordFilters, parseRecordQuery } = require('./playerPublicApi');
  const mock = test.mock.method(pool, 'query', async sql => {
    if (sql.includes('FROM user_settings')) return { rows: [{ user_id: 101, username: '红中用户' }] };
    if (sql.startsWith('SELECT rule, COUNT(*)')) return { rows: [{ rule: 'hongzhong', total_games: 3 }, { rule: 'guobiao', total_games: 5 }] };
    return { rows: [] };
  });
  try {
    const info = await fetchPlayerInfo(101);
    assert.deepEqual(info.record_counts, { hongzhong: 3, guobiao: 5 });
    assert.equal(info.hongzhong_stats, undefined);
    const params = [];
    const clauses = buildRecordFilters(101, parseRecordQuery({ rule: 'hongzhong', sub_rule: 'hongzhong/mil2024', room_type: 'custom' }), params);
    assert.deepEqual(params, [101, 'custom', 'hongzhong', 'hongzhong/mil2024']);
    assert.ok(clauses.includes('gpr.rule = $3'));
    assert.ok(clauses.includes('gpr.sub_rule = $4'));
    const query = mock.mock.calls.find(call => call.arguments[0].startsWith('SELECT rule, COUNT(*)'));
    assert.deepEqual(query.arguments[1], [101]);
  } finally { mock.mock.restore(); }
});
