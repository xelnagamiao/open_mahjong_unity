const config = require('../config/config');

// Python validates the forwarded bearer token itself and owns every inventory write.
module.exports = function rewardProxy(prefix) {
  return async (req, res) => {
    if (!['GET', 'POST', 'PATCH'].includes(req.method)) return res.sendStatus(405);
    try {
      const response = await fetch(`${config.calcServer.baseUrl.replace(/\/$/, '')}${prefix}${req.url === '/' ? '' : req.url}`, {
        method: req.method,
        headers: { 'Content-Type': 'application/json', Authorization: req.headers.authorization || '' },
        body: req.method === 'GET' ? undefined : JSON.stringify(req.body || {}),
        signal: AbortSignal.timeout(20000),
      });
      const data = await response.json();
      res.set('Cache-Control', 'no-store');
      res.status(response.status).json(response.ok ? data : {
        success: false, message: typeof data.detail === 'string' ? data.detail : '兑换操作失败',
        retryable: response.status >= 500 || response.status === 429,
      });
    } catch {
      res.status(503).json({ success: false, retryable: true, message: '服务暂不可用；请重试原操作，避免重复创建或领取' });
    }
  };
};
