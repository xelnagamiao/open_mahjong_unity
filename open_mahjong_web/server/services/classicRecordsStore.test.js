const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const test = require('node:test');

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'om-fun-'));
process.env.OM_DATA_DIR = tmp;

const store = require('../services/classicRecordsStore');

test('seeds the featured classic record once', () => {
  const items = store.listAll();
  assert.equal(items.some((item) => item.game_id === 'V9V0a25tKe' && item.description === '人和不够番'), true);
  const again = store.listAll();
  assert.equal(again.filter((item) => item.game_id === 'V9V0a25tKe').length, 1);
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
