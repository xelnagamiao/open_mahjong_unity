const express = require('express');
const config = require('../../config/config');
const router = express.Router();

// Python owns inventory transactions; forward the actual admin token so it can
// independently verify identity through the existing /admin/auth/me endpoint.
router.use(async (req, res) => {
  if (!['GET', 'POST', 'PATCH'].includes(req.method)) return res.sendStatus(405);
  try {
    const path = req.path === '/' ? '/catalog' : req.path;
    const response = await fetch(`${config.calcServer.baseUrl.replace(/\/$/, '')}/admin/inventory${path}`, {
      method: req.method,
      headers: { 'Content-Type': 'application/json', Authorization: req.headers.authorization },
      body: req.method === 'GET' ? undefined : JSON.stringify(req.body || {}),
      signal: AbortSignal.timeout(15000),
    });
    const data = await response.json();
    res.status(response.status).json(response.ok ? data : { success: false, message: typeof data.detail === 'string' ? data.detail : '物品操作失败' });
  } catch (error) {
    console.error('Inventory service unavailable:', error.message);
    res.status(503).json({ success: false, message: '物品服务暂不可用；发放或使用结果不明时请以原操作编号重试' });
  }
});
module.exports = router;
