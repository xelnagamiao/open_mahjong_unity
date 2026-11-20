const test = require('node:test');
const assert = require('node:assert/strict');
// Query fixtures exercise the public boundary without opening a database.
const pool = { query: async () => ({ rows: [] }) };
const database = require.resolve('../config/database');
require.cache[database] = { id: database, filename: database, loaded: true, exports: pool };
const { resultRow } = require('./testFixtures/replay');
const { getPublicGameRecord } = require('./publicGameRecord');

test('山西公开牌谱保留锁听、暗扣弃牌、杠摸与规则书分值', async () => {
  const record = {
    game_title: { rule: 'shanxi', sub_rule: 'shanxi/mil2023', round_time: 20, step_time: 5 },
    game_round: { round_index_1: { action_ticks: [
      ['c', 16, 'F', 'C'], ['state', 'ready', 0, 'public', 'T'],
      ['state', 'concealed_discard', 0, 0], ['gd', 19],
      ['hu_self', 0, 49, ['豪华七对', '和牌张（9分）'], [147, -49, -49, -49]],
    ] } },
  };
  const players = [0,1,2,3].map(i => ({ user_id: i+100, username: String(i), rank: i+1,
    original_player_index: i, score: i ? -49 : 147 }));
  const db = { query: async () => ({ rows: [resultRow('ShanxiTest1', record, players)] }) };
  const result = await getPublicGameRecord('ShanxiTest1', db);
  assert.equal(result.status, 200);
  assert.equal(result.data.rule, record.game_title.rule);
  assert.equal(result.data.sub_rule, record.game_title.sub_rule);
  assert.deepEqual(result.data.record.tables.main.payload, record);
  assert.deepEqual(result.data.players.map(p => p.score), [147,-49,-49,-49]);
});
