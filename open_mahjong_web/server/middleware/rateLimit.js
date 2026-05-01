/**
 * 固定时间窗内按 key 计数，超限返回 429。
 * @param {{ windowMs: number, max: number, keyFn?: (req) => string }} opts
 */
function createWindowLimiter(opts) {
  const { windowMs, max, keyFn = (req) => req.ip || 'unknown' } = opts;
  const buckets = new Map();

  return function rateLimitMiddleware(req, res, next) {
    const key = keyFn(req);
    const now = Date.now();
    let b = buckets.get(key);
    if (!b || now >= b.resetAt) {
      b = { count: 0, resetAt: now + windowMs };
      buckets.set(key, b);
    }
    b.count += 1;
    if (b.count > max) {
      const retrySec = Math.ceil((b.resetAt - now) / 1000);
      res.setHeader('Retry-After', String(Math.max(1, retrySec)));
      return res.status(429).json({
        success: false,
        message: '请求过于频繁，请稍后再试',
      });
    }
    next();
  };
}

module.exports = { createWindowLimiter };
