const express = require('express');
const multer = require('multer');
const { writeAudit } = require('../../utils/audit');
const store = require('../../services/activityStore');

const router = express.Router();
const MAX_FILE_BYTES = 2 * 1024 * 1024;

const upload = multer({
  storage: multer.memoryStorage(),
  limits: { fileSize: MAX_FILE_BYTES, files: 1 },
  fileFilter: (_req, file, cb) => {
    if (!file.mimetype || !file.mimetype.startsWith('image/')) {
      cb(Object.assign(new Error('请上传图片文件'), { status: 400 }));
      return;
    }
    cb(null, true);
  },
});

function sendError(res, err) {
  const status = err.status || (err.code === 'LIMIT_FILE_SIZE' ? 400 : 500);
  const message =
    err.code === 'LIMIT_FILE_SIZE'
      ? '图片不能超过 2MB'
      : err.message || '服务器内部错误';
  if (status >= 500) console.error('admin activities:', err);
  return res.status(status).json({ success: false, message });
}

function wrap(handler) {
  return async (req, res) => {
    try {
      await handler(req, res);
    } catch (err) {
      sendError(res, err);
    }
  };
}

router.get(
  '/',
  wrap(async (_req, res) => {
    res.json({ success: true, data: { items: store.listAll() } });
  })
);

router.get(
  '/:id',
  wrap(async (req, res) => {
    res.json({ success: true, data: store.getById(req.params.id) });
  })
);

router.post(
  '/',
  wrap(async (req, res) => {
    const item = store.createActivity({
      title: req.body.title,
      body: req.body.body,
      sort: req.body.sort,
      published: req.body.published,
    });
    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'activity.create',
      targetType: 'activity',
      targetId: item.id,
      payload: { title: item.title },
    });
    res.json({ success: true, data: item, message: '活动已创建' });
  })
);

router.put(
  '/:id',
  wrap(async (req, res) => {
    const item = store.updateActivity(req.params.id, {
      title: req.body.title,
      body: req.body.body,
      sort: req.body.sort,
      published: req.body.published,
      image_urls: req.body.image_urls,
    });
    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'activity.update',
      targetType: 'activity',
      targetId: item.id,
      payload: { title: item.title, published: item.published },
    });
    res.json({ success: true, data: item, message: '活动已保存' });
  })
);

router.delete(
  '/:id',
  wrap(async (req, res) => {
    store.deleteActivity(req.params.id);
    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'activity.delete',
      targetType: 'activity',
      targetId: req.params.id,
    });
    res.json({ success: true, message: '活动已删除' });
  })
);

function receiveFile(req, res, next) {
  upload.single('file')(req, res, (err) => {
    if (err) return sendError(res, err);
    next();
  });
}

router.post(
  '/:id/cover',
  receiveFile,
  wrap(async (req, res) => {
    if (!req.file) {
      return res.status(400).json({ success: false, message: '请选择封面图' });
    }
    const item = store.saveCover(req.params.id, req.file);
    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'activity.cover',
      targetType: 'activity',
      targetId: item.id,
    });
    res.json({ success: true, data: item, message: '封面已更新' });
  })
);

router.post(
  '/:id/images',
  receiveFile,
  wrap(async (req, res) => {
    if (!req.file) {
      return res.status(400).json({ success: false, message: '请选择图片' });
    }
    const item = store.addBodyImage(req.params.id, req.file);
    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'activity.image.add',
      targetType: 'activity',
      targetId: item.id,
    });
    res.json({ success: true, data: item, message: '图片已添加' });
  })
);

router.delete(
  '/:id/images/:filename',
  wrap(async (req, res) => {
    const item = store.removeBodyImage(req.params.id, req.params.filename);
    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'activity.image.remove',
      targetType: 'activity',
      targetId: item.id,
      payload: { filename: req.params.filename },
    });
    res.json({ success: true, data: item, message: '图片已删除' });
  })
);

module.exports = router;
