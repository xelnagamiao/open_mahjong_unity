const express = require('express');
const { createAndroidUpdateStore } = require('../services/androidUpdate');

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
      const { file, ...data } = manifest;
      return res.json({ status: 'available', ...data, downloadPath: `/api/client-updates/android/apk/${manifest.sha256}` });
    } catch (_) {
      return res.json({ status: 'pending', message: '新安装包尚未就绪，可能仍在上传，请稍后重试。' });
    } finally {
      if (release) await release.handle.close();
    }
  });

  router.get('/apk/:sha256', async (req, res) => {
    if (!/^[a-f0-9]{64}$/.test(req.params.sha256)) return res.status(404).end();
    let release;
    try {
      release = await store.openRelease(req.params.sha256);
      res.set({
        'Content-Type': 'application/vnd.android.package-archive',
        'Content-Length': String(release.manifest.sizeBytes),
        'Content-Disposition': 'attachment; filename="open_mahjong_unity.apk"',
        'X-Content-Type-Options': 'nosniff',
      });
      const stream = release.handle.createReadStream({ autoClose: true, start: 0 });
      stream.on('error', () => res.destroy());
      res.on('close', () => stream.destroy());
      stream.pipe(res);
    } catch (_) {
      if (release) await release.handle.close();
      res.status(409).json({ status: 'pending', message: '安装包已变更或尚未就绪，请重新检查更新。' });
    }
  });
  return router;
}

module.exports = createRouter();
module.exports.createRouter = createRouter;
