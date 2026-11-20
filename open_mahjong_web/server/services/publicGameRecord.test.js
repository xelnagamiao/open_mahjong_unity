const test = require('node:test');
const assert = require('node:assert/strict');
const { getPublicGameRecord } = require('./publicGameRecord');
const fixture = require('../../../open_mahjong_unity/Assets/Tests/Fixtures/MatchRecordV2.json').archive;

function database(allowed, selected = 'open', record = fixture, participants = []) {
  const calls = [];
  return { calls, async query(sql, args) {
    calls.push({ sql, args });
    return { rows: [{ id: args[0][0], game_id: 'P123', table_id: selected, allowed,
      body: typeof record === 'string' ? record : JSON.stringify(record), participants }] };
  } };
}
test('legacy table link opens same aggregate and preserves selected table and cursor', async () => {
  const db = database(['idopen', 'idclosed'], 'closed');
  const result = await getPublicGameRecord('idclosed', db);
  assert.equal(result.status, 200);
  assert.equal(result.data.selected_table_id, 'closed');
  assert.equal(result.data.record.match_id, fixture.match_id);
  assert.deepEqual(result.data.legacy_cursor_map.node_map.round_index_1, fixture.tables.closed.round_meta.round_index_1.legacy_node_map);
  assert.ok(db.calls[0].sql.includes('sichuan_duplicate_matches'), 'per-table authoritative access predicate is reused');
});
function independentVisibleTable() {
  const body = structuredClone(fixture);
  const payload = body.tables.open.payload;
  for (const key of ['duplicate_boards','doubles_result','duplicate_match']) delete payload[key];
  for (const key of ['duplicate_tiles','duplicate_seed','duplicate_round_tiles','doubles_result']) delete payload.game_title[key];
  return body;
}
test('blocked sibling and top-level shared material are removed without changing the saved game', async () => {
  const result = await getPublicGameRecord('idopen', database(['idopen'], 'open', independentVisibleTable()));
  assert.equal(result.data.record.tables.closed.payload, null);
  assert.equal(result.data.record.completeness, 'partial');
  assert.equal(result.data.record.board_catalog[0].wall_definition, undefined);
  assert.ok(fixture.tables.closed.payload);
});
test('blocked selected legacy link fails', async () => {
  assert.equal((await getPublicGameRecord('idclosed', database(['idopen'], 'closed'))).status, 403);
});
test('unrecognized IDs are missing and malformed IDs are rejected', async () => {
  const result = await getPublicGameRecord('old', { query: async () => ({ rows: [] }) });
  assert.equal(result.status, 404);
  assert.equal((await getPublicGameRecord('../bad', database([]))).status, 400);
});
test('64-bit seeds survive the JSONB text and HTTP JSON boundary', async () => {
  const record = JSON.stringify(fixture).replace('"game_title":{', '"game_title":{"seed":18446744073709551615,');
  const result = await getPublicGameRecord('idopen', database(['idopen', 'idclosed'], 'open', record));
  assert.match(JSON.stringify(result.data), /"seed":18446744073709551615/);
});
test('display a renamed player on their table and preserve the recorded title and bot seats', async () => {
  const body = structuredClone(fixture);
  body.tables.open.players = [
    { user_id: 112, original_player_index: 0, username: 'before', note: 'private', is_favorite: true },
    { user_id: 1, original_player_index: 1, username: 'recorded bot' },
  ];
  body.tables.open.payload.game_title.p0_name = 'before';
  const roster = [
    { table_id: 'open', user_id: 112, username: 'after' },
    { table_id: 'open', user_id: 1, username: 'other bot' },
  ];
  const result = await getPublicGameRecord('idopen', database(['idopen', 'idclosed'], 'open', body, roster));
  assert.deepEqual(result.data.record.tables.open.players.map(p => p.username), ['after', 'recorded bot']);
  for (const seat of [result.data.players[0], result.data.record.tables.open.players[0]]) {
    assert.equal(Object.hasOwn(seat, 'note'), false);
    assert.equal(Object.hasOwn(seat, 'is_favorite'), false);
  }
  assert.equal(result.data.record.tables.open.payload.game_title.p0_name, 'before');
});
test('redaction removes forbidden table roster from the match wrapper', async () => {
  const record = independentVisibleTable();
  record.teams = [{team_id:0,members:[{table_id:'open',user_id:1},{table_id:'closed',user_id:2}]}];
  const result = await getPublicGameRecord('idopen', database(['idopen'], 'open', record));
  assert.deepEqual(result.data.record.teams[0].members, [{table_id:'open',user_id:1}]);
});

test('mixed permissions reject an otherwise visible table containing the sibling result', async () => {
  const record = independentVisibleTable();
  record.tables.open.payload.duplicate_match = { scores: [123, -123] };
  const result = await getPublicGameRecord('idopen', database(['idopen'], 'open', record));
  assert.equal(result.status, 403);
  assert.equal(result.data, undefined);
});
