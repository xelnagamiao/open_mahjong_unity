const express = require('express');
const { writeAudit } = require('../../utils/audit');
const store = require('../../services/classicRecordsStore');
const pool = require('../../config/database');

const router = express.Router();

function sendError(res, err) {
  const status = err.status || 500;
  if (status >= 500) console.error('admin classic-records:', err);
  return res.status(status).json({
    success: false,
    message: err.message || '服务器内部错误',
  });
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

async function writeAuditSafe(req, action, targetId, payload) {
  try {
    await writeAudit({
      adminUserId: req.admin.userId,
      action,
      targetType: 'classic_record',
      targetId,
      payload,
    });
  } catch (err) {
    console.error('classic record audit skipped:', err.message || err);
  }
}

async function assertGameExists(gameId) {
  const result = await pool.query(
    'SELECT game_id FROM game_records WHERE game_id = $1 LIMIT 1',
    [gameId]
  );
  if (!result.rowCount) {
    throw Object.assign(new Error('没有找到这份牌谱'), { status: 404 });
  }
}

router.get(
  '/',
  wrap(async (_req, res) => {
    const items = await store.attachRecordMeta(store.listAll());
    res.json({ success: true, data: { items } });
  })
);

router.post(
  '/',
  wrap(async (req, res) => {
    const parsed = store.parseClassicInput(req.body || {});
    await assertGameExists(parsed.game_id);
    const item = store.createClassicRecord(req.body || {});
    const [decorated] = await store.attachRecordMeta([item]);
    await writeAuditSafe(req, 'classic_record.create', item.id, {
      game_id: item.game_id,
      description: item.description,
    });
    res.json({ success: true, data: decorated, message: '已添加经典牌谱' });
  })
);

router.put(
  '/:id',
  wrap(async (req, res) => {
    const current = store.listAll().find((row) => row.id === req.params.id);
    if (!current) {
      throw Object.assign(new Error('经典牌谱不存在'), { status: 404 });
    }
    const parsed = store.parseClassicInput({
      url: req.body?.url,
      game_id: req.body?.game_id || req.body?.gameId || current.game_id,
      round: req.body?.round !== undefined ? req.body.round : current.round,
      node: req.body?.node !== undefined ? req.body.node : current.node,
      description: req.body?.description != null ? req.body.description : current.description,
      sort: req.body?.sort !== undefined ? req.body.sort : current.sort,
    });
    await assertGameExists(parsed.game_id);
    const item = store.updateClassicRecord(req.params.id, req.body || {});
    const [decorated] = await store.attachRecordMeta([item]);
    await writeAuditSafe(req, 'classic_record.update', item.id, {
      game_id: item.game_id,
      description: item.description,
    });
    res.json({ success: true, data: decorated, message: '已保存' });
  })
);

router.delete(
  '/:id',
  wrap(async (req, res) => {
    store.deleteClassicRecord(req.params.id);
    await writeAuditSafe(req, 'classic_record.delete', req.params.id);
    res.json({ success: true, message: '已删除' });
  })
);

module.exports = router;
