const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');

const hashPattern = /^[a-f0-9]{64}$/;
const integer = v => Number.isSafeInteger(v) && v > 0 && v <= 2147483647;
const signedText = m => [m.schemaVersion, m.baseVersion, m.releaseVersion, m.baseline,
  m.file, m.sizeBytes, m.unpackedBytes, m.sha256].join('\n');

function validateContent(m) {
  if (!m || m.schemaVersion !== 1 || !integer(m.baseVersion) || !integer(m.releaseVersion)
    || !hashPattern.test(m.baseline) || !hashPattern.test(m.sha256)
    || m.file !== `content-${m.sha256}.zip` || !Number.isSafeInteger(m.sizeBytes)
    || m.sizeBytes < 1 || m.sizeBytes > 2147483648 || !Number.isSafeInteger(m.unpackedBytes)
    || m.unpackedBytes < 1 || m.unpackedBytes > 4294967296
    || typeof m.signature !== 'string' || !/^[A-Za-z0-9+/=]{128,1024}$/.test(m.signature)) {
    throw new Error('Invalid content manifest');
  }
  return m;
}

// A single publication directory contains both the APK sidecar and hotupdate/.
// Preserve every published hash directory while clients may still download it.
function createContentStore(directory) {
  const root = () => path.resolve(typeof directory === 'function' ? directory() : directory);
  const verified = new Map();
  const pending = new Map();
  async function readTarget(base, release) {
    const filename = path.join(root(), 'hotupdate', String(base), String(release), 'content-update.json');
    const stat = await fs.stat(filename);
    if (stat.size > 32768) throw new Error('Manifest too large');
    const m = validateContent(JSON.parse(await fs.readFile(filename, 'utf8')));
    if (m.baseVersion !== base || m.releaseVersion !== release) throw new Error('Wrong release');
    const file = path.join(path.dirname(filename), m.file);
    if (path.dirname(await fs.realpath(file)) !== await fs.realpath(path.dirname(filename))) throw new Error('Invalid location');
    const handle = await fs.open(file, 'r');
    try {
      const stat = await handle.stat();
      if (!stat.isFile() || stat.size !== m.sizeBytes) throw new Error('Incomplete upload');
      const fingerprint = s => `${s.size}:${s.mtimeMs}:${s.ctimeMs}:${s.ino}:${m.sha256}`;
      const key = fingerprint(stat);
      if (verified.get(file) !== key) {
        const taskKey = file + ':' + key;
        let task = pending.get(taskKey);
        if (!task) {
          task = (async () => {
            const hash = crypto.createHash('sha256');
            for await (const data of handle.createReadStream({ autoClose: false, start: 0 })) hash.update(data);
            if (hash.digest('hex') !== m.sha256 || fingerprint(await handle.stat()) !== key)
              throw new Error('Content checksum mismatch');
            if (verified.size > 128) verified.clear();
            verified.set(file, key);
          })();
          pending.set(taskKey, task);
        }
        try { await task; } finally { if (pending.get(taskKey) === task) pending.delete(taskKey); }
      }
      return { manifest: m, handle };
    } catch (e) { await handle.close(); throw e; }
  }
  return { readTarget };
}

module.exports = { createContentStore, validateContent, signedText, integer, hashPattern };
