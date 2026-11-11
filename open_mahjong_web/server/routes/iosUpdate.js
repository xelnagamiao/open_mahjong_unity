const express = require('express');
const { createIosUpdateStore, VERSION, HASH, integer } = require('../services/iosUpdate');

function publicManifest(manifest, kind) {
  const { file, ...data } = manifest;
  return file ? { ...data, ...(kind === 'content' ? { file } : {}),
    downloadPath: `/api/client-updates/ios/${kind}/${manifest.sha256}` } : data;
}

function createRouter(store = createIosUpdateStore()) {
  const router = express.Router();
  router.use((_req, res, next) => { res.set('Cache-Control', 'no-store'); next(); });
  router.get('/version', async (_req, res) => {
    let opened;
    try {
      opened = await store.openRelease();
      return res.json({ status: 'available', ...publicManifest(opened.manifest, 'ipa') });
    } catch (_) { return res.json({ status: 'pending', message: 'iOS 安装包尚未发布。' }); }
    finally { if (opened?.handle) await opened.handle.close(); }
  });
  router.get('/content', async (req, res) => {
    const { playerVersion, releaseVersion, contentVersion } = req.query;
    if (typeof playerVersion !== 'string' || !VERSION.test(playerVersion)
      || typeof releaseVersion !== 'string' || !/^\d{1,10}$/.test(releaseVersion) || !integer(Number(releaseVersion))
      || typeof contentVersion !== 'string' || !/^\d{1,10}$/.test(contentVersion)
      || !Number.isSafeInteger(Number(contentVersion)) || Number(contentVersion) > 2147483647)
      return res.status(400).json({ status: 'error', message: '更新请求参数无效。' });
    let opened;
    try {
      opened = await store.openContent();
      const manifest = opened.manifest;
      if (playerVersion !== manifest.playerVersion || Number(releaseVersion) !== manifest.releaseVersion)
        return res.json({ status: 'player_required', playerVersion: manifest.playerVersion, releaseVersion: manifest.releaseVersion });
      return res.json({ status: Number(contentVersion) >= manifest.contentVersion ? 'current' : 'available',
        ...publicManifest(manifest, 'content') });
    } catch (_) { return res.json({ status: 'pending', message: '对应的 iOS 资源包尚未发布。' }); }
    finally { if (opened?.handle) await opened.handle.close(); }
  });

  async function download(req, res, kind) {
    if (!HASH.test(req.params.sha256)) return res.status(404).end();
    let opened;
    try {
      opened = await (kind === 'ipa' ? store.openRelease(req.params.sha256) : store.openContent(req.params.sha256));
      if (!opened.handle) throw new Error('No local artifact');
      const manifest = opened.manifest, size = manifest.sizeBytes, etag = `"sha256-${manifest.sha256}"`;
      let start = 0, end = size - 1, partial = false;
      if (req.get('Range') && (!req.get('If-Range') || req.get('If-Range') === etag)) {
        const ranges = req.range(size, { combine: true });
        if (ranges === -1) return res.status(416).set('Content-Range', `bytes */${size}`).end();
        if (Array.isArray(ranges) && ranges.type === 'bytes' && ranges.length === 1) {
          ({ start, end } = ranges[0]); partial = true;
        }
      }
      res.set({ 'Content-Type': 'application/octet-stream', 'Content-Length': String(end - start + 1),
        'Content-Disposition': `attachment; filename="${manifest.file}"`, 'Accept-Ranges': 'bytes', ETag: etag,
        'Cache-Control': 'public, max-age=0, s-maxage=86400, must-revalidate', 'X-Content-Type-Options': 'nosniff' });
      if (partial) res.status(206).set('Content-Range', `bytes ${start}-${end}/${size}`);
      if (req.method === 'HEAD') return res.end();
      const stream = opened.handle.createReadStream({ autoClose: true, start, end });
      opened = null;
      stream.on('error', () => res.destroy()); res.on('close', () => stream.destroy()); stream.pipe(res);
    } catch (_) {
      if (res.headersSent) res.destroy();
      else res.status(404).set('Cache-Control', 'no-store').end();
    } finally { if (opened?.handle) await opened.handle.close(); }
  }
  router.get('/ipa/:sha256', (req, res) => download(req, res, 'ipa'));
  router.get('/content/:sha256', (req, res) => download(req, res, 'content'));
  return router;
}

module.exports = createRouter();
module.exports.createRouter = createRouter;
