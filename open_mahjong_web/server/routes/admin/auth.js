const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const config = require('../../config/config');
const { verifyPassword } = require('../../utils/password');
const { signToken } = require('../../utils/jwt');
const { requireAdmin } = require('../../middleware/requireAdmin');
const { writeAudit } = require('../../utils/audit');

router.post('/login', async (req, res) => {
  try {
    const { username, password } = req.body || {};
    if (!username || !password) {
      return res.status(400).json({ success: false, message: '请输入用户名和密码' });
    }

    const result = await pool.query(
      `SELECT user_id, username, password, is_tourist FROM users WHERE username = $1`,
      [username.trim()]
    );
    if (result.rows.length === 0) {
      return res.status(401).json({ success: false, message: '用户名或密码错误' });
    }

    const user = result.rows[0];
    if (user.is_tourist) {
      return res.status(403).json({ success: false, message: '游客账号不能登录管理后台' });
    }

    if (!config.admin.userIds.has(Number(user.user_id))) {
      return res.status(403).json({ success: false, message: '该账号无管理权限' });
    }

    if (!verifyPassword(password, user.password)) {
      return res.status(401).json({ success: false, message: '用户名或密码错误' });
    }

    const token = signToken(
      { user_id: user.user_id, username: user.username },
      config.admin.jwtSecret,
      config.admin.jwtExpiresSec
    );

    await writeAudit({
      adminUserId: user.user_id,
      action: 'auth.login',
      targetType: 'admin',
      targetId: user.user_id,
    });

    return res.json({
      success: true,
      data: {
        token,
        user_id: user.user_id,
        username: user.username,
        expires_in: config.admin.jwtExpiresSec,
      },
    });
  } catch (err) {
    console.error('admin login error:', err);
    return res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/me', requireAdmin, (req, res) => {
  res.json({
    success: true,
    data: {
      user_id: req.admin.userId,
      username: req.admin.username,
    },
  });
});

module.exports = router;
