const fs = require('node:fs/promises');
const path = require('node:path');
const { createHash } = require('node:crypto');

const HASH = /^[a-f0-9]{64}$/;
const VERSION = /^\d{1,9}\.\d{1,9}\.\d{1,9}$/;
const BUNDLE = /^[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+$/;
const integer = value => Number.isSafeInteger(value) && value > 0 && value <= 2147483647;
const resourceKey = value => typeof value === 'string' && value.trim().length > 0 && value.length <= 240
  && value === value.toLowerCase() && !value.startsWith('/') && !value.includes('..')
  && !/[\\:\x00-\x1f\x7f-\x9f]/.test(value);

function releaseDirectory() {
  return process.env.IOS_UPDATE_DIR ? path.resolve(process.env.IOS_UPDATE_DIR)
    : path.resolve(__dirname, '../../client', process.env.NODE_ENV === 'production' ? 'dist' : 'public', 'ios-game');
}

function common(value) {
  if (!value || value.schemaVersion !== 1 || value.platform !== 'ios'
    || typeof value.bundleId !== 'string' || value.bundleId.length > 255 || !BUNDLE.test(value.bundleId)
    || !integer(value.releaseVersion)) throw new Error('Invalid iOS manifest');
  return { schemaVersion: 1, platform: 'ios', bundleId: value.bundleId, releaseVersion: value.releaseVersion };
}

function artifact(value, maxBytes, extension) {
  if (!Number.isSafeInteger(value.sizeBytes) || value.sizeBytes < 1 || value.sizeBytes > maxBytes
    || typeof value.sha256 !== 'string' || !HASH.test(value.sha256)
    || typeof value.file !== 'string' || !new RegExp(`^[A-Za-z0-9][A-Za-z0-9._-]*\\.${extension}$`).test(value.file))
    throw new Error('Invalid artifact');
  return { file: value.file, sizeBytes: value.sizeBytes, sha256: value.sha256 };
}

function validateRelease(value) {
  const result = common(value);
  const buildNumber = value.buildNumber === undefined ? value.releaseVersion : value.buildNumber;
  if (typeof value.versionName !== 'string' || !VERSION.test(value.versionName)
    || !integer(buildNumber) || !['sideload', 'testflight', 'appstore'].includes(value.channel))
    throw new Error('Invalid iOS release');
  Object.assign(result, { versionName: value.versionName, buildNumber, channel: value.channel,
    notes: typeof value.notes === 'string' ? value.notes.slice(0, 4000) : '' });
  if (value.channel === 'sideload') Object.assign(result, artifact(value, 2 * 1024 * 1024 * 1024, 'ipa'));
  else {
    const url = new URL(value.installUrl);
    if (url.protocol !== 'https:' || url.username || url.password || url.port || url.hash
      || (value.channel === 'testflight'
        ? url.hostname !== 'testflight.apple.com' || !/^\/join\/[A-Za-z0-9]+\/?$/.test(url.pathname) || url.search
        : url.hostname !== 'apps.apple.com' || !/\/id\d+\/?$/.test(url.pathname)))
      throw new Error('Invalid installation URL');
    result.installUrl = url.href;
  }
  return result;
}

function validateContent(value) {
  const result = common(value);
  if (typeof value.playerVersion !== 'string' || !VERSION.test(value.playerVersion)
    || !integer(value.contentVersion) || !Array.isArray(value.keys)
    || value.keys.length < 1 || value.keys.length > 1024 || !value.keys.every(resourceKey)
    || new Set(value.keys).size !== value.keys.length) throw new Error('Invalid iOS content');
  Object.assign(result, artifact(value, 256 * 1024 * 1024, 'bundle'));
  if (value.file !== `content-${value.sha256}.bundle`) throw new Error('Wrong content filename');
  return Object.assign(result, { playerVersion: value.playerVersion, contentVersion: value.contentVersion, keys: [...value.keys] });
}

function createIosUpdateStore(directory = releaseDirectory) {
  const verified = new Map();
  const fingerprint = stat => `${stat.size}:${stat.mtimeMs}:${stat.ctimeMs}:${stat.ino}`;
  async function openManifest(name, validate, expectedHash) {
    const root = path.resolve(typeof directory === 'function' ? directory() : directory);
    const realRoot = await fs.realpath(root);
    const manifestPath = path.join(root, name);
    if (path.dirname(await fs.realpath(manifestPath)) !== realRoot) throw new Error('Invalid manifest location');
    const metadata = await fs.open(manifestPath, 'r');
    let manifest;
    try {
      const stat = await metadata.stat();
      if (!stat.isFile() || stat.size < 2 || stat.size > 32768) throw new Error('Invalid manifest size');
      const bytes = Buffer.alloc(stat.size);
      const { bytesRead } = await metadata.read(bytes, 0, bytes.length, 0);
      if (bytesRead !== bytes.length || fingerprint(await metadata.stat()) !== fingerprint(stat)) throw new Error('Manifest changed');
      manifest = validate(JSON.parse(bytes.toString('utf8')));
    } finally { await metadata.close(); }
    if (!manifest.file) {
      if (expectedHash) throw new Error('No local package');
      return { manifest, handle: null };
    }
    if (expectedHash && manifest.sha256 !== expectedHash) throw new Error('Artifact changed');
    const filename = path.join(root, manifest.file);
    if (path.dirname(await fs.realpath(filename)) !== realRoot) throw new Error('Invalid artifact location');
    const handle = await fs.open(filename, 'r');
    try {
      const stat = await handle.stat();
      if (!stat.isFile() || stat.size !== manifest.sizeBytes) throw new Error('Incomplete artifact');
      const key = `${filename}:${manifest.sha256}:${fingerprint(stat)}`;
      if (!verified.has(key)) {
        const hash = createHash('sha256');
        for await (const chunk of handle.createReadStream({ autoClose: false, start: 0 })) hash.update(chunk);
        if (hash.digest('hex') !== manifest.sha256 || fingerprint(await handle.stat()) !== fingerprint(stat))
          throw new Error('Artifact checksum mismatch');
        if (verified.size >= 16) verified.clear();
        verified.set(key, true);
      }
      // The same open handle is used for verification and download; replacing a
      // release path cannot swap different bytes into an in-flight response.
      return { manifest, handle };
    } catch (error) { await handle.close(); throw error; }
  }
  return {
    openRelease: hash => openManifest('ios-update.json', validateRelease, hash),
    openContent: hash => openManifest('ios-content.json', validateContent, hash),
  };
}

module.exports = { createIosUpdateStore, releaseDirectory, validateRelease, validateContent, VERSION, HASH, integer };
