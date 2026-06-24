const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const config = require('../../config/config');
const { hashPassword } = require('../../utils/password');
const { writeAudit } = require('../../utils/audit');
const { fetchUserStatsBundle } = require('../../services/playerStats');
const { validateUsername } = require('../../utils/username');

const GAME_SERVER_BASE_URL = config.calcServer.baseUrl.replace(/\/$/, '');
const GAME_SERVER_TIMEOUT_MS = config.calcServer.timeoutMs;

async function proxyToGameServer(path, body, method = 'POST') {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), GAME_SERVER_TIMEOUT_MS);
  try {
    const resp = await fetch(`${GAME_SERVER_BASE_URL}${path}`, {
      method,
      headers: body ? { 'Content-Type': 'application/json' } : undefined,
      body: body ? JSON.stringify(body) : undefined,
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

async function fetchUserOnline(userId) {
  const { status, data } = await proxyToGameServer(`/admin/user/${userId}/online`, null, 'GET');
  if (status >= 400) {
    return { online: false };
  }
  return { online: !!data.online, username: data.username || null };
}

const BAN_TYPES = new Set(['login', 'chat', 'match', 'full']);

function parseBanExpiresAt(value) {
  if (value === null || value === undefined || value === '') {
    return { value: null };
  }
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return { error: '封禁到期时间格式无效' };
  }
  return { value: parsed.toISOString() };
}

function normalizeBanPayload({ ban_expires_at, ban_type, ban_reason }) {
  const hasBanType = ban_type !== undefined;
  const hasBanExpires = ban_expires_at !== undefined;
  const hasBanReason = ban_reason !== undefined;

  if (!hasBanType && !hasBanExpires && !hasBanReason) {
    return { updates: [] };
  }

  if (ban_type === null || ban_type === '') {
    return {
      updates: [
        'ban_type = NULL',
        'ban_expires_at = NULL',
        'ban_reason = NULL',
      ],
      params: [],
    };
  }

  const type = String(ban_type).trim();
  if (!BAN_TYPES.has(type)) {
    return { error: '无效的封禁类型' };
  }

  const updates = [`ban_type = $IDX_TYPE`];
  const params = [type];
  let idx = 2;

  if (hasBanExpires) {
    const parsed = parseBanExpiresAt(ban_expires_at);
    if (parsed?.error) {
      return { error: parsed.error };
    }
    if (parsed.value === null) {
      updates.push('ban_expires_at = NULL');
    } else {
      updates.push(`ban_expires_at = $${idx++}`);
      params.push(parsed.value);
    }
  }

  if (hasBanReason) {
    const reason = String(ban_reason || '').trim();
    updates.push(`ban_reason = $${idx++}`);
    params.push(reason || null);
  }

  return {
    updates: updates.map((line, i) => (i === 0 ? `ban_type = $1` : line)),
    params,
  };
}

router.get('/search', async (req, res) => {
  try {
    const q = (req.query.q || '').trim();
    const page = Math.max(1, parseInt(req.query.page, 10) || 1);
    const limit = Math.min(50, Math.max(1, parseInt(req.query.limit, 10) || 20));
    const offset = (page - 1) * limit;

    if (!q) {
      return res.status(400).json({ success: false, message: '请输入搜索关键词' });
    }

    const userId = parseInt(q, 10);
    let result;
    if (!Number.isNaN(userId)) {
      result = await pool.query(
        `SELECT u.user_id, u.username, u.is_tourist, u.sponsor_expires_at, u.is_mcrpl_qualified,
                u.ban_expires_at, u.ban_type, u.ban_reason,
                u.created_at,
                EXISTS(SELECT 1 FROM game_player_records g WHERE g.user_id = u.user_id) AS has_game_records
         FROM users u
         WHERE u.user_id = $1
         ORDER BY u.user_id
         LIMIT $2 OFFSET $3`,
        [userId, limit, offset]
      );
    } else {
      result = await pool.query(
        `SELECT u.user_id, u.username, u.is_tourist, u.sponsor_expires_at, u.is_mcrpl_qualified,
                u.ban_expires_at, u.ban_type, u.ban_reason,
                u.created_at,
                EXISTS(SELECT 1 FROM game_player_records g WHERE g.user_id = u.user_id) AS has_game_records
         FROM users u
         WHERE u.username ILIKE $1
         ORDER BY u.user_id DESC
         LIMIT $2 OFFSET $3`,
        [`%${q}%`, limit, offset]
      );
    }

    res.json({
      success: true,
      data: { items: result.rows, page, limit },
    });
  } catch (err) {
    console.error('admin users search:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/:userId', async (req, res) => {
  try {
    const userId = parseInt(req.params.userId, 10);
    if (Number.isNaN(userId)) {
      return res.status(400).json({ success: false, message: '无效的用户 ID' });
    }

    const userResult = await pool.query(
      `SELECT user_id, username, is_tourist, sponsor_expires_at, is_mcrpl_qualified,
              ban_expires_at, ban_type, ban_reason, created_at
       FROM users WHERE user_id = $1`,
      [userId]
    );
    if (userResult.rows.length === 0) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }

    const user = userResult.rows[0];

    const [settingsRes, configRes, rankRes, recordsRes, loginIpsRes, stats, onlineStatus] = await Promise.all([
      pool.query(
        `SELECT title_id, profile_image_id, character_id, voice_id FROM user_settings WHERE user_id = $1`,
        [userId]
      ),
      pool.query(`SELECT volume FROM user_config WHERE user_id = $1`, [userId]),
      pool.query(
        `SELECT guobiao_rank, guobiao_score, updated_at FROM rank_data WHERE user_id = $1`,
        [userId]
      ),
      pool.query(
        `SELECT DISTINCT gpr.game_id, gr.created_at, gpr.room_type, gpr.rule, gpr.sub_rule, gpr.match_type
         FROM game_player_records gpr
         JOIN game_records gr ON gr.game_id = gpr.game_id
         WHERE gpr.user_id = $1
         ORDER BY gpr.game_id DESC
         LIMIT 10`,
        [userId]
      ),
      pool.query(
        `SELECT ip_address, logged_at
         FROM user_login_ips
         WHERE user_id = $1
         ORDER BY logged_at DESC, id DESC
         LIMIT 20`,
        [userId]
      ),
      fetchUserStatsBundle(userId),
      fetchUserOnline(userId).catch(() => ({ online: false })),
    ]);

    const recordCount = await pool.query(
      `SELECT COUNT(DISTINCT game_id)::int AS cnt FROM game_player_records WHERE user_id = $1`,
      [userId]
    );

    res.json({
      success: true,
      data: {
        user,
        user_settings: settingsRes.rows[0] || null,
        user_config: configRes.rows[0] || null,
        rank_data: rankRes.rows[0] || null,
        recent_games: recordsRes.rows,
        recent_login_ips: loginIpsRes.rows,
        online: onlineStatus.online,
        game_record_count: recordCount.rows[0].cnt,
        ...stats,
      },
    });
  } catch (err) {
    console.error('admin user detail:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.patch('/:userId', async (req, res) => {
  try {
    const userId = parseInt(req.params.userId, 10);
    const { username, sponsor_expires_at, is_mcrpl_qualified, ban_expires_at, ban_type, ban_reason, reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写变更原因' });
    }

    const before = await pool.query(
      `SELECT user_id, username, sponsor_expires_at, is_mcrpl_qualified,
              ban_expires_at, ban_type, ban_reason
       FROM users WHERE user_id = $1`,
      [userId]
    );
    if (before.rows.length === 0) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }

    const updates = [];
    const params = [];
    let idx = 1;

    if (username !== undefined) {
      const name = String(username).trim();
      const usernameError = validateUsername(name);
      if (usernameError) {
        return res.status(400).json({ success: false, message: usernameError });
      }
      updates.push(`username = $${idx++}`);
      params.push(name);
    }
    if (sponsor_expires_at !== undefined) {
      if (sponsor_expires_at === null || sponsor_expires_at === '') {
        updates.push(`sponsor_expires_at = NULL`);
      } else {
        const parsed = new Date(sponsor_expires_at);
        if (Number.isNaN(parsed.getTime())) {
          return res.status(400).json({ success: false, message: '赞助到期时间格式无效' });
        }
        updates.push(`sponsor_expires_at = $${idx++}`);
        params.push(parsed.toISOString());
      }
    }
    if (is_mcrpl_qualified !== undefined) {
      updates.push(`is_mcrpl_qualified = $${idx++}`);
      params.push(!!is_mcrpl_qualified);
    }

    const banPatch = normalizeBanPayload({ ban_expires_at, ban_type, ban_reason });
    if (banPatch.error) {
      return res.status(400).json({ success: false, message: banPatch.error });
    }
    if (banPatch.updates?.length) {
      for (const line of banPatch.updates) {
        if (line.includes('$')) {
          const match = line.match(/\$(\d+)/g);
          if (match) {
            const reindexed = line.replace(/\$\d+/g, () => `$${idx++}`);
            updates.push(reindexed);
          } else {
            updates.push(line);
          }
        } else {
          updates.push(line);
        }
      }
      if (banPatch.params?.length) {
        params.push(...banPatch.params);
      }
    }

    if (updates.length === 0) {
      return res.status(400).json({ success: false, message: '没有可更新的字段' });
    }

    params.push(userId);
    await pool.query(
      `UPDATE users SET ${updates.join(', ')} WHERE user_id = $${idx}`,
      params
    );

    const after = await pool.query(
      `SELECT user_id, username, sponsor_expires_at, is_mcrpl_qualified,
              ban_expires_at, ban_type, ban_reason
       FROM users WHERE user_id = $1`,
      [userId]
    );

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'user.update',
      targetType: 'user',
      targetId: userId,
      payload: { before: before.rows[0], after: after.rows[0] },
      reason: String(reason).trim(),
    });

    res.json({ success: true, data: after.rows[0] });
  } catch (err) {
    if (err.code === '23505') {
      return res.status(409).json({ success: false, message: '用户名已存在' });
    }
    console.error('admin user patch:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/:userId/reset-password', async (req, res) => {
  try {
    const userId = parseInt(req.params.userId, 10);
    const { new_password, reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写变更原因' });
    }
    if (!new_password || String(new_password).length < 6) {
      return res.status(400).json({ success: false, message: '新密码至少 6 位' });
    }

    const exists = await pool.query(`SELECT user_id FROM users WHERE user_id = $1`, [userId]);
    if (exists.rows.length === 0) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }

    const hashed = hashPassword(String(new_password));
    await pool.query(`UPDATE users SET password = $1 WHERE user_id = $2`, [hashed, userId]);

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'user.reset_password',
      targetType: 'user',
      targetId: userId,
      reason: String(reason).trim(),
    });

    res.json({ success: true, message: '密码已重置' });
  } catch (err) {
    console.error('admin reset password:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.delete('/:userId', async (req, res) => {
  try {
    const userId = parseInt(req.params.userId, 10);
    const { reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写删除原因' });
    }

    const userRes = await pool.query(
      `SELECT user_id, username, is_tourist, password FROM users WHERE user_id = $1`,
      [userId]
    );
    if (userRes.rows.length === 0) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }
    const user = userRes.rows[0];

    if (!user.is_tourist) {
      return res.status(403).json({ success: false, message: '仅允许删除游客账号' });
    }
    if (!String(user.username).includes('游客')) {
      return res.status(403).json({ success: false, message: '用户名须包含「游客」' });
    }
    if (userId < 9000000 || userId > 9900000) {
      return res.status(403).json({ success: false, message: '用户 ID 不在游客范围内' });
    }

    const rec = await pool.query(
      `SELECT COUNT(*)::int AS cnt FROM game_player_records WHERE user_id = $1`,
      [userId]
    );
    if (rec.rows[0].cnt > 0) {
      return res.status(409).json({
        success: false,
        message: `该游客有 ${rec.rows[0].cnt} 条牌谱记录，无法删除`,
      });
    }

    await pool.query(`DELETE FROM users WHERE user_id = $1`, [userId]);

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'user.delete_tourist',
      targetType: 'user',
      targetId: userId,
      payload: { username: user.username },
      reason: String(reason).trim(),
    });

    res.json({ success: true, message: '游客账号已删除' });
  } catch (err) {
    console.error('admin delete user:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/:userId/rename', async (req, res) => {
  try {
    const userId = parseInt(req.params.userId, 10);
    const { new_username, reason } = req.body || {};
    if (Number.isNaN(userId)) {
      return res.status(400).json({ success: false, message: '无效的用户 ID' });
    }
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写变更原因' });
    }
    const usernameError = validateUsername(new_username);
    if (usernameError) {
      return res.status(400).json({ success: false, message: usernameError });
    }
    const newName = String(new_username).trim();

    const before = await pool.query(
      `SELECT user_id, username, is_tourist FROM users WHERE user_id = $1`,
      [userId]
    );
    if (before.rows.length === 0) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }
    const user = before.rows[0];
    if (user.is_tourist) {
      return res.status(403).json({ success: false, message: '游客账号不支持改名' });
    }
    if (user.username === newName) {
      return res.status(400).json({ success: false, message: '新用户名与当前相同' });
    }

    const dup = await pool.query(
      `SELECT user_id FROM users WHERE username = $1 AND user_id <> $2`,
      [newName, userId]
    );
    if (dup.rows.length > 0) {
      return res.status(409).json({ success: false, message: '用户名已存在' });
    }

    await pool.query(`UPDATE users SET username = $1 WHERE user_id = $2`, [newName, userId]);

    let syncedOnline = false;
    try {
      const { status, data } = await proxyToGameServer('/admin/user/sync-username', {
        user_id: userId,
        username: newName,
      });
      syncedOnline = status < 400 && !!data?.online;
    } catch (_) {
      /* 游戏服不可达时仍视为改名成功 */
    }

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'user.rename',
      targetType: 'user',
      targetId: userId,
      payload: {
        before: { username: user.username },
        after: { username: newName },
        synced_online: syncedOnline,
      },
      reason: String(reason).trim(),
    });

    res.json({
      success: true,
      data: { user_id: userId, username: newName, synced_online: syncedOnline },
      message: syncedOnline
        ? '改名成功，已同步在线会话（玩家需重新登录聊天服）'
        : '改名成功',
    });
  } catch (err) {
    if (err.code === '23505') {
      return res.status(409).json({ success: false, message: '用户名已存在' });
    }
    console.error('admin user rename:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/:userId/kick', async (req, res) => {
  try {
    const userId = parseInt(req.params.userId, 10);
    const { reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写踢下线原因' });
    }
    if (Number.isNaN(userId)) {
      return res.status(400).json({ success: false, message: '无效的用户 ID' });
    }

    const userResult = await pool.query(
      'SELECT user_id, username FROM users WHERE user_id = $1',
      [userId]
    );
    if (userResult.rows.length === 0) {
      return res.status(404).json({ success: false, message: '用户不存在' });
    }

    const kickReason = String(reason).trim();
    const { status, data } = await proxyToGameServer('/admin/user/kick', {
      user_id: userId,
      reason: kickReason,
    });

    if (status >= 400) {
      const msg = status === 404
        ? '用户当前不在线'
        : data.detail || data.message || '游戏服务器返回错误';
      return res.status(status === 404 ? 404 : status >= 500 ? 502 : status).json({
        success: false,
        message: msg,
      });
    }

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'user.kick',
      targetType: 'user',
      targetId: userId,
      payload: { username: userResult.rows[0].username, result: data },
      reason: kickReason,
    });

    res.json({
      success: true,
      message: `已踢下线用户 ${userResult.rows[0].username}`,
      data,
    });
  } catch (err) {
    console.error('admin user kick:', err);
    res.status(502).json({ success: false, message: '无法连接到游戏服务器' });
  }
});

module.exports = router;
