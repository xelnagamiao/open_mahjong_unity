const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const express = require('express');

const testRoot = path.resolve(__dirname, '../../../../.om_workspace/tests/classic-records');
fs.mkdirSync(testRoot, { recursive: true });
process.env.OM_DATA_DIR = fs.mkdtempSync(path.join(testRoot, 'api-'));
const players = [0, 1, 2, 3].map(index => ({game_id: 'ApiReplay', user_id: 100 + index,
  original_player_index: index, username: `P${index}`, rank: index + 1, rule: 'guobiao'}));
const record = {game_title: {p0_uid: 100, p1_uid: 101, p2_uid: 102, p3_uid: 103},
  game_round: {r: {round_index: 2, seats: [1, 0, 3, 2], start_player_index: 0, action_ticks: [['bd', 11, 3]]}}};
require.cache[require.resolve('../../config/database')] = {exports: {query: async sql => ({rowCount: 1,
  rows: sql.includes('game_player_records') ? players : [{game_id: 'ApiReplay', record, created_at: '2026-01-01'}]})}};
require.cache[require.resolve('../../utils/audit')] = {exports: {writeAudit: async () => {}}};
const store = require('../../services/classicRecordsStore');
const router = require('./classicRecords');

test('admin resolves, saves, edits and rejects an unrelated protagonist through the API', async () => {
  const app = express();
  app.use(express.json(), (req, _res, next) => {req.admin = {userId: 1}; next();}, router);
  const server = app.listen(0, '127.0.0.1');
  await new Promise(resolve => server.once('listening', resolve));
  const origin = `http://127.0.0.1:${server.address().port}`;
  async function send(method, url, body) {
    const response = await fetch(origin + url, {method, headers: {'content-type': 'application/json'}, body: JSON.stringify(body)});
    return {status: response.status, ...(await response.json())};
  }
  try {
    const preview = await send('POST', '/resolve', {url: '/2d/record/ApiReplay?round=2&node=1'});
    assert.equal(preview.status, 200);
    assert.equal(preview.data.protagonist.username, 'P2');
    assert.equal(store.listAll().some(item => item.game_id === 'ApiReplay'), false);
    const created = await send('POST', '/', {url: '/game-unity?recordId=ApiReplay&round=2&node=1&player=103&playerIndex=3', description: 'API test'});
    assert.equal(created.status, 200);
    assert.equal(created.data.protagonist.username, 'P3');
    const updated = await send('PUT', '/' + created.data.id, {protagonist_player_index: 0});
    assert.equal(updated.status, 200);
    assert.equal(updated.data.protagonist.username, 'P0');
    assert.match(updated.data.url_2d, /round=2&node=1&player=100&playerIndex=0$/);
    assert.match(updated.data.url_3d, /round=2&node=1&player=100&playerIndex=0$/);
    const invalid = await send('PUT', '/' + created.data.id, {protagonist_user_id: 999});
    assert.equal(invalid.status, 400);
    assert.equal(store.listAll().find(item => item.id === created.data.id).protagonist_user_id, 100);
    const legacy = await send('PUT', '/' + created.data.id, {url: '/2d/record/ApiReplay?round=2&node=1'});
    assert.equal(legacy.data.protagonist.username, 'P2');
  } finally {
    await new Promise(resolve => server.close(resolve));
  }
});
