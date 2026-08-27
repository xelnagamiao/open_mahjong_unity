const pool = require('../config/database');
const config = require('../config/config');
const { verifyToken } = require('../utils/jwt');

function requireEventAdmin(req, res, next) {
  const header = req.headers.authorization || '';
  const token = header.startsWith('Bearer ') ? header.slice(7) : null;
  if (!token) {
    return res.status(401).json({ success: false, message: '未登录或令牌无效' });
  }

  const payload = verifyToken(token, config.eventAdmin.jwtSecret);
  if (!payload || !payload.user_id) {
    return res.status(401).json({ success: false, message: '登录已过期，请重新登录' });
  }
  if (payload.aud !== config.eventAdmin.audience) {
    return res.status(401).json({ success: false, message: '令牌类型无效' });
  }

  req.eventAdmin = {
    userId: Number(payload.user_id),
    username: payload.username || '',
  };
  return next();
}

/**
 * 校验当前用户对 :eventId 有 owner|admin 权限。
 * 挂载后设置 req.eventRole / req.event。
 */
async function requireEventMembership(req, res, next) {
  try {
    const eventId = String(req.params.eventId || '').trim();
    if (!eventId) {
      return res.status(400).json({ success: false, message: '缺少赛事 ID' });
    }

    const eventRes = await pool.query(
      `SELECT event_id, name, description, status, kind, entry_config,
              reopen_requested, created_by, closed_at, created_at, updated_at
       FROM events WHERE event_id = $1`,
      [eventId]
    );
    if (eventRes.rows.length === 0) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }

    const roleRes = await pool.query(
      `SELECT role FROM event_admins WHERE event_id = $1 AND user_id = $2`,
      [eventId, req.eventAdmin.userId]
    );
    if (roleRes.rows.length === 0) {
      return res.status(403).json({ success: false, message: '您不是该赛事的主管理员或子管理员' });
    }

    req.event = eventRes.rows[0];
    req.eventRole = roleRes.rows[0].role;
    return next();
  } catch (err) {
    console.error('requireEventMembership:', err);
    return res.status(500).json({ success: false, message: '服务器内部错误' });
  }
}

function requireEventOwner(req, res, next) {
  if (req.eventRole !== 'owner') {
    return res.status(403).json({ success: false, message: '仅赛事主管理员可执行此操作' });
  }
  return next();
}

module.exports = {
  requireEventAdmin,
  requireEventMembership,
  requireEventOwner,
};
