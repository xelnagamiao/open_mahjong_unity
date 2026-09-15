const crypto = require('crypto');

const SEND_MESSAGE = '如果该邮箱关联了可找回的账户，验证码将发送至邮箱，请查收。';
const INVALID_CODE = '邮箱或验证码不正确、已过期或已失效，请重新获取验证码';
const digest = value => crypto.createHash('sha256').update(value).digest('hex');

function parseEmail(value) {
  if (typeof value !== 'string') return null;
  const email = value.trim().toLowerCase();
  return email.length <= 255 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) ? email : null;
}

function createPasswordResetHandlers({ pool, sendCode, hashPassword, secret, mailEnabled, now = Date.now }) {
  const codeHash = (email, code) => crypto.createHmac('sha256', secret)
    .update(`password-reset:${email}:${code}`).digest('hex');
  const reject = (res, status, message) => res.status(status).json({ success: false, message });

  async function send(req, res) {
    const email = parseEmail(req.body?.email);
    if (!email) return reject(res, 400, '请填写正确的邮箱地址');
    if (!mailEnabled()) return reject(res, 503, '邮件服务暂不可用，请稍后重试或联系测试群群主');
    try {
      const users = await pool.query(
        `SELECT user_id, password FROM users WHERE LOWER(email) = $1 AND is_tourist = FALSE LIMIT 2`, [email]
      );
      // Unverified registration emails may be used only after proving mailbox access here.
      // Ambiguous historical duplicates always require manual recovery.
      if (users.rows.length !== 1) return res.json({ success: true, message: SEND_MESSAGE });
      const user = users.rows[0];
      const code = String(crypto.randomInt(100000, 1000000));
      const hashedCode = codeHash(email, code);
      const timestamp = now();
      const saved = await pool.query(
        `INSERT INTO password_reset_codes (email, user_id, code_hash, password_digest, expires_at, created_at, attempts)
         VALUES ($1, $2, $3, $4, $5, $6, 0)
         ON CONFLICT (email) DO UPDATE SET user_id = EXCLUDED.user_id, code_hash = EXCLUDED.code_hash,
           password_digest = EXCLUDED.password_digest, expires_at = EXCLUDED.expires_at,
           created_at = EXCLUDED.created_at, attempts = 0
         WHERE password_reset_codes.created_at <= $7
         RETURNING email`,
        [email, user.user_id, hashedCode, digest(user.password), new Date(timestamp + 10 * 60 * 1000),
          new Date(timestamp), new Date(timestamp - 60 * 1000)]
      );
      // Also return the generic acknowledgement during the resend cooldown.
      if (!saved.rows.length) return res.json({ success: true, message: SEND_MESSAGE });
      try {
        await sendCode({ to: email, code });
      } catch {
        await pool.query('DELETE FROM password_reset_codes WHERE email = $1 AND code_hash = $2', [email, hashedCode]);
        return reject(res, 502, '验证码发送失败，请稍后重试或联系测试群群主');
      }
      return res.json({ success: true, message: SEND_MESSAGE });
    } catch (err) {
      console.error('password reset send failed:', err.code || err.name);
      return reject(res, 500, '暂时无法发送验证码，请稍后重试');
    }
  }

  async function reset(req, res) {
    const email = parseEmail(req.body?.email);
    const { code, new_password: password, confirm_password: confirmation } = req.body || {};
    if (!email || typeof code !== 'string' || code.length !== 6 || !/^\d{6}$/.test(code)) return reject(res, 400, '请填写正确的邮箱和 6 位验证码');
    if (typeof password !== 'string' || password.length < 6 || password.length > 32 || /[^\x21-\x7e]/.test(password)) {
      return reject(res, 400, '密码须为 6–32 位英文、数字或英文特殊字符');
    }
    if (password !== confirmation) return reject(res, 400, '两次输入的密码不一致');
    let client;
    try {
      client = await pool.connect();
      await client.query('BEGIN');
      const stored = await client.query('SELECT * FROM password_reset_codes WHERE email = $1 FOR UPDATE', [email]);
      const row = stored.rows[0];
      if (!row || new Date(row.expires_at).getTime() <= now() || row.attempts >= 5) {
        await client.query('ROLLBACK');
        return reject(res, 400, INVALID_CODE);
      }
      const actual = Buffer.from(codeHash(email, code), 'hex');
      const expected = Buffer.from(row.code_hash, 'hex');
      if (actual.length !== expected.length || !crypto.timingSafeEqual(actual, expected)) {
        await client.query('UPDATE password_reset_codes SET attempts = attempts + 1 WHERE email = $1', [email]);
        await client.query('COMMIT');
        return reject(res, 400, INVALID_CODE);
      }
      const users = await client.query(
        `SELECT user_id, password FROM users WHERE LOWER(email) = $1 AND is_tourist = FALSE LIMIT 2 FOR UPDATE`, [email]
      );
      const user = users.rows[0];
      if (users.rows.length !== 1 || String(user.user_id) !== String(row.user_id) || digest(user.password) !== row.password_digest) {
        await client.query('DELETE FROM password_reset_codes WHERE email = $1', [email]);
        await client.query('COMMIT');
        return reject(res, 400, INVALID_CODE);
      }
      await client.query('UPDATE users SET password = $1 WHERE user_id = $2', [hashPassword(password), user.user_id]);
      await client.query('DELETE FROM password_reset_codes WHERE email = $1', [email]);
      await client.query('DELETE FROM email_bind_codes WHERE user_id = $1', [user.user_id]);
      await client.query('COMMIT');
      return res.json({ success: true, message: '密码已重置，请使用新密码登录网页或游戏客户端' });
    } catch (err) {
      if (client) { try { await client.query('ROLLBACK'); } catch { /* connection failed */ } }
      console.error('password reset failed:', err.code || err.name);
      return reject(res, 500, '密码重置失败，请稍后重试');
    } finally {
      client?.release();
    }
  }
  return { send, reset };
}

module.exports = { createPasswordResetHandlers, parseEmail };
