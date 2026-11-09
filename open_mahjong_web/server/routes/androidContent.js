const express = require('express');
const { createContentStore, integer, hashPattern } = require('../services/androidContent');
const { createAndroidUpdateStore, releaseDirectory } = require('../services/androidUpdate');

async function gameTarget() {
  const response = await fetch('http://127.0.0.1:8081/client-version', { signal: AbortSignal.timeout(5000) });
  if (!response.ok) throw new Error('Version service unavailable');
  const target = await response.json();
  if (!integer(target.baseVersion) || !integer(target.releaseVersion)) throw new Error('Invalid target version');
  return target;
}

function createRouter(store = createContentStore(releaseDirectory), targetProvider = gameTarget,
  apkStore = createAndroidUpdateStore()) {
  const router = express.Router();
  router.use((_req, res, next) => { res.set('Cache-Control', 'no-store'); next(); });
  router.get('/version', async (_req, res) => {
    try { return res.json(await targetProvider()); }
    catch (_) { return res.status(503).json({ status: 'pending' }); }
  });
  router.get('/content', async (req, res) => {
    let opened;
    try {
      const base = Number(req.query.baseVersion), release = Number(req.query.releaseVersion);
      if (!integer(base) || !integer(release)) return res.status(400).json({ status: 'error' });
      const target = await targetProvider();
      if (base > target.baseVersion || (base === target.baseVersion && release > target.releaseVersion))
        return res.json({ status: 'client_ahead', ...target });
      if (base !== target.baseVersion) return res.json({ status: 'apk_required', ...target });
      if (release === target.releaseVersion) return res.json({ status: 'current', ...target });
      opened = await store.readTarget(target.baseVersion, target.releaseVersion);
      return res.json({ ...opened.manifest, status: 'available',
        downloadPath: `/api/client-updates/android/content/${target.baseVersion}/${target.releaseVersion}/${opened.manifest.sha256}` });
    } catch (_) { return res.json({ status: 'pending', message: '对应版本的内容包尚未完整发布。' }); }
    finally { if (opened) await opened.handle.close(); }
  });
  router.get('/framework', async (req, res) => {
    let opened;
    try {
      const installed = Number(req.query.installedVersionCode);
      if (!Number.isSafeInteger(installed) || installed < 0) return res.status(400).end();
      const target = await targetProvider();
      opened = await apkStore.openRelease();
      const m = opened.manifest;
      if (m.baseVersion !== target.baseVersion || m.versionCode <= installed || m.releaseVersion > target.releaseVersion)
        return res.json({ status: 'pending' });
      return res.json({ ...m, status: 'available', downloadPath: `/api/client-updates/android/apk/${m.sha256}` });
    } catch (_) { return res.json({ status: 'pending' }); }
    finally { if (opened) await opened.handle.close(); }
  });
  router.get('/content/:base/:release/:sha256', async (req, res) => {
    let opened;
    try {
      const base = Number(req.params.base), release = Number(req.params.release);
      if (!integer(base) || !integer(release) || !hashPattern.test(req.params.sha256)) return res.status(404).end();
      opened = await store.readTarget(base, release);
      const m = opened.manifest;
      if (req.params.sha256 !== m.sha256) throw new Error('Wrong content hash');
      const etag = `"sha256-${m.sha256}"`;
      let start = 0, end = m.sizeBytes - 1, partial = false;
      if (req.get('Range') && (!req.get('If-Range') || req.get('If-Range') === etag)) {
        const ranges = req.range(m.sizeBytes, { combine: true });
        if (ranges === -1) { await opened.handle.close(); opened = null;
          return res.status(416).set('Content-Range', `bytes */${m.sizeBytes}`).end(); }
        if (Array.isArray(ranges) && ranges.type === 'bytes' && ranges.length === 1) {
          ({ start, end } = ranges[0]); partial = true;
        }
      }
      res.set({ 'Content-Type': 'application/zip', 'Accept-Ranges': 'bytes', ETag: etag,
        'Content-Length': String(end - start + 1), 'Cache-Control': 'public, max-age=31536000, immutable',
        'X-Content-Type-Options': 'nosniff' });
      if (partial) res.status(206).set('Content-Range', `bytes ${start}-${end}/${m.sizeBytes}`);
      if (req.method === 'HEAD') { await opened.handle.close(); opened = null; return res.end(); }
      const stream = opened.handle.createReadStream({ autoClose: true, start, end });
      opened = null;
      stream.on('error', () => res.destroy()); res.on('close', () => stream.destroy()); stream.pipe(res);
    } catch (_) {
      if (opened) await opened.handle.close();
      if (!res.headersSent) res.status(404).set('Cache-Control', 'no-store').end();
      else res.destroy();
    }
  });
  return router;
}
module.exports = createRouter();
module.exports.createRouter = createRouter;
