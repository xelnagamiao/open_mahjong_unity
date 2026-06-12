const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const config = require('../../config/config');
const { writeAudit } = require('../../utils/audit');

const GAME_SERVER_BASE_URL = config.calcServer.baseUrl.replace(/\/$/, '');
const GAME_SERVER_TIMEOUT_MS = config.calcServer.timeoutMs;

const MAX_TITLE_LEN = 64;
const MAX_CONTENT_LEN = 2000;

function validateMessageInput(body) {
  const title = (body.title || '').trim();
  const content = (body.content || '').trim();
  if (!title) return { error: '请输入消息标题' };
  if (!content) return { error: '请输入消息内容' };
  if (title.length > MAX_TITLE_LEN) {
    return { error: `标题不能超过 ${MAX_TITLE_LEN} 字` };
  }
  if (content.length > MAX_CONTENT_LEN) {
    return { error: `内容不能超过 ${MAX_CONTENT_LEN} 字` };
  }
  return { title, content };
}

async function proxyToGameServer(path, body) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), GAME_SERVER_TIMEOUT_MS);
  try {
    const resp = await fetch(`${GAME_SERVER_BASE_URL}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      signal: controller.signal,
    });
    const text = await resp.text();
    let data;
    try {
      data = text ? JSON.parse(text) : {};
    } catch (_) {
      data = { detail: text };
    }
    return { status: resp.status, data };
  } finally {
    clearTimeout(timer);
  }
}

router.post('/broadcast', async (req, res) => {
  try {
    const parsed = validateMessageInput(req.body);
    if (parsed.error) {
      return res.status(400).json({ success: false, message: parsed.error });
    }

    const { status, data } = await proxyToGameServer('/admin/message/broadcast', {
      title: parsed.title,
      content: parsed.content,
    });

    if (status >= 400) {
      return res.status(status >= 500 ? 502 : status).json({
        success: false,
        message: data.detail || data.message || '游戏服务器返回错误',
      });
    }

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'message.broadcast',
      targetType: 'broadcast',
      payload: { title: parsed.title, content: parsed.content, result: data },
    });

    return res.json({
      success: true,
      data,
      message: `已向 ${data.sent} 名在线玩家发送消息`,
    });
  } catch (err) {
    console.error('admin message broadcast:', err);
    return res.status(502).json({ success: false, message: '无法连接到游戏服务器' });
  }
});

router.post('/user', async (req, res) => {
  try {
    const userId = parseInt(req.body.user_id, 10);
    if (Number.isNaN(userId) || userId <= 0) {
      return res.status(400).json({ success: false, message: '无效的用户 ID' });
    }

    const parsed = validateMessageInput(req.body);
    if (parsed.error) {
      return res.status(400).json({ success: false, message: parsed.error });
    }

    const userResult = await pool.query(
      'SELECT user_id, username FROM users WHERE user_id = $1',
      [userId]
    );
    if (userResult.rows.length === 0) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }

    const { status, data } = await proxyToGameServer('/admin/message/user', {
      user_id: userId,
      title: parsed.title,
      content: parsed.content,
    });

    if (status >= 400) {
      const msg =
        status === 404
          ? '用户当前不在线'
          : data.detail || data.message || '游戏服务器返回错误';
      return res.status(status === 404 ? 404 : status >= 500 ? 502 : status).json({
        success: false,
        message: msg,
      });
    }

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'message.user',
      targetType: 'user',
      targetId: userId,
      payload: { title: parsed.title, content: parsed.content, result: data },
    });

    return res.json({
      success: true,
      data: {
        ...data,
        username: data.username || userResult.rows[0].username,
      },
      message: `已向用户 ${userId} 发送消息`,
    });
  } catch (err) {
    console.error('admin message user:', err);
    return res.status(502).json({ success: false, message: '无法连接到游戏服务器' });
  }
});

module.exports = router;
