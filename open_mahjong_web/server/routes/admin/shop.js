const express = require('express');
const config = require('../../config/config');
const router = express.Router();

// The common admin router checks the token; Python verifies the same token again.
router.use(async (req, res) => {
  if (!['GET', 'POST', 'PATCH'].includes(req.method)) return res.sendStatus(405);
  const path = req.path === '/' ? '/offers' : req.path;
  if (!((req.method === 'GET' && ['/offers', '/orders'].includes(path))
      || (req.method === 'POST' && path === '/offers')
      || (req.method === 'PATCH' && /^\/offers\/[A-Za-z0-9_-]{1,64}$/.test(path)))) {
    return res.status(404).json({ success: false, message: '接口不存在' });
  }
  const query = new URLSearchParams();
  for (const key of ['page', 'limit', 'user_id', 'offer_id', 'order_id', 'from_time', 'to_time']) {
    if (typeof req.query[key] === 'string') query.set(key, req.query[key]);
  }
  const suffix = query.size ? `?${query}` : '';
  try {
    const response = await fetch(`${config.calcServer.baseUrl.replace(/\/$/, '')}/admin/shop${path}${suffix}`, {
      method: req.method,
      headers: { 'Content-Type': 'application/json', Authorization: req.headers.authorization },
      body: req.method === 'GET' ? undefined : JSON.stringify(req.body || {}),
      signal: AbortSignal.timeout(15000),
    });
    const data = await response.json();
    res.status(response.status).json(response.ok ? data : {
      success: false, message: typeof data.detail === 'string' ? data.detail : '商店操作失败',
    });
  } catch (error) {
    console.error('Shop administration unavailable:', error.message);
    res.status(503).json({ success: false, message: '商店管理服务暂不可用，请稍后重新载入确认结果' });
  }
});

module.exports = router;
