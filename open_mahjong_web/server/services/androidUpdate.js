const fs = require('node:fs/promises');
const path = require('node:path');
const { createHash } = require('node:crypto');

const MANIFEST_NAME = 'android-update.json';
const MAX_APK_BYTES = 2 * 1024 * 1024 * 1024;

function releaseDirectory() {
  if (process.env.ANDROID_UPDATE_DIR) return path.resolve(process.env.ANDROID_UPDATE_DIR);
  return path.resolve(__dirname, '../../client', process.env.NODE_ENV === 'production' ? 'dist' : 'public', 'android-game');
}

function validateManifest(value) {
  if (!value || value.schemaVersion !== 1
      || !Number.isSafeInteger(value.versionCode) || value.versionCode < 1 || value.versionCode > 2147483647
      || !Number.isSafeInteger(value.releaseVersion) || value.releaseVersion < 1
      || !Number.isSafeInteger(value.sizeBytes) || value.sizeBytes < 1 || value.sizeBytes > MAX_APK_BYTES
      || typeof value.versionName !== 'string' || !/^[0-9A-Za-z.+_-]{1,64}$/.test(value.versionName)
      || typeof value.packageName !== 'string' || !/^[A-Za-z]\w*(?:\.[A-Za-z]\w*)+$/.test(value.packageName)
      || typeof value.file !== 'string' || !/^[A-Za-z0-9][A-Za-z0-9._-]*\.apk$/.test(value.file)
      || typeof value.sha256 !== 'string' || !/^[a-fA-F0-9]{64}$/.test(value.sha256)) {
    throw new Error('Invalid Android release manifest');
  }
  return {
    schemaVersion: 1, versionName: value.versionName, versionCode: value.versionCode,
    releaseVersion: value.releaseVersion, packageName: value.packageName,
    file: value.file, sizeBytes: value.sizeBytes, sha256: value.sha256.toLowerCase(),
    notes: typeof value.notes === 'string' ? value.notes.slice(0, 4000) : '',
  };
}

// Read the actual uploaded file, not the platform's advertised version. Incomplete
// uploads and a new manifest paired with an old APK must never be offered to players.
function createAndroidUpdateStore(directory = releaseDirectory) {
  let verifiedFingerprint;
  let pendingHash;
  const fingerprint = (stat, filename, hash) =>
    `${filename}:${hash}:${stat.size}:${stat.mtimeMs}:${stat.ctimeMs}:${stat.ino}`;

  async function openRelease(expectedHash) {
    const root = path.resolve(typeof directory === 'function' ? directory() : directory);
    const manifestPath = path.join(root, MANIFEST_NAME);
    const manifestStat = await fs.stat(manifestPath);
    if (manifestStat.size > 32768) throw new Error('Manifest too large');
    const manifest = validateManifest(JSON.parse(await fs.readFile(manifestPath, 'utf8')));
    if (expectedHash && expectedHash !== manifest.sha256) throw new Error('Release has changed');
    const filename = path.join(root, manifest.file);
    // Symlink targets outside the configured publication folder are not downloads.
    if (path.dirname(await fs.realpath(filename)) !== await fs.realpath(root)) throw new Error('Invalid APK location');
    const handle = await fs.open(filename, 'r');
    try {
      const stat = await handle.stat();
      if (!stat.isFile() || stat.size !== manifest.sizeBytes) throw new Error('APK upload incomplete');
      const key = fingerprint(stat, filename, manifest.sha256);
      if (verifiedFingerprint !== key) {
        if (!pendingHash || pendingHash.key !== key) {
          const promise = (async () => {
            const hash = createHash('sha256');
            for await (const chunk of handle.createReadStream({ autoClose: false, start: 0 })) hash.update(chunk);
            const after = await handle.stat();
            if (fingerprint(after, filename, manifest.sha256) !== key
                || hash.digest('hex') !== manifest.sha256) throw new Error('APK checksum mismatch');
            verifiedFingerprint = key;
          })();
          pendingHash = { key, promise };
        }
        const current = pendingHash;
        try { await current.promise; }
        finally { if (pendingHash === current) pendingHash = null; }
      }
      return { manifest, handle };
    } catch (error) {
      await handle.close();
      throw error;
    }
  }
  return { openRelease };
}

module.exports = { MANIFEST_NAME, validateManifest, createAndroidUpdateStore };
