const config = require('../config/config');
const pool = require('../config/database');
const { checkAccountSession } = require('../utils/accountBans');
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
    expiresAt: payload.exp,
  };
}

async function requirePlayer(req, res, next) {
  const header = req.headers.authorization || '';
  const token = header.startsWith('Bearer ') ? header.slice(7) : null;
  if (!token) {
    return res.status(401).json({ success: false, message: '未登录或令牌无效' });
  }
  const player = readPlayer(req);
  if (!player) {
    return res.status(401).json({ success: false, message: '登录已过期，请重新登录' });
  }
  try {
    const account = await checkAccountSession(pool, player.userId, res);
    if (!account) return;
    player.activeBanType = account.active_ban_type;
  } catch (err) {
    return next(err);
  }
  req.player = player;
  return next();
}

function optionalPlayer(req, res, next) {
  if (readPlayer(req)) return requirePlayer(req, res, next);
  req.player = null;
  return next();
}

function requireSpeaking(req, res, next) {
  if (req.player.activeBanType === 'chat') {
    return res.status(403).json({ success: false, message: '账号当前已被禁言' });
  }
  return next();
}

module.exports = { requirePlayer, optionalPlayer, requireSpeaking };
