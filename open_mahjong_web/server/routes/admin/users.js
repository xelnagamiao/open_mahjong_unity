const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const config = require('../../config/config');
const { hashPassword } = require('../../utils/password');
const { writeAudit } = require('../../utils/audit');
const { fetchUserStatsBundle } = require('../../services/playerStats');
const { normalizeUsername, validateUsername } = require('../../utils/username');
const { getAccountGeography } = require('../../services/accountGeography');
const { RENAME_CARD_COLUMNS } = require('../../services/renameCards');
const { grantSponsorTitles } = require('../../utils/titleTables');

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

const { BAN_TYPES, ACTIVE_BAN } = require('../../utils/accountBans');
const { LADDER_PASS_COLUMNS: LADDER_PASS_FIELDS } = require('../../utils/userLadderPass');
const USER_LIST_COLUMNS = `u.user_id, u.username, u.email, u.email_verified_at, u.is_tourist, u.sponsor_expires_at, u.sponsor_amount, u.admin_note,
                ${LADDER_PASS_FIELDS.map((field) => `u.${field}`).join(', ')},
                u.ban_expires_at, u.ban_type, u.ban_reason,
                u.created_at`;
const USER_DETAIL_COLUMNS = `user_id, username, is_tourist, email, email_verified_at, ${RENAME_CARD_COLUMNS}, sponsor_expires_at, sponsor_amount, admin_note, has_special_settings,
              ${LADDER_PASS_FIELDS.join(', ')},
              ban_expires_at, ban_type, ban_reason, created_at`;
const USER_PATCH_SELECT_COLUMNS = `user_id, username, sponsor_expires_at, sponsor_amount, admin_note, has_special_settings,
              ${LADDER_PASS_FIELDS.join(', ')},
              ban_expires_at, ban_type, ban_reason`;
const SPONSOR_PLAYER_CONDITION = '(u.sponsor_expires_at IS NOT NULL OR COALESCE(u.sponsor_amount, 0) <> 0)';

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
    return { fields: {} };
  }

  if (ban_type === null || ban_type === '') {
    return { fields: { ban_type: null, ban_expires_at: null, ban_reason: null } };
  }

  const type = String(ban_type).trim();
  if (!BAN_TYPES.includes(type)) {
    return { error: '无效的封禁类型' };
  }

  const fields = { ban_type: type };

  if (hasBanExpires) {
    const parsed = parseBanExpiresAt(ban_expires_at);
    if (parsed?.error) {
      return { error: parsed.error };
    }
    fields.ban_expires_at = parsed.value;
  }

  if (hasBanReason) {
    const reason = String(ban_reason || '').trim();
    fields.ban_reason = reason || null;
  }

  return { fields };
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

    const isNumericId = /^\d+$/.test(q) && BigInt(q) <= 9223372036854775807n;
    let result;
    if (isNumericId) {
      result = await pool.query(
        `SELECT ${USER_LIST_COLUMNS},
                EXISTS(SELECT 1 FROM game_player_records g WHERE g.user_id = u.user_id) AS has_game_records
         FROM users u
         WHERE u.user_id = $1
         LIMIT 1`,
        [q]
      );
    }
    if (result?.rows.length > 0) {
      // 先确认 UID 是否存在再分页，避免后续空页错误回退为名称搜索。
      result.rows = result.rows.slice(offset, offset + limit);
    } else {
      result = await pool.query(
        `SELECT ${USER_LIST_COLUMNS},
                EXISTS(SELECT 1 FROM game_player_records g WHERE g.user_id = u.user_id) AS has_game_records
         FROM users u
         WHERE u.username ILIKE $1 OR u.email ILIKE $1
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

router.get('/sponsors', async (req, res) => {
  const { group = 'sponsor' } = req.query;
  if (group !== 'sponsor' && group !== 'special') {
    return res.status(400).json({ success: false, message: '玩家分组无效' });
  }
  const q = typeof req.query.q === 'string' ? req.query.q.trim() : '';
  const page = Math.max(1, parseInt(req.query.page, 10) || 1);
  const limit = Math.min(100, Math.max(1, parseInt(req.query.limit, 10) || 20));
  const where = [group === 'sponsor' ? SPONSOR_PLAYER_CONDITION : 'u.has_special_settings'];
  const params = [];
  if (q) {
    params.push(q);
    const queryParam = `$${params.length}`;
    where.push(`(u.user_id::text = ${queryParam} OR u.username ILIKE '%' || ${queryParam} || '%' OR COALESCE(u.email, '') ILIKE '%' || ${queryParam} || '%')`);
  }
  const condition = where.join(' AND ');
  try {
    const count = await pool.query(
      `SELECT COUNT(*) FILTER (WHERE ${condition})::int AS total,
              COUNT(*) FILTER (WHERE ${SPONSOR_PLAYER_CONDITION})::int AS sponsor,
              COUNT(*) FILTER (WHERE u.has_special_settings)::int AS special,
              COUNT(*) FILTER (WHERE ${SPONSOR_PLAYER_CONDITION} AND u.has_special_settings)::int AS overlap
       FROM users u WHERE ${SPONSOR_PLAYER_CONDITION} OR u.has_special_settings`, params
    );
    const { total, ...totals } = count.rows[0];
    const totalPages = Math.max(1, Math.ceil(total / limit));
    const currentPage = Math.min(page, totalPages);
    const offset = (currentPage - 1) * limit;
    const result = await pool.query(
      `SELECT ${USER_LIST_COLUMNS}, u.has_special_settings, ${SPONSOR_PLAYER_CONDITION} AS has_sponsorship,
              EXISTS(SELECT 1 FROM game_player_records g WHERE g.user_id = u.user_id) AS has_game_records
       FROM users u
       WHERE ${condition}
       ORDER BY (COALESCE(u.sponsor_amount, 0) <> 0) DESC,
                u.sponsor_expires_at DESC NULLS LAST,
                u.user_id DESC
       LIMIT $${params.length + 1} OFFSET $${params.length + 2}`,
      [...params, limit, offset]
    );
    res.json({ success: true, data: { items: result.rows, total, totals, page: currentPage, limit } });
  } catch (err) {
    console.error('admin sponsor list:', err);
    res.status(500).json({ success: false, message: '读取玩家分组失败' });
  }
});

router.patch('/sponsors/permissions', async (req, res) => {
  const {
    is_beginner_qualified,
    is_intermediate_qualified,
    is_advanced_qualified,
    is_mcrpl_qualified,
    sync_special_settings = false,
    reason,
  } = req.body || {};
  const permissions = {
    is_beginner_qualified,
    is_intermediate_qualified,
    is_advanced_qualified,
    is_mcrpl_qualified,
  };
  if (!reason || !String(reason).trim()) {
    return res.status(400).json({ success: false, message: '请填写变更原因' });
  }
  if (Object.values(permissions).some((value) => typeof value !== 'boolean')) {
    return res.status(400).json({ success: false, message: '请完整设置赞助玩家的入场特许' });
  }
  if (typeof sync_special_settings !== 'boolean') {
    return res.status(400).json({ success: false, message: '同步特殊设定玩家必须为布尔值' });
  }

  let client;
  try {
    client = await pool.connect();
    await client.query('BEGIN');
    const result = await client.query(
      `UPDATE users AS u
       SET ${LADDER_PASS_FIELDS.map((field, index) => `${field} = $${index + 1}`).join(', ')}
       WHERE ${SPONSOR_PLAYER_CONDITION} AND ($${LADDER_PASS_FIELDS.length + 1}::boolean OR NOT u.has_special_settings)`,
      [...LADDER_PASS_FIELDS.map((field) => permissions[field]), sync_special_settings]
    );
    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'sponsor_players.permissions.update',
      targetType: 'sponsor_players',
      payload: { affected_count: result.rowCount, permissions, sync_special_settings },
      reason: String(reason).trim(),
    }, client);
    await client.query('COMMIT');
    res.json({ success: true, data: { affected_count: result.rowCount, permissions } });
  } catch (err) {
    try {
      await client.query('ROLLBACK');
    } catch (_) {
      // 保留原始错误并交给统一错误响应。
    }
    console.error('admin sponsor permissions patch:', err);
    res.status(500).json({ success: false, message: '统一更新赞助玩家权限失败' });
  } finally {
    client?.release();
  }
});

router.get('/bans', async (req, res) => {
  const { type = '', status = 'active', q = '' } = req.query;
  if ((type && !BAN_TYPES.includes(type)) || !['active', 'expired', 'all'].includes(status) || typeof q !== 'string') {
    return res.status(400).json({ success: false, message: '名单筛选条件无效' });
  }
  const page = Math.max(1, parseInt(req.query.page, 10) || 1);
  const limit = Math.min(100, Math.max(1, parseInt(req.query.limit, 10) || 20));
  const where = [`ban_type = ANY($1::text[])`];
  const params = [type ? [type] : BAN_TYPES];
  if (status !== 'all') where.push(status === 'active' ? ACTIVE_BAN : `NOT ${ACTIVE_BAN}`);
  if (q.trim()) {
    where.push("(user_id::text = $2 OR strpos(lower(username), lower($2)) > 0)");
    params.push(q.trim());
  }
  const from = `FROM users WHERE ${where.join(' AND ')}`;
  try {
    const count = await pool.query(`SELECT count(*)::int AS total ${from}`, params);
    const total = count.rows[0].total;
    const currentPage = Math.min(page, Math.max(1, Math.ceil(total / limit)));
    const result = await pool.query(
      `SELECT user_id, username, admin_note, ban_type, ban_reason, ban_expires_at, ${ACTIVE_BAN} AS active
       ${from} ORDER BY ban_expires_at DESC NULLS FIRST, user_id DESC
       LIMIT $${params.length + 1} OFFSET $${params.length + 2}`,
      [...params, limit, (currentPage - 1) * limit]
    );
    res.json({ success: true, data: { items: result.rows, total, page: currentPage, limit } });
  } catch (err) {
    console.error('admin account ban list:', err);
    res.status(500).json({ success: false, message: '读取封禁名单失败' });
  }
});

router.get('/geography', async (req, res) => {
  try {
    res.json({ success: true, data: await getAccountGeography().distribution() });
  } catch (err) {
    console.error('admin account geography:', err.message);
    res.status(503).json({ success: false, message: '暂时无法读取账号分布，请稍后重试' });
  }
});

router.get('/:userId', async (req, res) => {
  try {
    const userId = parseInt(req.params.userId, 10);
    if (Number.isNaN(userId)) {
      return res.status(400).json({ success: false, message: '无效的用户 ID' });
    }

    const userResult = await pool.query(
      `SELECT ${USER_DETAIL_COLUMNS}
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
        `SELECT DISTINCT r.game_id, r.created_at,gpr.room_type,gpr.rule,gpr.sub_rule,gpr.match_type
         FROM game_player_records gpr JOIN game_record_tables t USING(game_id)
         JOIN game_replays r ON r.game_id=t.replay_id WHERE gpr.user_id=$1
         ORDER BY r.created_at DESC NULLS LAST,r.game_id DESC
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
      `SELECT COUNT(DISTINCT t.replay_id)::int AS cnt FROM game_player_records
       JOIN game_record_tables t USING(game_id) WHERE user_id = $1`,
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
        recent_login_ips: await Promise.all(loginIpsRes.rows.map(async (row) => ({
          ...row, ...await getAccountGeography().lookup(row.ip_address),
        }))),
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
  let client;
  let connectionError;
  try {
    const userId = parseInt(req.params.userId, 10);
    const {
      username,
      sponsor_expires_at,
      sponsor_amount,
      admin_note,
      has_special_settings,
      is_beginner_qualified,
      is_intermediate_qualified,
      is_advanced_qualified,
      is_mcrpl_qualified,
      ban_expires_at,
      ban_type,
      ban_reason,
      reason,
    } = req.body || {};
    const ladderPassPayload = {
      is_beginner_qualified,
      is_intermediate_qualified,
      is_advanced_qualified,
      is_mcrpl_qualified,
    };
    if (has_special_settings !== undefined && typeof has_special_settings !== 'boolean') {
      return res.status(400).json({ success: false, message: '特殊设定标记必须为布尔值' });
    }
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写变更原因' });
    }

    const updates = [];
    const params = [];
    let idx = 1;

    if (username !== undefined) {
      const name = normalizeUsername(username);
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
    if (sponsor_amount !== undefined) {
      const rawAmount = sponsor_amount === null || sponsor_amount === '' ? '0' : String(sponsor_amount).trim();
      if (!/^\d+(?:\.\d{1,2})?$/.test(rawAmount) || Number(rawAmount) > 9999999999.99) {
        return res.status(400).json({ success: false, message: '赞助金额须为不超过两位小数的非负数字' });
      }
      updates.push(`sponsor_amount = $${idx++}::numeric`);
      params.push(rawAmount);
    }
    if (admin_note !== undefined) {
      const note = admin_note === null ? '' : String(admin_note);
      if (note.length > 2000) {
        return res.status(400).json({ success: false, message: '玩家注释最多 2000 字' });
      }
      updates.push(`admin_note = $${idx++}`);
      params.push(note);
    }
    for (const field of LADDER_PASS_FIELDS) {
      if (ladderPassPayload[field] !== undefined) {
        updates.push(`${field} = $${idx++}`);
        params.push(!!ladderPassPayload[field]);
      }
    }

    const banPatch = normalizeBanPayload({ ban_expires_at, ban_type, ban_reason });
    if (banPatch.error) {
      return res.status(400).json({ success: false, message: banPatch.error });
    }
    for (const [column, value] of Object.entries(banPatch.fields)) {
      const cast = column === 'ban_expires_at' ? '::timestamptz::timestamp' : '';
      updates.push(`${column} = $${idx++}${cast}`);
      params.push(value);
    }

    if (updates.length === 0 && has_special_settings === undefined) {
      return res.status(400).json({ success: false, message: '没有可更新的字段' });
    }

    client = await pool.connect();
    await client.query('BEGIN');
    const before = await client.query(
      `SELECT ${USER_PATCH_SELECT_COLUMNS} FROM users WHERE user_id = $1 FOR UPDATE`,
      [userId]
    );
    if (!before.rowCount) {
      await client.query('ROLLBACK');
      return res.status(404).json({ success: false, message: '用户不存在' });
    }
    const player = before.rows[0];
    const expiresAt = sponsor_expires_at === undefined ? player.sponsor_expires_at : sponsor_expires_at;
    const hasSponsorship = (expiresAt != null && expiresAt !== '')
      || Number(sponsor_amount === undefined ? player.sponsor_amount : sponsor_amount) !== 0;
    const changedPass = LADDER_PASS_FIELDS.some(field =>
      ladderPassPayload[field] !== undefined && Boolean(ladderPassPayload[field]) !== Boolean(player[field])
    );
    const hasPass = LADDER_PASS_FIELDS.some(field =>
      Boolean(ladderPassPayload[field] === undefined ? player[field] : ladderPassPayload[field])
    );
    if (!hasSponsorship && hasPass && has_special_settings === false) {
      await client.query('ROLLBACK');
      return res.status(400).json({ success: false, message: '非赞助玩家须先清空入场特许才能取消特殊设定' });
    }
    const special = has_special_settings ?? (player.has_special_settings || changedPass || (!hasSponsorship && hasPass));
    if (has_special_settings !== undefined || special !== player.has_special_settings) {
      updates.push(`has_special_settings = $${idx++}`);
      params.push(special);
    }
    params.push(userId);
    const after = await client.query(
      `UPDATE users SET ${updates.join(', ')} WHERE user_id = $${idx} RETURNING ${USER_PATCH_SELECT_COLUMNS}`,
      params
    );

    const sponsorAmount = after.rows[0].sponsor_amount;
    const grantedSponsorTitles = await grantSponsorTitles(
      client,
      req.admin.userId,
      userId,
      sponsorAmount,
      '根据赞助金额自动授予头衔'
    );
    for (const grant of grantedSponsorTitles) {
      await writeAudit({
        adminUserId: req.admin.userId,
        action: 'title.grant',
        targetType: 'user',
        targetId: userId,
        payload: { title_id: grant.title_id, grant, automatic: true },
        reason: '根据赞助金额自动授予头衔',
      }, client);
    }

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'user.update',
      targetType: 'user',
      targetId: userId,
      payload: { before: before.rows[0], after: after.rows[0] },
      reason: String(reason).trim(),
    }, client);
    await client.query('COMMIT');

    res.json({ success: true, data: after.rows[0] });
  } catch (err) {
    if (client) {
      try {
        await client.query('ROLLBACK');
      } catch (rollbackError) {
        connectionError = rollbackError;
      }
    }
    if (err.code === '23505') {
      return res.status(409).json({ success: false, message: '用户名已存在' });
    }
    console.error('admin user patch:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  } finally {
    client?.release(connectionError);
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
    const { new_username, sync_history = false, reason } = req.body || {};
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
    const newName = normalizeUsername(new_username);

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

    const client = await pool.connect();
    let syncedRecordCount = 0;
    try {
      await client.query('BEGIN');
      await client.query(`UPDATE users SET username = $1 WHERE user_id = $2`, [newName, userId]);
      if (sync_history) {
        const records = await client.query(
          `UPDATE game_player_records SET username = $1 WHERE user_id = $2`,
          [newName, userId]
        );
        syncedRecordCount = records.rowCount;
      }
      await client.query('COMMIT');
    } catch (err) {
      await client.query('ROLLBACK');
      throw err;
    } finally {
      client.release();
    }

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
        synced_history: !!sync_history,
        synced_game_player_records: syncedRecordCount,
      },
      reason: String(reason).trim(),
    });

    res.json({
      success: true,
      data: { user_id: userId, username: newName, synced_online: syncedOnline, synced_history: !!sync_history, synced_game_player_records: syncedRecordCount },
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

router.post('/:userId/history-username', async (req, res) => {
  try {
    const userId = parseInt(req.params.userId, 10);
    const { old_username, new_username, reason } = req.body || {};
    const oldName = normalizeUsername(old_username);
    const newName = normalizeUsername(new_username);
    if (!Number.isSafeInteger(userId) || !oldName || !newName || oldName === newName || !String(reason || '').trim()) {
      return res.status(400).json({ success: false, message: '请填写原历史用户名、新用户名和变更原因' });
    }
    const newNameError = validateUsername(newName);
    if (newNameError) return res.status(400).json({ success: false, message: newNameError });

    const client = await pool.connect();
    let updatedRecords = 0;
    try {
      await client.query('BEGIN');
      const user = await client.query(`SELECT user_id FROM users WHERE user_id = $1 FOR UPDATE`, [userId]);
      if (!user.rows.length) {
        await client.query('ROLLBACK');
        return res.status(404).json({ success: false, message: '用户不存在' });
      }
      const records = await client.query(
        `UPDATE game_player_records SET username = $1 WHERE user_id = $2 AND username = $3`,
        [newName, userId, oldName]
      );
      updatedRecords = records.rowCount;
      await client.query('COMMIT');
    } catch (err) {
      await client.query('ROLLBACK');
      throw err;
    } finally {
      client.release();
    }
    await writeAudit({ adminUserId: req.admin.userId, action: 'user.rename_history_username', targetType: 'user', targetId: userId,
      payload: { old_username: oldName, new_username: newName, updated_game_player_records: updatedRecords }, reason: String(reason).trim() });
    res.json({ success: true, data: { user_id: userId, old_username: oldName, new_username: newName, updated_game_player_records: updatedRecords } });
  } catch (err) {
    console.error('admin history username rename:', err);
    res.status(500).json({ success: false, message: '历史牌谱用户名迁移失败，已回滚' });
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
