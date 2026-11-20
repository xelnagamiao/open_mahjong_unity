const test = require('node:test');
const assert = require('node:assert/strict');
// This contract uses a query spy and never opens the shared development database.
const pool = { query: async () => ({ rows: [] }) };
require.cache[require.resolve('../config/database')] = { id: require.resolve('../config/database'), filename: require.resolve('../config/database'), loaded: true, exports: pool };
const { resultRow } = require('./testFixtures/replay');
const { getPublicGameRecord } = require('./publicGameRecord');

test('公开牌谱读取保留红中规则、版本、实体鸟与替代解释', async () => {
  const record = { game_title: { rule: 'hongzhong', sub_rule: 'hongzhong/mil2024', detailed_config: { rule_version: 'mil-hongzhong-2024-om1' } }, game_round: { round_index_1: { action_ticks: [['hongzhong', 'birds', { tiles: [45, 15], hits: 2, detail: { joker_substitutions: [[45, 11]] } }]] } } };
  const players = [0,1,2,3].map(i => ({ user_id: i+100, username: String(i), rank: i+1,
    original_player_index: i, score: i ? -49 : 147 }));
  const db = { query: async () => ({ rows: [resultRow('HongzhongTest1', record, players)] }) };
  const result = await getPublicGameRecord('HongzhongTest1', db);
  assert.equal(result.status, 200);
  assert.equal(result.data.rule, record.game_title.rule);
  assert.equal(result.data.sub_rule, record.game_title.sub_rule);
  assert.deepEqual(result.data.record.tables.main.payload, record);
  assert.deepEqual(result.data.players.map(p => p.score), [147,-49,-49,-49]);
});


test('红中玩家记录筛选保持参数化，统计数来自通用记录索引', async () => {
  const { fetchPlayerInfo, buildRecordFilters, parseRecordQuery } = require('./playerPublicApi');
  const mock = test.mock.method(pool, 'query', async sql => {
    if (sql.includes('FROM user_settings')) return { rows: [{ user_id: 101, username: '红中用户' }] };
    if (sql.includes('AS rule,COUNT(*)::int AS total_games FROM game_player_records')) return { rows: [{ rule: 'hongzhong', total_games: 3 }, { rule: 'guobiao', total_games: 5 }] };
    return { rows: [] };
  });
  try {
    const info = await fetchPlayerInfo(101);
    assert.deepEqual(info.record_counts, { hongzhong: 3, guobiao: 5 });
    assert.equal(info.hongzhong_stats, undefined);
    const params = [];
    const clauses = buildRecordFilters(101, parseRecordQuery({ rule: 'hongzhong', sub_rule: 'hongzhong/mil2024', room_type: 'custom' }), params);
    assert.deepEqual(params, [101, 'custom', 'hongzhong/mil2024']);
    const subruleFilter = clauses.find(clause => clause.endsWith(' = $3'));
    assert.ok(subruleFilter.includes('gpr.sub_rule'));
    assert.ok(subruleFilter.includes('game_title'), '按原始牌谱解析子规则，避免旧索引混入其他子规则');
    const query = mock.mock.calls.find(call => call.arguments[0].includes('AS rule,COUNT(*)::int AS total_games FROM game_player_records'));
    assert.deepEqual(query.arguments[1], [101]);
  } finally { mock.mock.restore(); }
});
