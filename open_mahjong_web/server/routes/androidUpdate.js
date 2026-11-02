const express = require('express');
const { createAndroidUpdateStore } = require('../services/androidUpdate');

function publicRelease(manifest) {
  const { file, ...data } = manifest;
  return { status: 'available', ...data, downloadPath: `/api/client-updates/android/apk/${manifest.sha256}` };
}

function createRouter(store = createAndroidUpdateStore()) {
  const router = express.Router();
  router.use((_req, res, next) => {
    res.set('Cache-Control', 'no-store');
    next();
  });

  router.get('/', async (req, res) => {
    const integer = value => typeof value === 'string' && /^\d{1,10}$/.test(value)
      && Number(value) <= 2147483647;
    if (!integer(req.query.requiredReleaseVersion) || !integer(req.query.installedVersionCode)) {
      return res.status(400).json({ status: 'error', message: '更新请求参数无效。' });
    }
    const required = Number(req.query.requiredReleaseVersion);
    if (!required) return res.json({ status: 'pending', message: '服务器暂未提供更新版本信息，请稍后重试。' });
    let release;
    try {
      release = await store.openRelease();
      const { manifest } = release;
      if (manifest.releaseVersion !== required || manifest.versionCode <= Number(req.query.installedVersionCode)) {
        return res.json({ status: 'pending', message: '与当前服务器匹配的新安装包尚未发布，请稍后重试。' });
      }
      return res.json(publicRelease(manifest));
    } catch (_) {
      return res.json({ status: 'pending', message: '新安装包尚未就绪，可能仍在上传，请稍后重试。' });
    } finally {
      if (release) await release.handle.close();
    }
  });

  // Browser downloads have no installed version. Resolve the current verified
  // package for every click instead of embedding a release number in the site.
  router.get('/latest', async (_req, res) => {
    let release;
    try {
      release = await store.openRelease();
      return res.json(publicRelease(release.manifest));
    } catch (_) {
      return res.json({ status: 'pending', message: '新安装包尚未就绪，请稍后重试。' });
    } finally {
      if (release) await release.handle.close();
    }
  });

  // The old static APK URL can proxy here. A temporary, non-cacheable redirect
  // follows future uploads automatically; a hash never names different APK bytes.
  router.get('/download', async (_req, res) => {
    let release;
    try {
      release = await store.openRelease();
      return res.redirect(302, publicRelease(release.manifest).downloadPath);
    } catch (_) {
      return res.status(503).json({ status: 'pending', message: '新安装包尚未就绪，请稍后重试。' });
    } finally {
      if (release) await release.handle.close();
    }
  });

  router.get('/apk/:sha256', async (req, res) => {
    if (!/^[a-f0-9]{64}$/.test(req.params.sha256)) return res.status(404).end();
    let release;
    try {
      release = await store.openRelease(req.params.sha256);
      const size = release.manifest.sizeBytes;
      const etag = `"sha256-${release.manifest.sha256}"`;
      let start = 0;
      let end = size - 1;
      let partial = false;
      // ESA and download managers need real ranges to cache/resume large APKs.
      if (req.method === 'GET' && req.get('Range')
          && (!req.get('If-Range') || req.get('If-Range') === etag)) {
        const ranges = req.range(size, { combine: true });
        if (ranges === -1) {
          await release.handle.close();
          return res.status(416).set('Content-Range', `bytes */${size}`).end();
        }
        // Unsupported/multipart ranges fall back to a full response per HTTP.
        if (Array.isArray(ranges) && ranges.type === 'bytes' && ranges.length === 1) {
          ({ start, end } = ranges[0]);
          partial = true;
        }
      }
      res.set({
        // Only verified SHA-256 URLs are cacheable; metadata stays no-store.
        // Revoking an already cached APK requires an ESA purge of that hash URL.
        'Cache-Control': 'public, max-age=0, s-maxage=86400, must-revalidate',
        'ETag': etag,
        'Accept-Ranges': 'bytes',
        'Content-Type': 'application/vnd.android.package-archive',
        'Content-Length': String(end - start + 1),
        'Content-Disposition': `attachment; filename="open_mahjong_unity-${release.manifest.versionName}-${release.manifest.versionCode}.apk"`,
        'X-Content-Type-Options': 'nosniff',
      });
      if (partial) res.status(206).set('Content-Range', `bytes ${start}-${end}/${size}`);
      if (req.method === 'HEAD') {
        await release.handle.close();
        return res.end();
      }
      const stream = release.handle.createReadStream({ autoClose: true, start, end });
      stream.on('error', () => res.destroy());
      res.on('close', () => stream.destroy());
      stream.pipe(res);
    } catch (_) {
      if (release) await release.handle.close();
      if (res.headersSent) return res.destroy();
      res.set('Cache-Control', 'no-store');
      res.status(409).json({ status: 'pending', message: '安装包已变更或尚未就绪，请重新检查更新。' });
    }
  });
  return router;
}

module.exports = createRouter();
module.exports.createRouter = createRouter;
