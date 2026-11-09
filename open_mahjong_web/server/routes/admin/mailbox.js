const express = require('express');
const config = require('../../config/config');
const router = express.Router();

// Game mail is owned by Python. Never write rewards or player inventory here.
async function forward(req, res, path) {
  try {
    const response = await fetch(`${config.calcServer.baseUrl.replace(/\/$/, '')}/admin/mail${path}`, {
      method: req.method,
      headers: { 'Content-Type': 'application/json', Authorization: req.headers.authorization },
      body: req.method === 'GET' ? undefined : JSON.stringify(req.body || {}),
      signal: AbortSignal.timeout(15000),
    });
    const data = await response.json();
    return res.status(response.status).json(response.ok ? data : {
      success: false,
      message: typeof data.detail === 'string' ? data.detail : '游戏内邮件操作失败',
      retryable: response.status >= 500,
    });
  } catch (error) {
    console.error('Mailbox service unavailable:', error.message);
    return res.status(503).json({ success: false, retryable: true,
      message: '邮箱服务暂不可用；发送结果未确认时，请使用原投递编号重试' });
  }
}

router.post('/send', (req, res) => forward(req, res, ''));
router.get('/users/:userId', (req, res) => {
  if (!/^[1-9]\d{0,18}$/.test(req.params.userId)) {
    return res.status(400).json({ success: false, message: '无效的玩家 ID' });
  }
  return forward(req, res, `/users/${req.params.userId}`);
});
module.exports = router;
