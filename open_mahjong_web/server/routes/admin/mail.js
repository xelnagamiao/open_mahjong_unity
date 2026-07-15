const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const config = require('../../config/config');
const { writeAudit } = require('../../utils/audit');
const { sendMail } = require('../../utils/mailer');

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const MAX_SUBJECT_LEN = 200;
const MAX_BODY_LEN = 10000;

function normalizeEmail(raw) {
  const email = String(raw || '').trim().toLowerCase();
  if (!email) return { error: '请填写收件邮箱' };
  if (email.length > 255) return { error: '邮箱过长' };
  if (!EMAIL_RE.test(email)) return { error: '邮箱格式不正确' };
  return { value: email };
}

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function textToHtml(text) {
  return `<div style="white-space:pre-wrap;font-family:sans-serif;line-height:1.6;">${escapeHtml(text)}</div>`;
}

router.get('/status', (req, res) => {
  const smtp = config.smtp;
  return res.json({
    success: true,
    data: {
      enabled: !!smtp.enabled,
      fromEmail: smtp.enabled ? smtp.fromEmail : null,
      fromName: smtp.enabled ? smtp.fromName : null,
      host: smtp.enabled ? smtp.host : null,
    },
  });
});

router.post('/send', async (req, res) => {
  try {
    if (!config.smtp.enabled) {
      return res.status(503).json({ success: false, message: '邮件服务未配置，请联系管理员' });
    }

    const subject = String(req.body?.subject || '').trim();
    const body = String(req.body?.body || '').trim();
    if (!subject) {
      return res.status(400).json({ success: false, message: '请输入邮件主题' });
    }
    if (!body) {
      return res.status(400).json({ success: false, message: '请输入邮件正文' });
    }
    if (subject.length > MAX_SUBJECT_LEN) {
      return res.status(400).json({ success: false, message: `主题不能超过 ${MAX_SUBJECT_LEN} 字` });
    }
    if (body.length > MAX_BODY_LEN) {
      return res.status(400).json({ success: false, message: `正文不能超过 ${MAX_BODY_LEN} 字` });
    }

    let to = null;
    let targetUserId = null;
    let targetUsername = null;

    const userIdRaw = req.body?.user_id;
    if (userIdRaw !== undefined && userIdRaw !== null && String(userIdRaw).trim() !== '') {
      const userId = parseInt(userIdRaw, 10);
      if (Number.isNaN(userId) || userId <= 0) {
        return res.status(400).json({ success: false, message: '无效的用户 ID' });
      }
      const userRes = await pool.query(
        `SELECT user_id, username, email, email_verified_at
         FROM users WHERE user_id = $1`,
        [userId]
      );
      if (userRes.rows.length === 0) {
        return res.status(404).json({ success: false, message: '用户不存在' });
      }
      const user = userRes.rows[0];
      if (!user.email || !user.email_verified_at) {
        return res.status(400).json({
          success: false,
          message: '该用户尚未绑定已验证邮箱',
        });
      }
      to = String(user.email).trim().toLowerCase();
      targetUserId = user.user_id;
      targetUsername = user.username;
    } else {
      const parsed = normalizeEmail(req.body?.to);
      if (parsed.error) {
        return res.status(400).json({ success: false, message: parsed.error });
      }
      to = parsed.value;
    }

    const html = textToHtml(body);
    try {
      await sendMail({ to, subject, text: body, html });
    } catch (mailErr) {
      console.error('admin mail send:', mailErr);
      const msg =
        mailErr.code === 'SMTP_DISABLED'
          ? '邮件服务未配置'
          : mailErr.response || mailErr.message || '邮件发送失败';
      return res.status(502).json({ success: false, message: String(msg) });
    }

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'mail.send',
      targetType: targetUserId ? 'user' : 'email',
      targetId: targetUserId || null,
      payload: {
        to,
        subject,
        body_len: body.length,
        username: targetUsername || undefined,
      },
    });

    return res.json({
      success: true,
      data: {
        to,
        subject,
        user_id: targetUserId,
        username: targetUsername,
      },
      message: `邮件已发送至 ${to}`,
    });
  } catch (err) {
    console.error('admin mail send:', err);
    return res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
