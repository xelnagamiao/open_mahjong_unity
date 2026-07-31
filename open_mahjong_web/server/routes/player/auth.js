const express = require('express');
const crypto = require('crypto');
const router = express.Router();
const pool = require('../../config/database');
const config = require('../../config/config');
const { verifyPassword, hashPassword } = require('../../utils/password');
const { signToken } = require('../../utils/jwt');
const { requirePlayer } = require('../../middleware/requirePlayer');
const { createWindowLimiter, getClientIp } = require('../../middleware/rateLimit');
const { listUserEvents } = require('../../utils/eventAdminHelpers');
const { sendEmailBindCode } = require('../../utils/mailer');

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const CODE_TTL_MS = 10 * 60 * 1000;
const RESEND_COOLDOWN_MS = 60 * 1000;
const registrationLimiter = createWindowLimiter({
  windowMs: 24 * 60 * 60 * 1000,
  max: 3,
  keyFn: (req) => `${getClientIp(req)}:player-register`,
  countSuccessfulOnly: true,
});

function normalizeEmail(raw) {
  const email = String(raw || '').trim().toLowerCase();
  if (!email) return { error: '请填写邮箱' };
  if (email.length > 255) return { error: '邮箱过长' };
  if (!EMAIL_RE.test(email)) return { error: '邮箱格式不正确' };
  return { value: email };
}

function generateCode() {
  return String(crypto.randomInt(100000, 1000000));
}

function validateUsername(raw) {
  const username = String(raw || '').trim();
  if (!username) return { error: '用户名不能为空' };
  if (username.length > 16) return { error: '用户名不能超过16个字符' };

  let length = 0;
  for (const char of username) {
    if (/[\u4e00-\u9fff]/u.test(char)) {
      length += 2;
    } else {
      length += 1;
    }
  }
  if (length < 2) {
    return { error: '用户名长度至少需要2（中文=2，其他字符=1）' };
  }
  if (length > 20) return { error: '用户名不能超过20' };
  return { value: username };
}

function validateNewPassword(raw) {
  const password = String(raw || '');
  if (!password) return { error: '密码不能为空' };
  if (password.length < 6) return { error: '密码至少需要6个字符' };
  if (password.length > 32) return { error: '密码不能超过32个字符' };
  if (!/^[\x21-\x7e]+$/.test(password)) {
    return { error: '密码只能包含英文、数字或特殊字符' };
  }
  return { value: password };
}

async function buildAuthPayload(userId, username) {
  const events = await listUserEvents(userId);
  let eventAdminToken = null;
  if (events.length > 0) {
    eventAdminToken = signToken(
      {
        user_id: userId,
        username,
        aud: config.eventAdmin.audience,
      },
      config.eventAdmin.jwtSecret,
      config.eventAdmin.jwtExpiresSec
    );
  }
  const emailRes = await pool.query(
    `SELECT email, email_verified_at FROM users WHERE user_id = $1`,
    [userId]
  );
  const emailRow = emailRes.rows[0] || {};
  return {
    user_id: userId,
    username,
    email: emailRow.email || null,
    email_verified: !!emailRow.email_verified_at,
    email_verified_at: emailRow.email_verified_at || null,
    expires_in: config.playerAuth.jwtExpiresSec,
    is_event_admin: events.length > 0,
    event_admin_token: eventAdminToken,
    events,
  };
}

router.post('/register', registrationLimiter, async (req, res) => {
  const parsedUsername = validateUsername(req.body?.username);
  if (parsedUsername.error) {
    return res.status(400).json({ success: false, message: parsedUsername.error });
  }
  const parsedPassword = validateNewPassword(req.body?.password);
  if (parsedPassword.error) {
    return res.status(400).json({ success: false, message: parsedPassword.error });
  }

  const username = parsedUsername.value;
  const password = parsedPassword.value;
  let client;
  try {
    client = await pool.connect();
    await client.query('BEGIN');
    const existing = await client.query(
      `SELECT 1 FROM users WHERE username = $1 LIMIT 1`,
      [username]
    );
    if (existing.rows.length > 0) {
      await client.query('ROLLBACK');
      return res.status(409).json({ success: false, message: '用户名已存在' });
    }

    const created = await client.query(
      `INSERT INTO users (username, password, is_tourist)
       VALUES ($1, $2, FALSE)
       RETURNING user_id, username`,
      [username, hashPassword(password)]
    );
    const user = created.rows[0];

    // 与 Python 游戏服务的 create_user 保持一致，保证网页注册的账号可直接进入游戏。
    await client.query(
      `INSERT INTO user_settings (user_id, title_id, profile_image_id, character_id, voice_id)
       VALUES ($1, 1, 1, 1, 1)`,
      [user.user_id]
    );
    await client.query(
      `INSERT INTO user_config (user_id, volume) VALUES ($1, 100)`,
      [user.user_id]
    );
    await client.query(
      `INSERT INTO rank_data (user_id, guobiao_rank, guobiao_score)
       VALUES ($1, '10级', 0)`,
      [user.user_id]
    );
    await client.query('COMMIT');

    const token = signToken(
      {
        user_id: user.user_id,
        username: user.username,
        aud: config.playerAuth.audience,
      },
      config.playerAuth.jwtSecret,
      config.playerAuth.jwtExpiresSec
    );
    const payload = await buildAuthPayload(user.user_id, user.username);
    return res.status(201).json({
      success: true,
      message: '注册成功',
      data: {
        token,
        ...payload,
      },
    });
  } catch (err) {
    if (client) {
      try {
        await client.query('ROLLBACK');
      } catch {
        /* transaction may already be closed */
      }
    }
    if (err.code === '23505') {
      return res.status(409).json({ success: false, message: '用户名已存在' });
    }
    console.error('player register error:', err);
    return res.status(500).json({ success: false, message: '注册失败，请稍后重试' });
  } finally {
    client?.release();
  }
});

router.post('/login', async (req, res) => {
  try {
    const { username, password } = req.body || {};
    if (!username || !password) {
      return res.status(400).json({ success: false, message: '请输入用户名和密码' });
    }

    const result = await pool.query(
      `SELECT user_id, username, password, is_tourist FROM users WHERE username = $1`,
      [String(username).trim()]
    );
    if (result.rows.length === 0) {
      return res.status(401).json({ success: false, message: '用户名或密码错误' });
    }

    const user = result.rows[0];
    if (user.is_tourist) {
      return res.status(403).json({ success: false, message: '游客账号不能登录网站' });
    }

    if (!verifyPassword(password, user.password)) {
      return res.status(401).json({ success: false, message: '用户名或密码错误' });
    }

    const token = signToken(
      {
        user_id: user.user_id,
        username: user.username,
        aud: config.playerAuth.audience,
      },
      config.playerAuth.jwtSecret,
      config.playerAuth.jwtExpiresSec
    );

    const payload = await buildAuthPayload(user.user_id, user.username);
    return res.json({
      success: true,
      data: {
        token,
        ...payload,
      },
    });
  } catch (err) {
    console.error('player login error:', err);
    return res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/me', requirePlayer, async (req, res) => {
  try {
    const payload = await buildAuthPayload(req.player.userId, req.player.username);
    res.json({ success: true, data: payload });
  } catch (err) {
    console.error('player me error:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/my-events', requirePlayer, async (req, res) => {
  try {
    const events = await listUserEvents(req.player.userId);
    res.json({ success: true, data: { items: events } });
  } catch (err) {
    console.error('player my-events error:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/change-password', requirePlayer, async (req, res) => {
  try {
    const { old_password, new_password } = req.body || {};
    if (!old_password || !new_password) {
      return res.status(400).json({ success: false, message: '请填写旧密码和新密码' });
    }
    const newPwd = String(new_password);
    if (newPwd.length < 6) {
      return res.status(400).json({ success: false, message: '新密码至少 6 位' });
    }

    const result = await pool.query(
      `SELECT password FROM users WHERE user_id = $1`,
      [req.player.userId]
    );
    if (result.rows.length === 0) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }
    if (!verifyPassword(old_password, result.rows[0].password)) {
      return res.status(400).json({ success: false, message: '旧密码错误' });
    }

    await pool.query(
      `UPDATE users SET password = $1 WHERE user_id = $2`,
      [hashPassword(newPwd), req.player.userId]
    );

    return res.json({ success: true, message: '密码已更新' });
  } catch (err) {
    console.error('player change-password error:', err);
    return res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/email/send-code', requirePlayer, async (req, res) => {
  try {
    if (!config.smtp.enabled) {
      return res.status(503).json({ success: false, message: '邮件服务未配置，请联系管理员' });
    }
    const parsed = normalizeEmail(req.body?.email);
    if (parsed.error) {
      return res.status(400).json({ success: false, message: parsed.error });
    }
    const email = parsed.value;

    const taken = await pool.query(
      `SELECT user_id FROM users
       WHERE LOWER(email) = $1
         AND email_verified_at IS NOT NULL
         AND user_id <> $2
       LIMIT 1`,
      [email, req.player.userId]
    );
    if (taken.rows.length > 0) {
      return res.status(400).json({ success: false, message: '该邮箱已被其他账号绑定' });
    }

    const existing = await pool.query(
      `SELECT created_at FROM email_bind_codes WHERE user_id = $1`,
      [req.player.userId]
    );
    if (existing.rows.length > 0) {
      const createdAt = new Date(existing.rows[0].created_at).getTime();
      if (Date.now() - createdAt < RESEND_COOLDOWN_MS) {
        return res.status(429).json({ success: false, message: '发送过于频繁，请稍后再试' });
      }
    }

    const code = generateCode();
    const expiresAt = new Date(Date.now() + CODE_TTL_MS);
    await pool.query(
      `INSERT INTO email_bind_codes (user_id, email, code, expires_at, created_at)
       VALUES ($1, $2, $3, $4, CURRENT_TIMESTAMP)
       ON CONFLICT (user_id) DO UPDATE
         SET email = EXCLUDED.email,
             code = EXCLUDED.code,
             expires_at = EXCLUDED.expires_at,
             created_at = CURRENT_TIMESTAMP`,
      [req.player.userId, email, code, expiresAt]
    );

    try {
      await sendEmailBindCode({
        to: email,
        code,
        username: req.player.username,
      });
    } catch (mailErr) {
      console.error('send email bind code:', mailErr);
      return res.status(502).json({ success: false, message: '验证码发送失败，请稍后重试' });
    }

    return res.json({ success: true, message: '验证码已发送，请查收邮箱' });
  } catch (err) {
    console.error('player email send-code:', err);
    return res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/email/verify', requirePlayer, async (req, res) => {
  try {
    const parsed = normalizeEmail(req.body?.email);
    if (parsed.error) {
      return res.status(400).json({ success: false, message: parsed.error });
    }
    const email = parsed.value;
    const code = String(req.body?.code || '').trim();
    if (!/^\d{6}$/.test(code)) {
      return res.status(400).json({ success: false, message: '请输入 6 位验证码' });
    }

    const rowRes = await pool.query(
      `SELECT email, code, expires_at FROM email_bind_codes WHERE user_id = $1`,
      [req.player.userId]
    );
    if (rowRes.rows.length === 0) {
      return res.status(400).json({ success: false, message: '请先获取验证码' });
    }
    const row = rowRes.rows[0];
    if (String(row.email).toLowerCase() !== email) {
      return res.status(400).json({ success: false, message: '邮箱与验证码不匹配，请重新获取' });
    }
    if (new Date(row.expires_at).getTime() < Date.now()) {
      return res.status(400).json({ success: false, message: '验证码已过期，请重新获取' });
    }
    if (String(row.code) !== code) {
      return res.status(400).json({ success: false, message: '验证码错误' });
    }

    const taken = await pool.query(
      `SELECT user_id FROM users
       WHERE LOWER(email) = $1
         AND email_verified_at IS NOT NULL
         AND user_id <> $2
       LIMIT 1`,
      [email, req.player.userId]
    );
    if (taken.rows.length > 0) {
      return res.status(400).json({ success: false, message: '该邮箱已被其他账号绑定' });
    }

    await pool.query(
      `UPDATE users
       SET email = $1, email_verified_at = CURRENT_TIMESTAMP
       WHERE user_id = $2`,
      [email, req.player.userId]
    );
    await pool.query(`DELETE FROM email_bind_codes WHERE user_id = $1`, [req.player.userId]);

    return res.json({
      success: true,
      message: '邮箱绑定成功',
      data: {
        email,
        email_verified: true,
      },
    });
  } catch (err) {
    if (err.code === '23505') {
      return res.status(400).json({ success: false, message: '该邮箱已被其他账号绑定' });
    }
    console.error('player email verify:', err);
    return res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/email/unbind', requirePlayer, async (req, res) => {
  try {
    await pool.query(
      `UPDATE users SET email = NULL, email_verified_at = NULL WHERE user_id = $1`,
      [req.player.userId]
    );
    await pool.query(`DELETE FROM email_bind_codes WHERE user_id = $1`, [req.player.userId]);
    return res.json({ success: true, message: '已解除邮箱绑定' });
  } catch (err) {
    console.error('player email unbind:', err);
    return res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
