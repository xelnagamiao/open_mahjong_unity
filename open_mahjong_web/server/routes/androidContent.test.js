const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const express = require('express');
const { createRouter } = require('./androidContent');
const { createContentStore, signedText } = require('../services/androidContent');
const { createAndroidUpdateStore } = require('../services/androidUpdate');

async function fixture(t) {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'omu-content-'));
  const directory = path.join(root, 'hotupdate', '1', '33'); await fs.mkdir(directory, { recursive: true });
  const bytes = Buffer.from('PK\x03\x04verified-content-fixture');
  const sha256 = crypto.createHash('sha256').update(bytes).digest('hex');
  const m = { schemaVersion: 1, baseVersion: 1, releaseVersion: 33, baseline: 'b'.repeat(64),
    sha256, sizeBytes: bytes.length, unpackedBytes: 100, file: `content-${sha256}.zip`, signature: 'A'.repeat(512) };
  let target = { baseVersion: 1, releaseVersion: 33 };
  await fs.writeFile(path.join(directory, m.file), bytes);
  await fs.writeFile(path.join(directory, 'content-update.json'), JSON.stringify(m));
  const app = express();
  app.use('/api/client-updates/android', createRouter(createContentStore(root), async () => target, createAndroidUpdateStore(root)));
  const server = app.listen(0, '127.0.0.1'); await new Promise(r => server.once('listening', r));
  const origin = `http://127.0.0.1:${server.address().port}/api/client-updates/android`;
  t.after(async () => { await new Promise(r => server.close(r)); await fs.rm(root, { recursive: true, force: true }); });
  return { root, directory, origin, m, bytes, setTarget: v => { target = v; }, check: (base = 1, release = 32) => fetch(`${origin}/content?baseVersion=${base}&releaseVersion=${release}`) };
}
test('same framework updates content, matching content skips downloading', async t => {
  const f = await fixture(t);
  const response = await f.check(); assert.equal(response.headers.get('cache-control'), 'no-store');
  const m = await response.json(); assert.equal(m.status, 'available'); assert.equal(m.sha256, f.m.sha256);
  assert.equal((await (await f.check(1, 33)).json()).status, 'current');
});
test('base mismatch requires APK; newer clients never auto-downgrade', async t => {
  const f = await fixture(t);
  f.setTarget({ baseVersion: 2, releaseVersion: 33 });
  assert.equal((await (await f.check()).json()).status, 'apk_required');
  f.setTarget({ baseVersion: 1, releaseVersion: 31 });
  assert.equal((await (await f.check()).json()).status, 'client_ahead');
  assert.equal((await (await f.check(2)).json()).status, 'client_ahead');
});
test('missing, partial, and corrupt uploads are pending, never offered', async t => {
  const f = await fixture(t), file = path.join(f.directory, f.m.file);
  await fs.writeFile(file, f.bytes.subarray(0, 5)); assert.equal((await (await f.check()).json()).status, 'pending');
  await fs.writeFile(file, Buffer.alloc(f.bytes.length)); assert.equal((await (await f.check()).json()).status, 'pending');
  await fs.unlink(file); assert.equal((await (await f.check()).json()).status, 'pending');
});
test('content downloads are exact, resumable, immutable and reject invalid ranges', async t => {
  const f = await fixture(t); const m = await (await f.check()).json();
  const url = new URL(m.downloadPath, f.origin);
  const full = await fetch(url); assert.deepEqual(Buffer.from(await full.arrayBuffer()), f.bytes);
  assert.match(full.headers.get('cache-control'), /immutable/);
  const partial = await fetch(url, { headers: { Range: 'bytes=5-' } }); assert.equal(partial.status, 206);
  assert.deepEqual(Buffer.from(await partial.arrayBuffer()), f.bytes.subarray(5));
  assert.equal((await fetch(url, { headers: { Range: 'bytes=9999-' } })).status, 416);
  assert.equal((await fetch(new URL('/api/client-updates/android/content/1/33/' + 'c'.repeat(64), f.origin))).status, 404);
});
test('old published bytes remain downloadable after target moves forward', async t => {
  const f = await fixture(t); const m = await (await f.check()).json();
  f.setTarget({ baseVersion: 1, releaseVersion: 34 });
  assert.equal((await (await f.check()).json()).status, 'pending');
  const old = await fetch(new URL(m.downloadPath, f.origin)); assert.equal(old.status, 200);
  assert.deepEqual(Buffer.from(await old.arrayBuffer()), f.bytes);
});
test('framework lookup accepts older bundled content and still enforces APK versionCode', async t => {
  const f = await fixture(t); const bytes = Buffer.from('APK test fixture');
  const apk = { schemaVersion: 1, versionName: '1.0', versionCode: 40, baseVersion: 1, releaseVersion: 32,
    packageName: 'cn.salasasa.mahjong', file: 'game.apk', sizeBytes: bytes.length, sha256: crypto.createHash('sha256').update(bytes).digest('hex') };
  await fs.writeFile(path.join(f.root, 'game.apk'), bytes); await fs.writeFile(path.join(f.root, 'android-update.json'), JSON.stringify(apk));
  assert.equal((await (await fetch(f.origin + '/framework?installedVersionCode=39')).json()).status, 'available');
  assert.equal((await (await fetch(f.origin + '/framework?installedVersionCode=40')).json()).status, 'pending');
  f.setTarget({ baseVersion: 2, releaseVersion: 33 });
  assert.equal((await (await fetch(f.origin + '/framework?installedVersionCode=39')).json()).status, 'pending');
});
test('invalid parameters and manifests are rejected', async t => {
  const f = await fixture(t); assert.equal((await f.check(-1)).status, 400);
  await fs.writeFile(path.join(f.directory, 'content-update.json'), JSON.stringify({ ...f.m, file: '../private.zip' }));
  assert.equal((await (await f.check()).json()).status, 'pending');
});
test('signing format matches the C# version manifest', () => {
  assert.equal(signedText({ schemaVersion: 1, baseVersion: 2, releaseVersion: 33, baseline: 'b', file: 'c.zip', sizeBytes: 20, unpackedBytes: 30, sha256: 'a' }), '1\n2\n33\nb\nc.zip\n20\n30\na');
});
