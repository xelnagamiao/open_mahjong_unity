const test = require('node:test');
const assert = require('node:assert/strict');
const { sourceText } = require('./replayTables');

test('original evidence retains source action positions, integer seeds and decimal precision', () => {
  const source = sourceText(`{
    "payload":{"game_title":{"seed":9223372036854775807,"duplicate_table":"open"},
      "game_round":{"r":{"action_ticks":[{"score":1.2300}],"action_times_ms":[20]}}},
    "source_omitted_title_fields":["duplicate_table"],
    "transport_actions":{"r":[{"node":1,"tick":{"ask":true},"time_ms":10}]},
    "source_round_fields":{"r":{"time_count":3,"time_tail":[40]}}
  }`);
  assert.match(source, /"seed":9223372036854775807/);
  assert.match(source, /"score":1\.2300/);
  assert.ok(!source.includes('duplicate_table'));
  assert.deepEqual(JSON.parse(source).game_round.r, {
    action_ticks: [{ask:true},{score:1.23}], action_times_ms: [10,20,40]
  });
});

test('absent source actions stay absent, and impossible prompt positions fail closed', () => {
  const table = {payload:{game_title:{},game_round:{r:{action_ticks:[]}}},
    source_round_fields:{r:{no_action_ticks:true}}};
  assert.deepEqual(JSON.parse(sourceText(JSON.stringify(table))).game_round.r, {});
  table.transport_actions = {r:[{node:2,tick:{}}]};
  assert.throws(() => sourceText(JSON.stringify(table)), /prompt position/);
});
