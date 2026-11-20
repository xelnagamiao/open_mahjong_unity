// NODE_PATH may point at a temporary installation of fake-indexeddb.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { spawnSync } = require('node:child_process');
const { IDBFactory } = require('fake-indexeddb');
const migration = require('./browser_migration');

function put(db, entries) { return new Promise((resolve, reject) => {
  const tx = db.transaction('records', 'readwrite');
  for (const [key, value] of Object.entries(entries)) tx.objectStore('records').put(new TextEncoder().encode(migration.canonical(value)).buffer, key);
  tx.oncomplete = resolve; tx.onabort = tx.onerror = () => reject(tx.error);
}); }
function open() { return new Promise((resolve, reject) => {
  const request = indexedDB.open('omu.localRecords', 1);
  request.onupgradeneeded = () => request.result.createObjectStore('records');
  request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error);
}); }
function get(db, key) { return new Promise((resolve, reject) => {
  const tx = db.transaction('records'); const request = tx.objectStore('records').get(key);
  request.onsuccess = () => resolve(request.result == null ? null : migration.parseLossless(new TextDecoder().decode(request.result)));
  request.onerror = () => reject(request.error);
}); }
test('lossless seed and canonical numbers agree with Python semantics', () => {
  const input = '{"seed":18446744073709551615,"zero":-0.0,"float":1.0,"small":1e-7,"text":"中文"}';
  assert.equal(migration.canonical(migration.parseLossless(input)), '{"float":1,"seed":18446744073709551615,"small":0.000000099999999999999995,"text":"中文","zero":0}');
});
test('Python Unity and browser share Unicode, float and integer hash vectors', async () => {
  const fixtures = migration.parseLossless(fs.readFileSync(path.resolve(__dirname, '../../../open_mahjong_unity/Assets/Tests/Fixtures/MatchCanonical.json'), 'utf8'));
  for (const item of fixtures) assert.equal(await migration.hash(item.value), item.sha256);
});
test('export -> Python migration -> atomic unified IndexedDB import and replay integrity', async t => {
  globalThis.indexedDB = new IDBFactory();
  const db = await open();
  const source = migration.parseLossless('{"game_id":"browserOld","record":{"game_title":{"rule":"guobiao","sub_rule":"guobiao/standard","seed":18446744073709551615},"game_round":{"round_index_1":{"action_ticks":[["reset",0],["end"]]}}},"players":[],"custom":{"keep":true}}');
  await put(db, { browserOld: source, __index__: [{ game_id: 'browserOld' }] });
  const before = migration.canonical(await get(db, 'browserOld'));
  const workspace = path.resolve(__dirname, '../../../.om_workspace');
  const scratch = process.env.OM_BROWSER_MIGRATION_TEST_DIR || (fs.existsSync(workspace) ? workspace : os.tmpdir());
  fs.mkdirSync(scratch, { recursive: true });
  const root = fs.mkdtempSync(path.join(scratch, 'browser-replay-test-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  fs.writeFileSync(path.join(root, 'export.json'), migration.canonical(await migration.exportLegacy(false)));
  const venv = path.resolve(__dirname, process.platform === 'win32' ? '../../.venv/Scripts/python.exe' : '../../.venv/bin/python');
  const python = process.env.OM_TEST_PYTHON || (fs.existsSync(venv) ? venv : 'python');
  const result = spawnSync(python, ['-B', '-m', 'server.records.migrate', 'apply', '--backend', 'browser',
    '--input', path.join(root, 'export.json'), '--output', path.join(root, 'import.json')],
    { cwd: path.resolve(__dirname, '../..'), encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  const text = fs.readFileSync(path.join(root, 'import.json'), 'utf8');
  await put(db, { newGame: {record: {changed: true}} });
  await assert.rejects(migration.importPackage(text), /Source changed/);
  assert.equal(migration.canonical(await get(db, 'browserOld')), before);
  await new Promise((resolve,reject) => {
    const tx = db.transaction('records','readwrite');
    const store = tx.objectStore('records');
    store.delete('newGame');
    store.put(new Blob([before]), 'browserOld');
    tx.oncomplete=resolve; tx.onerror=reject;
  });
  assert.equal(await migration.importPackage(text), 'applied');
  assert.equal(await migration.importPackage(text), 'already_unified');
  const detail = await get(db, 'browserOld');
  assert.equal(detail.record.tables.main.payload.game_title.seed, 18446744073709551615n);
  assert.deepEqual(detail.custom.keep, true);
  const locators = await get(db, '__record_aliases__');
  assert.equal(locators.browserOld.table_id, 'main');
  assert.equal(await get(db, '__match_v2_catalog__'), null);
  db.close();
});
