const express = require('express');
const multer = require('multer');
const { defaultStore } = require('../../services/platformContentStore');
const { writeAudit } = require('../../utils/audit');

function createRouter({ store = defaultStore(), audit = writeAudit } = {}) {
  const router = express.Router();
  const upload = multer({ storage: multer.memoryStorage(), limits: { fileSize: 2 * 1024 * 1024, files: 1 } });
  const wrap = handler => async (req, res, next) => { try { await handler(req, res); } catch (error) { next(error); } };
  async function record(req, action, payload) {
    try { await audit({ adminUserId: req.admin.userId, action, targetType: 'platform_content', targetId: 'shared', payload }); }
    catch (error) { console.error('platform content audit:', error.message); }
  }
  router.get('/', wrap(async (_req, res) => res.set('Cache-Control', 'no-store').json({ success: true, data: store.read() })));
  router.put('/', wrap(async (req, res) => {
    const data = store.publish(req.body);
    await record(req, 'platform_content.publish', { revision: data.revision, credits: data.credits.length, communities: data.communities.length });
    res.json({ success: true, data, message: '已发布，客户端下次刷新即可读取' });
  }));
  router.post('/qr', upload.single('file'), wrap(async (req, res) => {
    if (!req.file) throw Object.assign(new Error('请选择二维码图片'), { status: 400 });
    const data = store.upload(req.file.buffer);
    await record(req, 'platform_content.qr_upload', { url: data.url });
    res.json({ success: true, data });
  }));
  router.use((err, _req, res, _next) => res.status(err.status || (err.code === 'LIMIT_FILE_SIZE' ? 400 : 500)).json({
    success: false, message: err.code === 'LIMIT_FILE_SIZE' ? '二维码图片不能超过 2MB' : err.status ? err.message : '内容操作失败，请稍后重试',
  }));
  return router;
}
module.exports = createRouter();
module.exports.createRouter = createRouter;
