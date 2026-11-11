const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createHash } = require('node:crypto');
const express = require('express');
const { createIosUpdateStore, validateRelease, validateContent } = require('../services/iosUpdate');
const { createRouter } = require('./iosUpdate');

const hash = bytes => createHash('sha256').update(bytes).digest('hex');
async function fixture(t) {
  const parent = path.resolve(__dirname, '../../../.om_workspace/ios-readiness/test-artifacts');
  await fs.mkdir(parent, { recursive: true });
  const directory = await fs.mkdtemp(path.join(parent, 'api-'));
  const app = express();
  app.use('/api/client-updates/ios', createRouter(createIosUpdateStore(directory)));
  const server = app.listen(0, '127.0.0.1');
  await new Promise(resolve => server.once('listening', resolve));
  t.after(() => new Promise(resolve => server.close(resolve)));
  const origin = `http://127.0.0.1:${server.address().port}`;
  const base = `${origin}/api/client-updates/ios`;
  const ipa = Buffer.from('PK\x03\x04iOS test package'), bundle = Buffer.from('UnityFS iOS content fixture');
  const release = { schemaVersion: 1, platform: 'ios', bundleId: 'com.Salasasa-Studio.open-mahjong-unity',
    releaseVersion: 34, versionName: '0.5.89', buildNumber: 1, channel: 'sideload',
    file: 'open-mahjong.ipa', sizeBytes: ipa.length, sha256: hash(ipa) };
  const content = { schemaVersion: 1, platform: 'ios', bundleId: release.bundleId,
    playerVersion: '0.5.89', releaseVersion: 34, contentVersion: 1, keys: ['ios/notice', 'localization/platformui.en'],
    file: `content-${hash(bundle)}.bundle`, sizeBytes: bundle.length, sha256: hash(bundle) };
  const write = (name, value) => fs.writeFile(path.join(directory, name), JSON.stringify(value));
  return { directory, origin, base, ipa, bundle, release, content, write,
    publishRelease: async () => { await fs.writeFile(path.join(directory, release.file), ipa); await write('ios-update.json', release); },
    publishContent: async () => { await fs.writeFile(path.join(directory, content.file), bundle); await write('ios-content.json', content); },
    checkContent: (version = '0.5.89', releaseVersion = 34, contentVersion = 0) =>
      fetch(`${base}/content?playerVersion=${version}&releaseVersion=${releaseVersion}&contentVersion=${contentVersion}`),
  };
}

test('absent or partially uploaded releases remain pending', async t => {
  const f = await fixture(t);
  assert.equal((await (await fetch(f.base + '/version')).json()).status, 'pending');
  assert.equal((await (await f.checkContent()).json()).status, 'pending');
  await f.write('ios-update.json', f.release);
  assert.equal((await (await fetch(f.base + '/version')).json()).status, 'pending');
});

test('sideload release advertises only verified same-origin bytes with ranges and HEAD', async t => {
  const f = await fixture(t); await f.publishRelease();
  const response = await fetch(f.base + '/version');
  assert.equal(response.headers.get('cache-control'), 'no-store');
  const manifest = await response.json();
  assert.equal(manifest.status, 'available'); assert.equal(manifest.file, undefined);
  assert.equal(manifest.installUrl, undefined);
  assert.equal(manifest.downloadPath, `/api/client-updates/ios/ipa/${f.release.sha256}`);
  let download = await fetch(f.origin + manifest.downloadPath);
  assert.deepEqual(Buffer.from(await download.arrayBuffer()), f.ipa);
  download = await fetch(f.origin + manifest.downloadPath, { headers: { Range: 'bytes=2-5' } });
  assert.equal(download.status, 206);
  assert.deepEqual(Buffer.from(await download.arrayBuffer()), f.ipa.subarray(2, 6));
  download = await fetch(f.origin + manifest.downloadPath, { method: 'HEAD' });
  assert.equal(download.headers.get('content-length'), String(f.ipa.length));
  assert.equal((await download.arrayBuffer()).byteLength, 0);
  assert.equal((await fetch(f.origin + manifest.downloadPath, { headers: { Range: 'bytes=999-' } })).status, 416);
});

test('modified same-size bytes invalidate a previously verified hash; old URLs never serve replacement', async t => {
  const f = await fixture(t); await f.publishRelease();
  assert.equal((await (await fetch(f.base + '/version')).json()).status, 'available');
  await fs.writeFile(path.join(f.directory, f.release.file), Buffer.alloc(f.ipa.length, 7));
  assert.equal((await (await fetch(f.base + '/version')).json()).status, 'pending');
  assert.equal((await fetch(f.base + '/ipa/' + f.release.sha256)).status, 404);
  await f.publishRelease();
  assert.equal((await (await fetch(f.base + '/version')).json()).status, 'available');
  assert.equal((await fetch(f.base + '/ipa/' + 'a'.repeat(64))).status, 404);
});

test('release version supplies the default build number; explicit store build numbers remain independent', async t => {
  const f = await fixture(t);
  const { buildNumber, ...withoutBuild } = f.release;
  assert.equal(validateRelease(withoutBuild).buildNumber, 34);
  await fs.writeFile(path.join(f.directory, f.release.file), f.ipa);
  await f.write('ios-update.json', withoutBuild);
  let release = await (await fetch(f.base + '/version')).json();
  assert.equal(release.status, 'available');
  assert.equal(release.releaseVersion, 34);
  assert.equal(release.buildNumber, 34);
  await f.write('ios-update.json', { ...withoutBuild, buildNumber: 101 });
  release = await (await fetch(f.base + '/version')).json();
  assert.equal(release.releaseVersion, 34);
  assert.equal(release.buildNumber, 101);
  for (const invalid of [null, 0, -1, 1.5, '34', true, NaN, Infinity, 2147483648])
    assert.throws(() => validateRelease({ ...withoutBuild, buildNumber: invalid }));
  for (const invalid of [undefined, null, 0, -1, '34'])
    assert.throws(() => validateRelease({ ...withoutBuild, releaseVersion: invalid, buildNumber: 101 }));
});

test('replacing an IPA keeps the same game release while advertising only the newly verified package', async t => {
  const f = await fixture(t); await f.publishRelease();
  const previous = await (await fetch(f.base + '/version')).json();
  const replacement = Buffer.from('PK\x03\x04rebuilt package for the same game release');
  const { buildNumber, ...withoutBuild } = f.release;
  const next = { ...withoutBuild, file: 'rebuilt.ipa', sizeBytes: replacement.length, sha256: hash(replacement) };
  await fs.writeFile(path.join(f.directory, next.file), replacement);
  await f.write('ios-update.json', next);
  const release = await (await fetch(f.base + '/version')).json();
  assert.equal(release.status, 'available');
  assert.equal(release.releaseVersion, previous.releaseVersion);
  assert.equal(release.versionName, previous.versionName);
  assert.equal(release.buildNumber, previous.releaseVersion);
  assert.notEqual(release.downloadPath, previous.downloadPath);
  assert.deepEqual(Buffer.from(await (await fetch(f.origin + release.downloadPath)).arrayBuffer()), replacement);
  assert.equal((await fetch(f.origin + previous.downloadPath)).status, 404);
});

test('resource packages require exact player and protocol compatibility', async t => {
  const f = await fixture(t); await f.publishContent();
  const content = await (await f.checkContent()).json();
  assert.equal(content.status, 'available');
  assert.equal(content.file, `content-${content.sha256}.bundle`);
  assert.equal(content.bundleId, f.release.bundleId);
  assert.equal(content.playerVersion, '0.5.89');
  assert.deepEqual(Buffer.from(await (await fetch(f.origin + content.downloadPath)).arrayBuffer()), f.bundle);
  assert.equal((await (await f.checkContent('0.5.88')).json()).status, 'player_required');
  assert.equal((await (await f.checkContent('0.5.89', 33)).json()).status, 'player_required');
  assert.equal((await (await f.checkContent('0.5.89', 34, 1)).json()).status, 'current');
  assert.equal((await (await f.checkContent('0.5.89', 34, 2)).json()).status, 'current');
  assert.equal((await f.checkContent('0.5.89.0')).status, 400);
  assert.equal((await f.checkContent('0.5.89', 34, -1)).status, 400);
});

test('content tampering and overlarge metadata stay pending', async t => {
  const f = await fixture(t); await f.publishContent();
  await fs.writeFile(path.join(f.directory, f.content.file), Buffer.alloc(f.bundle.length, 1));
  assert.equal((await (await f.checkContent()).json()).status, 'pending');
  await fs.writeFile(path.join(f.directory, 'ios-update.json'), ' '.repeat(32769));
  assert.equal((await (await fetch(f.base + '/version')).json()).status, 'pending');
});

test('external install channels use only their Apple hosts and cannot redirect to arbitrary websites', async t => {
  const f = await fixture(t);
  for (const [channel, installUrl] of [['testflight', 'https://testflight.apple.com/join/AbC123'],
    ['appstore', 'https://apps.apple.com/cn/app/test/id12345678']]) {
    await f.write('ios-update.json', { ...f.release, channel, installUrl });
    const result = await (await fetch(f.base + '/version')).json();
    assert.equal(result.status, 'available'); assert.equal(result.installUrl, installUrl);
    assert.equal(result.downloadPath, undefined);
    assert.equal((await fetch(f.base + '/ipa/' + f.release.sha256)).status, 404);
  }
  for (const installUrl of ['http://testflight.apple.com/join/ABC', 'https://testflight.apple.com.evil.test/join/ABC',
    'https://name:pass@testflight.apple.com/join/ABC', 'https://testflight.apple.com/join/ABC?redirect=evil'])
    assert.throws(() => validateRelease({ ...f.release, channel: 'testflight', installUrl }));
});

test('manifest validators reject traversal, invalid versions and resource keys', async t => {
  const f = await fixture(t);
  for (const file of ['../outside.ipa', 'nested/file.ipa', 'a\\b.ipa', 'app.exe'])
    assert.throws(() => validateRelease({ ...f.release, file }));
  for (const patch of [{ versionName: '0.5.89.0' }, { buildNumber: 0 }, { channel: 'enterprise' }, { platform: 'android' }])
    assert.throws(() => validateRelease({ ...f.release, ...patch }));
  for (const keys of [['../file'], ['/root'], ['one', 'one'], ['Uppercase'], ['key\u0000'], ['key\u0085'], []])
    assert.throws(() => validateContent({ ...f.content, keys }));
  assert.deepEqual(validateContent({ ...f.content, keys: ['localization/platformui.en', 'backgrounds/table (blue)'] }).keys,
    ['localization/platformui.en', 'backgrounds/table (blue)']);
  assert.throws(() => validateContent({ ...f.content, sizeBytes: 256 * 1024 * 1024 + 1 }));
  assert.throws(() => validateContent({ ...f.content, file: 'wrong.bundle' }));
});
