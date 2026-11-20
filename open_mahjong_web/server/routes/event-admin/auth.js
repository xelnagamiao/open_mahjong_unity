const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const config = require('../../config/config');
const { verifyPassword } = require('../../utils/password');
const { signToken } = require('../../utils/jwt');
const { BAN_COLUMNS, loginBanMessage } = require('../../utils/accountBans');
const { requireEventAdmin } = require('../../middleware/requireEventAdmin');
const { requireActiveAdminAccount } = require('../../middleware/requireActiveAdminAccount');
const { writeAudit } = require('../../utils/audit');
const { listUserEvents } = require('../../utils/eventAdminHelpers');

router.post('/login', async (req, res) => {
  try {
    const { username, password } = req.body || {};
    if (!username || !password) {
      return res.status(400).json({ success: false, message: '请输入用户名和密码' });
    }

    const result = await pool.query(
      `SELECT user_id, username, password, is_tourist, ${BAN_COLUMNS} FROM users WHERE username = $1`,
      [username.trim()]
    );
    if (result.rows.length === 0) {
      return res.status(401).json({ success: false, message: '用户名或密码错误' });
    }

    const user = result.rows[0];
    if (user.is_tourist) {
      return res.status(403).json({ success: false, message: '游客账号不能登录比赛管理后台' });
    }

    if (!verifyPassword(password, user.password)) {
      return res.status(401).json({ success: false, message: '用户名或密码错误' });
    }
    const banMessage = loginBanMessage(user);
    if (banMessage) return res.status(403).json({ success: false, message: banMessage });

    const events = await listUserEvents(user.user_id);
    if (events.length === 0) {
      return res.status(403).json({
        success: false,
        message: '该账号不是任何赛事的管理员',
      });
    }

    const token = signToken(
      {
        user_id: user.user_id,
        username: user.username,
        aud: config.eventAdmin.audience,
      },
      config.eventAdmin.jwtSecret,
      config.eventAdmin.jwtExpiresSec
    );

    await writeAudit({
      adminUserId: user.user_id,
      action: 'event_admin.auth.login',
      targetType: 'event_admin',
      targetId: user.user_id,
    });

    return res.json({
      success: true,
      data: {
        token,
        user_id: user.user_id,
        username: user.username,
        expires_in: config.eventAdmin.jwtExpiresSec,
        events,
      },
    });
  } catch (err) {
    console.error('event-admin login error:', err);
    return res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/me', requireEventAdmin, requireActiveAdminAccount, async (req, res) => {
  try {
    const events = await listUserEvents(req.eventAdmin.userId);
    res.json({
      success: true,
      data: {
        user_id: req.eventAdmin.userId,
        username: req.eventAdmin.username,
        events,
      },
    });
  } catch (err) {
    console.error('event-admin me error:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
