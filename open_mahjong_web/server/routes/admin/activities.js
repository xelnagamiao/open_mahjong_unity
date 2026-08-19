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

async function writeAuditSafe(req, action, targetId, payload) {
  try {
    await writeAudit({
      adminUserId: req.admin.userId,
      action,
      targetType: 'activity',
      targetId,
      payload,
    });
  } catch (err) {
    console.error('activity audit skipped:', err.message || err);
  }
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
    });
    await writeAuditSafe(req, 'activity.create', item.id, { title: item.title, status: item.status });
    res.json({ success: true, data: item, message: '已创建草稿' });
  })
);

router.put(
  '/:id',
  wrap(async (req, res) => {
    const item = store.updateActivity(req.params.id, {
      title: req.body.title,
      body: req.body.body,
      sort: req.body.sort,
      image_urls: req.body.image_urls,
    });
    await writeAuditSafe(req, 'activity.update', item.id, {
      title: item.title,
      status: item.status,
    });
    res.json({ success: true, data: item, message: '内容已保存' });
  })
);

router.post(
  '/:id/status',
  wrap(async (req, res) => {
    const item = store.setActivityStatus(req.params.id, req.body.status);
    await writeAuditSafe(req, 'activity.status', item.id, { status: item.status });
    const messages = {
      published: '已发布到通知页',
      ended: '已标记为结束，玩家仍能看到',
      offline: '已下架，通知页不再显示',
    };
    res.json({
      success: true,
      data: item,
      message: messages[item.status] || '状态已更新',
    });
  })
);

router.delete(
  '/:id',
  wrap(async (req, res) => {
    store.deleteActivity(req.params.id);
    await writeAuditSafe(req, 'activity.delete', req.params.id);
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
    await writeAuditSafe(req, 'activity.cover', item.id);
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
    await writeAuditSafe(req, 'activity.image.add', item.id);
    res.json({ success: true, data: item, message: '图片已添加' });
  })
);

router.delete(
  '/:id/images/:filename',
  wrap(async (req, res) => {
    const item = store.removeBodyImage(req.params.id, req.params.filename);
    await writeAuditSafe(req, 'activity.image.remove', item.id, {
      filename: req.params.filename,
    });
    res.json({ success: true, data: item, message: '图片已删除' });
  })
);

router.delete(
  '/:id/cover',
  wrap(async (req, res) => {
    const item = store.removeCover(req.params.id);
    await writeAuditSafe(req, 'activity.cover.remove', item.id);
    res.json({ success: true, data: item, message: '封面已移除' });
  })
);

module.exports = router;
