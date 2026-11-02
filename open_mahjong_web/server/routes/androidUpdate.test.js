const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { createHash } = require('node:crypto');
const express = require('express');
const { createAndroidUpdateStore } = require('../services/androidUpdate');
const { createRouter } = require('./androidUpdate');

async function fixture(t) {
  const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'android-update-'));
  const store = createAndroidUpdateStore(directory);
  const app = express();
  app.use('/api/client-updates/android', createRouter(store));
  const server = app.listen(0, '127.0.0.1');
  await new Promise(resolve => server.once('listening', resolve));
  t.after(async () => {
    await new Promise(resolve => server.close(resolve));
    await fs.rm(directory, { recursive: true, force: true });
  });
  const origin = `http://127.0.0.1:${server.address().port}`;
  const bytes = Buffer.from('PK\x03\x04Android update test fixture');
  const manifest = {
    schemaVersion: 1, versionName: '0.4.84.0', versionCode: 31, releaseVersion: 31,
    packageName: 'cn.salasasa.mahjong', file: 'open_mahjong_unity.apk', sizeBytes: bytes.length,
    sha256: createHash('sha256').update(bytes).digest('hex'), notes: '测试更新',
  };
  const writeManifest = async value => fs.writeFile(path.join(directory, 'android-update.json'), JSON.stringify(value));
  const publish = async () => {
    await fs.writeFile(path.join(directory, manifest.file), bytes);
    await writeManifest(manifest);
  };
  const check = (required = 31, installed = 30) => fetch(`${origin}/api/client-updates/android?requiredReleaseVersion=${required}&installedVersionCode=${installed}`);
  return { directory, origin, manifest, bytes, check, publish, writeManifest };
}

test('available release downloads the exact verified bytes without caching the manifest', async t => {
  const f = await fixture(t); await f.publish();
  const response = await f.check();
  assert.equal(response.headers.get('cache-control'), 'no-store');
  const manifest = await response.json();
  assert.equal(manifest.status, 'available');
  assert.equal(manifest.releaseVersion, 31);
  assert.equal(manifest.versionCode, 31);
  assert.equal(manifest.file, undefined);
  const apk = await fetch(f.origin + manifest.downloadPath);
  assert.equal(apk.status, 200);
  assert.equal(apk.headers.get('content-type'), 'application/vnd.android.package-archive');
  assert.deepEqual(Buffer.from(await apk.arrayBuffer()), f.bytes);
});

test('platform updated before either APK or sidecar is uploaded stays pending', async t => {
  const f = await fixture(t);
  assert.equal((await (await f.check()).json()).status, 'pending');
  await f.writeManifest(f.manifest);
  assert.equal((await (await f.check()).json()).status, 'pending');
});

test('old compatible-release metadata and future-release APK are never offered', async t => {
  const f = await fixture(t); await f.publish();
  for (const required of [30, 32]) assert.equal((await (await f.check(required)).json()).status, 'pending');
});

test('same or older Android versionCode never triggers a downgrade/reinstall loop', async t => {
  const f = await fixture(t); await f.publish();
  for (const installed of [31, 32]) assert.equal((await (await f.check(31, installed)).json()).status, 'pending');
});

test('partial upload becomes available on retry without restarting the server', async t => {
  const f = await fixture(t);
  await f.writeManifest(f.manifest);
  await fs.writeFile(path.join(f.directory, f.manifest.file), f.bytes.subarray(0, 4));
  assert.equal((await (await f.check()).json()).status, 'pending');
  await fs.writeFile(path.join(f.directory, f.manifest.file), f.bytes);
  assert.equal((await (await f.check()).json()).status, 'available');
});

test('same-size damaged APK is rejected and a repaired upload becomes available', async t => {
  const f = await fixture(t); await f.publish();
  assert.equal((await (await f.check()).json()).status, 'available');
  await fs.writeFile(path.join(f.directory, f.manifest.file), Buffer.alloc(f.bytes.length, 9));
  assert.equal((await (await f.check()).json()).status, 'pending');
  const download = await fetch(`${f.origin}/api/client-updates/android/apk/${f.manifest.sha256}`);
  assert.equal(download.status, 409);
  await fs.writeFile(path.join(f.directory, f.manifest.file), f.bytes);
  assert.equal((await (await f.check()).json()).status, 'available');
});

test('uploading new APK before its manifest prevents mixed-version downloads', async t => {
  const f = await fixture(t); await f.publish();
  await fs.writeFile(path.join(f.directory, f.manifest.file), Buffer.alloc(f.bytes.length + 1));
  assert.equal((await (await f.check()).json()).status, 'pending');
});

test('a stale hash URL cannot silently download a different release', async t => {
  const f = await fixture(t); await f.publish();
  const old = (await (await f.check()).json()).downloadPath;
  const bytes = Buffer.from('new APK');
  await fs.writeFile(path.join(f.directory, f.manifest.file), bytes);
  await f.writeManifest({ ...f.manifest, sizeBytes: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') });
  assert.equal((await fetch(f.origin + old)).status, 409);
  assert.equal((await (await f.check()).json()).status, 'available');
});

test('missing version information, malformed queries and array parameters fail safely', async t => {
  const f = await fixture(t); await f.publish();
  assert.equal((await (await f.check(0)).json()).status, 'pending');
  for (const required of ['-1', 'NaN', '1.5', '2147483648', '31&requiredReleaseVersion=32']) {
    assert.equal((await f.check(required)).status, 400);
  }
  assert.equal((await fetch(f.origin + '/api/client-updates/android')).status, 400);
});

test('malformed, oversized and path-traversing manifests never expose files', async t => {
  const f = await fixture(t); await f.publish();
  for (const change of [
    { file: '../secret.apk' }, { file: '/secret.apk' }, { file: 'a\\secret.apk' },
    { sizeBytes: 0 }, { sizeBytes: 3 * 1024 ** 3 }, { sha256: 'bad' },
    { versionCode: '31' }, { packageName: '<b>fake</b>' }, { schemaVersion: 2 },
  ]) {
    await f.writeManifest({ ...f.manifest, ...change });
    assert.equal((await (await f.check()).json()).status, 'pending');
  }
  await fs.writeFile(path.join(f.directory, 'android-update.json'), '{ broken');
  assert.equal((await (await f.check()).json()).status, 'pending');
  await f.writeManifest({ ...f.manifest, notes: 'x'.repeat(40000) });
  assert.equal((await (await f.check()).json()).status, 'pending');
});

test('concurrent checks share verification and each download starts at byte zero', async t => {
  const f = await fixture(t); await f.publish();
  const manifests = await Promise.all(Array.from({ length: 12 }, async () => (await f.check()).json()));
  assert.ok(manifests.every(m => m.status === 'available'));
  const downloads = await Promise.all(manifests.map(async m => Buffer.from(await (await fetch(f.origin + m.downloadPath)).arrayBuffer())));
  downloads.forEach(bytes => assert.deepEqual(bytes, f.bytes));
});

test('HEAD reports package length, invalid hashes and absent packages do not serve HTML', async t => {
  const f = await fixture(t); await f.publish();
  const url = `${f.origin}/api/client-updates/android/apk/${f.manifest.sha256}`;
  const head = await fetch(url, { method: 'HEAD' });
  assert.equal(head.status, 200);
  assert.equal(Number(head.headers.get('content-length')), f.bytes.length);
  assert.equal((await fetch(url.replace(f.manifest.sha256, 'invalid'))).status, 404);
  await fs.unlink(path.join(f.directory, f.manifest.file));
  assert.equal((await fetch(url)).status, 409);
});

test('web metadata stays uncached while the verified hash APK permits shared caching', async t => {
  const f = await fixture(t); await f.publish();
  const latest = await fetch(f.origin + '/api/client-updates/android/latest?request=first');
  assert.equal(latest.headers.get('cache-control'), 'no-store');
  const metadata = await latest.json();
  assert.equal(metadata.status, 'available');
  assert.equal(metadata.file, undefined);
  assert.equal(metadata.sha256, f.manifest.sha256);
  const redirect = await fetch(f.origin + '/api/client-updates/android/download', { redirect: 'manual' });
  assert.equal(redirect.status, 302);
  assert.equal(redirect.headers.get('cache-control'), 'no-store');
  assert.equal(redirect.headers.get('location'), metadata.downloadPath);
  const apk = await fetch(f.origin + metadata.downloadPath);
  assert.equal(apk.headers.get('cache-control'), 'public, max-age=0, s-maxage=86400, must-revalidate');
  assert.equal(apk.headers.get('content-disposition'), 'attachment; filename="open_mahjong_unity-0.4.84.0-31.apk"');
  assert.deepEqual(Buffer.from(await apk.arrayBuffer()), f.bytes);
});

test('web downloads follow a new publication automatically without restarting the same server', async t => {
  const f = await fixture(t); await f.publish();
  const first = await (await fetch(f.origin + '/api/client-updates/android/latest')).json();
  const bytes = Buffer.from('PK\x03\x04a later published APK');
  await fs.writeFile(path.join(f.directory, f.manifest.file), bytes);
  const pending = await fetch(f.origin + '/api/client-updates/android/download', { redirect: 'manual' });
  assert.equal(pending.status, 503);
  assert.equal(pending.headers.get('location'), null);
  await f.writeManifest({ ...f.manifest, versionName: '0.6.0.0', versionCode: 32, releaseVersion: 32,
    sizeBytes: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') });
  const second = await (await fetch(f.origin + '/api/client-updates/android/latest')).json();
  assert.equal(second.versionCode, 32);
  assert.equal(second.versionName, '0.6.0.0');
  assert.notEqual(second.downloadPath, first.downloadPath);
  const redirect = await fetch(f.origin + '/api/client-updates/android/download', { redirect: 'manual' });
  assert.equal(redirect.headers.get('location'), second.downloadPath);
  const apk = await fetch(f.origin + second.downloadPath);
  assert.match(apk.headers.get('content-disposition'), /0\.6\.0\.0-32\.apk/);
  assert.deepEqual(Buffer.from(await apk.arrayBuffer()), bytes);
  assert.equal((await fetch(f.origin + first.downloadPath)).status, 409);
  // The new browser convenience endpoint must not relax native compatibility rules.
  assert.equal((await (await f.check(31, 30)).json()).status, 'pending');
});

test('missing or corrupt releases never redirect web downloads to the legacy static file', async t => {
  const f = await fixture(t);
  for (const corrupt of [false, true]) {
    if (corrupt) {
      await f.publish();
      await fs.writeFile(path.join(f.directory, f.manifest.file), Buffer.alloc(f.bytes.length, 1));
    }
    const latest = await fetch(f.origin + '/api/client-updates/android/latest');
    assert.equal(latest.headers.get('cache-control'), 'no-store');
    assert.equal((await latest.json()).status, 'pending');
    const redirect = await fetch(f.origin + '/api/client-updates/android/download', { redirect: 'manual' });
    assert.equal(redirect.status, 503);
    assert.equal(redirect.headers.get('cache-control'), 'no-store');
    assert.equal(redirect.headers.get('location'), null);
  }
});


test('byte ranges resume exact verified bytes and are cacheable only on success', async t => {
  const f = await fixture(t); await f.publish();
  const url = `${f.origin}/api/client-updates/android/apk/${f.manifest.sha256}`;
  const etag = `"sha256-${f.manifest.sha256}"`;
  for (const [range, start, end] of [
    ['bytes=0-3', 0, 3], ['bytes=4-', 4, f.bytes.length - 1],
    ['bytes=-5', f.bytes.length - 5, f.bytes.length - 1],
    ['bytes=0-9999', 0, f.bytes.length - 1],
  ]) {
    const r = await fetch(url, { headers: { Range: range, 'If-Range': etag }, redirect: 'manual' });
    assert.equal(r.status, 206);
    assert.equal(r.headers.get('content-range'), `bytes ${start}-${end}/${f.bytes.length}`);
    assert.equal(Number(r.headers.get('content-length')), end - start + 1);
    assert.equal(r.headers.get('etag'), etag);
    assert.equal(r.headers.get('accept-ranges'), 'bytes');
    assert.match(r.headers.get('cache-control'), /s-maxage=86400/);
    assert.deepEqual(Buffer.from(await r.arrayBuffer()), f.bytes.subarray(start, end + 1));
  }
  for (const range of ['bytes=9999-', 'bytes=-0']) {
    const r = await fetch(url, { headers: { Range: range } });
    assert.equal(r.status, 416);
    assert.equal(r.headers.get('content-range'), `bytes */${f.bytes.length}`);
    assert.equal(r.headers.get('cache-control'), 'no-store');
    assert.equal((await r.arrayBuffer()).byteLength, 0);
  }
  for (const headers of [
    { Range: 'bytes=4-', 'If-Range': '"different-file"' },
    { Range: 'bytes=0-1,4-5' }, { Range: 'items=0-1' }, { Range: 'invalid' },
  ]) {
    const r = await fetch(url, { headers });
    assert.equal(r.status, 200);
    assert.equal(r.headers.get('content-range'), null);
    assert.deepEqual(Buffer.from(await r.arrayBuffer()), f.bytes);
  }
  const head = await fetch(url, { method: 'HEAD', headers: { Range: 'bytes=0-3' } });
  assert.equal(head.status, 200);
  assert.equal(Number(head.headers.get('content-length')), f.bytes.length);
  assert.equal((await head.arrayBuffer()).byteLength, 0);
  await fs.writeFile(path.join(f.directory, f.manifest.file), Buffer.alloc(f.bytes.length, 0));
  const corrupt = await fetch(url, { headers: { Range: 'bytes=0-3' } });
  assert.equal(corrupt.status, 409);
  assert.equal(corrupt.headers.get('cache-control'), 'no-store');
});

test('HEAD closes the verified descriptor without reading or streaming the APK', async t => {
  let closed = 0;
  let streamed = 0;
  const f = await fixture(t); await f.publish();
  const app = express();
  app.use(createRouter({ openRelease: async () => ({ manifest: f.manifest, handle: {
    close: async () => { closed += 1; },
    createReadStream: () => { streamed += 1; throw new Error('HEAD must not stream'); },
  } }) }));
  const server = app.listen(0, '127.0.0.1');
  await new Promise(resolve => server.once('listening', resolve));
  t.after(() => new Promise(resolve => server.close(resolve)));
  const r = await fetch(`http://127.0.0.1:${server.address().port}/apk/${f.manifest.sha256}`, { method: 'HEAD' });
  assert.equal(r.status, 200);
  assert.equal(closed, 1);
  assert.equal(streamed, 0);
});
