const config = require('../config/config');
const { verifyToken } = require('../utils/jwt');

function readPlayer(req) {
  const header = req.headers.authorization || '';
  const token = header.startsWith('Bearer ') ? header.slice(7) : null;
  if (!token) return null;
  const payload = verifyToken(token, config.playerAuth.jwtSecret);
  if (!payload || !payload.user_id) return null;
  if (payload.aud !== config.playerAuth.audience) return null;
  return {
    userId: Number(payload.user_id),
    username: payload.username || '',
  };
}

function requirePlayer(req, res, next) {
  const header = req.headers.authorization || '';
  const token = header.startsWith('Bearer ') ? header.slice(7) : null;
  if (!token) {
    return res.status(401).json({ success: false, message: '未登录或令牌无效' });
  }
  const player = readPlayer(req);
  if (!player) {
    return res.status(401).json({ success: false, message: '登录已过期，请重新登录' });
  }
  req.player = player;
  return next();
}

function optionalPlayer(req, _res, next) {
  req.player = readPlayer(req);
  return next();
}

module.exports = { requirePlayer, optionalPlayer };
