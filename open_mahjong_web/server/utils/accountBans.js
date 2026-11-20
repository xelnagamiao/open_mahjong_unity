const BAN_TYPES = ['login', 'chat', 'match', 'full'];
const ACTIVE_BAN = '(ban_expires_at IS NULL OR ban_expires_at > LOCALTIMESTAMP)';
const BAN_COLUMNS = `CASE WHEN ${ACTIVE_BAN} THEN ban_type END AS active_ban_type, ban_reason, ban_expires_at`;

function loginBanMessage(user) {
  if (!['login', 'full'].includes(user.active_ban_type)) return null;
  const until = user.ban_expires_at ? `至 ${new Date(user.ban_expires_at).toLocaleString('zh-CN')}` : '（永久）';
  return `账号已被禁止登录${until}${user.ban_reason ? '，原因：' + user.ban_reason : ''}`;
}

// Tokens identify an account. Permissions can change throughout their lifetime.
async function checkAccountSession(pool, userId, res) {
  const { rows } = await pool.query(`SELECT ${BAN_COLUMNS} FROM users WHERE user_id = $1`, [userId]);
  const message = rows.length ? loginBanMessage(rows[0]) : '账号不存在，请重新登录';
  if (message) {
    res.status(401).json({ success: false, message });
    return null;
  }
  return rows[0];
}

module.exports = { BAN_TYPES, ACTIVE_BAN, BAN_COLUMNS, loginBanMessage, checkAccountSession };
