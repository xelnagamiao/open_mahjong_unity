const { normalizeUsername } = require('./username');

async function findLoginAccount(pool, raw) {
  const account = normalizeUsername(raw);
  const username = await pool.query(
    'SELECT user_id, username, password, is_tourist FROM users WHERE username = $1', [account]
  );
  // Preserve email-shaped usernames. A password failure must never select another user.
  if (username.rows.length) return { user: username.rows[0] };
  if (account.length > 255 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(account)) return {};
  const email = await pool.query(
    `SELECT user_id, username, password, is_tourist FROM users
     WHERE LOWER(email) = $1 AND is_tourist = FALSE LIMIT 2`, [account.toLowerCase()]
  );
  if (email.rows.length > 1) return { error: '此邮箱关联了多个账户，请使用用户名登录' };
  return { user: email.rows[0] };
}

module.exports = { findLoginAccount };
