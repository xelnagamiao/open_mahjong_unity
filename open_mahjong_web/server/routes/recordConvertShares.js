const express = require('express');
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
const { dataRoot } = require('../utils/runtimeData');
const { createWindowLimiter } = require('../middleware/rateLimit');
const { sharedSourceIsLocked, LOCKED_MESSAGE } = require('../services/duplicateRecordAccess');

const router = express.Router();
const MODES = new Set(['tz2sala', 'bz2sala', 'mjai2sala']);
const ID_RE = /^[a-f0-9]{32}$/;
const MAX_BYTES = 2 * 1024 * 1024;

router.post('/', createWindowLimiter({ windowMs: 60_000, max: 10 }), express.json({ limit: '3mb' }), async (req, res, next) => {
  const { mode, source } = req.body || {};
  if (!MODES.has(mode) || typeof source !== 'string' || !source.trim()) {
    return res.status(400).json({ message: '请选择支持阅览的转换方向并提供源牌谱' });
  }
  if (Buffer.byteLength(source, 'utf8') > MAX_BYTES) {
    return res.status(413).json({ message: '分享的源牌谱不能超过 2 MB' });
  }
  try {
    if (await sharedSourceIsLocked(() => require('../config/database'), source)) return res.status(403).json({ message: LOCKED_MESSAGE });
    const dir = path.join(dataRoot(), 'record-convert-shares');
    await fs.mkdir(dir, { recursive: true });
    const id = crypto.randomBytes(16).toString('hex');
    await fs.writeFile(path.join(dir, `${id}.json`), JSON.stringify({ mode, source }), { flag: 'wx' });
    res.status(201).json({ id });
  } catch (error) { next(error); }
});

router.get('/:id', createWindowLimiter({ windowMs: 60_000, max: 60 }), async (req, res, next) => {
  if (!ID_RE.test(req.params.id)) return res.status(404).json({ message: '分享链接无效或牌谱不存在' });
  try {
    const raw = await fs.readFile(path.join(dataRoot(), 'record-convert-shares', `${req.params.id}.json`), 'utf8');
    const shared = JSON.parse(raw);
    if (await sharedSourceIsLocked(() => require('../config/database'), shared.source)) return res.status(403).json({ message: LOCKED_MESSAGE });
    res.set('Cache-Control', 'no-store').json(shared);
  } catch (error) {
    if (error.code === 'ENOENT') return res.status(404).json({ message: '分享链接无效或牌谱不存在' });
    next(error);
  }
});

router.use((error, req, res, next) => {
  if (error.type === 'entity.too.large') return res.status(413).json({ message: '分享的源牌谱不能超过 2 MB' });
  if (error.type === 'entity.parse.failed') return res.status(400).json({ message: '分享请求格式不正确' });
  console.error('record-convert-share:', error);
  res.status(500).json({ message: '牌谱分享服务暂时不可用，请稍后重试' });
});

module.exports = router;
