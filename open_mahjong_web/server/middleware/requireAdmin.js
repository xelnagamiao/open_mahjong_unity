const config = require('../config/config');
const { verifyToken } = require('../utils/jwt');

function requireAdmin(req, res, next) {
  const header = req.headers.authorization || '';
  const token = header.startsWith('Bearer ') ? header.slice(7) : null;
  if (!token) {
    return res.status(401).json({ success: false, message: '未登录或令牌无效' });
  }

  const payload = verifyToken(token, config.admin.jwtSecret);
  if (!payload || !payload.user_id) {
    return res.status(401).json({ success: false, message: '登录已过期，请重新登录' });
  }

  const userId = Number(payload.user_id);
  if (!config.admin.userIds.has(userId)) {
    return res.status(403).json({ success: false, message: '无管理权限' });
  }

  req.admin = {
    userId,
    username: payload.username || '',
  };
  return next();
}

module.exports = { requireAdmin };
