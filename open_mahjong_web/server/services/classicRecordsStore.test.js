const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const test = require('node:test');

const taskTmp = path.resolve(__dirname, '../../../.om_workspace/tests/classic-records');
fs.mkdirSync(taskTmp, { recursive: true });
const tmp = fs.mkdtempSync(path.join(taskTmp, 'catalog-'));
process.env.OM_DATA_DIR = tmp;

const store = require('../services/classicRecordsStore');

test('seeds the featured classic record once', () => {
  const items = store.listAll();
  assert.equal(items.some((item) => item.game_id === 'V9V0a25tKe' && item.description === '人和不够番'), true);
  const again = store.listAll();
  assert.equal(again.filter((item) => item.game_id === 'V9V0a25tKe').length, 1);
});

test('imports protagonist and allows changing it without retaining an old link player', () => {
  const created = store.createClassicRecord({ url: '/2d/record/PlayerTest?round=5&node=11&player=123&playerIndex=2', description: '主角' });
  assert.equal(created.protagonist_user_id, 123);
  assert.equal(created.protagonist_player_index, 2);
  const edited = store.updateClassicRecord(created.id, { protagonist_user_id: 456, protagonist_player_index: 0 });
  assert.match(edited.url_2d, /player=456&playerIndex=0$/);
  assert.match(edited.url_3d, /player=456&playerIndex=0$/);
  const indexOnly = store.updateClassicRecord(created.id, {protagonist_player_index: 3});
  assert.equal(indexOnly.protagonist_user_id, null);
  assert.equal(indexOnly.protagonist_player_index, 3);
  const replaced = store.updateClassicRecord(created.id, { url: '/game-unity?recordId=NewGame&player=789&playerIndex=1' });
  assert.equal(replaced.round, null);
  assert.equal(replaced.node, null);
  assert.equal(replaced.protagonist_user_id, 789);
  assert.equal(replaced.protagonist_player_index, 1);
  assert.throws(() => store.parseClassicInput({game_id: 'Test', description: 'Test', protagonist_player_index: 4}), /主角参数/);
});

test('backfills old catalog from node actor, persists it and rebuilds links with explicit overrides', async () => {
  const databasePath = require.resolve('../config/database');
  const previous = require.cache[databasePath];
  const record = {
    game_title: { p0_uid: 100, p1_uid: 101, p2_uid: 102, p3_uid: 103 },
    game_round: { r: { round_index: 5, seats: [1, 0, 3, 2], start_player_index: 0,
      action_ticks: [['reset', 0], ['c', 11], ['d', 12], ['c', 12], ['d', 13], ['c', 13], ['d', 14]] } },
  };
  require.cache[databasePath] = { exports: { query: async (sql, [ids]) => ({rows: sql.includes('game_player_records')
    ? ids.flatMap(game_id => [2, 0, 3, 1].map(index => ({game_id, user_id: 100 + index, original_player_index: index, username: `P${index}`, rank: 4 - index, rule: 'guobiao'})))
    : ids.map(game_id => ({game_id, record, created_at: '2026-01-01'})) }) } };
  try {
    const created = store.createClassicRecord({game_id: 'Backfill', round: 5, node: 7, description: '自动主角'});
    const [resolved] = await store.attachRecordMeta([created]);
    assert.equal(resolved.protagonist.username, 'P2');
    assert.equal(resolved.protagonist_user_id, 102);
    assert.equal(resolved.protagonist_player_index, 2);
    assert.match(resolved.url_3d, /player=102&playerIndex=2$/);
    assert.equal(store.listAll().find(item => item.id === created.id).protagonist_user_id, 102);
    const [manual] = await store.attachRecordMeta([{...created, protagonist_user_id: 100, protagonist_player_index: 0}], {strict: true, persist: false});
    assert.equal(manual.protagonist.username, 'P0');
    await assert.rejects(() => store.attachRecordMeta([{...created, protagonist_user_id: 999}], {strict: true, persist: false}), /主角必须/);
  } finally {
    if (previous) require.cache[databasePath] = previous;
    else delete require.cache[databasePath];
  }
});

test('creates, updates and deletes a classic record from a share URL', () => {
  const created = store.createClassicRecord({
    url: 'https://salasasa.cn/game-unity?recordId=AbCdEfGh12&round=2&node=0',
    description: '测试牌谱',
    sort: 1,
  });
  assert.equal(created.game_id, 'AbCdEfGh12');
  assert.equal(created.round, 2);
  assert.equal(created.node, 0);
  assert.match(created.url_2d, /\/2d\/record\/AbCdEfGh12\?round=2&node=0/);

  const updated = store.updateClassicRecord(created.id, {
    game_id: created.game_id,
    round: 3,
    node: 8,
    description: '改过的描述',
    sort: 20,
  });
  assert.equal(updated.round, 3);
  assert.equal(updated.description, '改过的描述');

  store.deleteClassicRecord(created.id);
  assert.equal(store.listAll().some((item) => item.id === created.id), false);
});
