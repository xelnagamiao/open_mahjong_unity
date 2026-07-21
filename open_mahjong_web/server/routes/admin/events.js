const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const config = require('../../config/config');
const { writeAudit } = require('../../utils/audit');
const { generateEventId } = require('../../utils/eventsTables');
const { fetchEventPlayerStats } = require('../../services/eventPlayerStats');

const MAX_EVENT_ADMINS = 10;

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

function normalizeName(name) {
  const text = String(name || '').trim();
  if (!text) return { error: '请填写赛事名称' };
  if (text.length > 128) return { error: '赛事名称过长（最多 128 字）' };
  return { value: text };
}

function parseUserId(value, label = '用户 ID') {
  const id = parseInt(value, 10);
  if (Number.isNaN(id) || id <= 0) {
    return { error: `无效的${label}` };
  }
  return { value: id };
}

async function fetchEventRow(eventId) {
  const result = await pool.query(
    `SELECT event_id, name, description, status, reopen_requested, created_by, closed_at, created_at, updated_at
     FROM events WHERE event_id = $1`,
    [eventId]
  );
  return result.rows[0] || null;
}

async function fetchEventAdmins(eventId) {
  const result = await pool.query(
    `SELECT ea.event_id, ea.user_id, ea.role, ea.added_by, ea.created_at,
            u.username, u.is_tourist
     FROM event_admins ea
     LEFT JOIN users u ON u.user_id = ea.user_id
     WHERE ea.event_id = $1
     ORDER BY CASE ea.role WHEN 'owner' THEN 0 ELSE 1 END, ea.created_at ASC`,
    [eventId]
  );
  return result.rows;
}

async function resolveRegisteredUser(userId) {
  const result = await pool.query(
    `SELECT user_id, username, is_tourist FROM users WHERE user_id = $1`,
    [userId]
  );
  if (result.rows.length === 0) {
    return { error: '用户不存在' };
  }
  const user = result.rows[0];
  if (user.is_tourist) {
    return { error: '不能将游客设为赛事子管理员' };
  }
  return { user };
}

async function countEventRecords(eventId) {
  const result = await pool.query(
    `SELECT COUNT(DISTINCT game_id)::int AS cnt
     FROM game_player_records
     WHERE event_id = $1`,
    [eventId]
  );
  return result.rows[0]?.cnt || 0;
}

router.get('/', async (req, res) => {
  try {
    const page = Math.max(1, parseInt(req.query.page, 10) || 1);
    const limit = Math.min(100, Math.max(1, parseInt(req.query.limit, 10) || 50));
    const offset = (page - 1) * limit;
    const status = (req.query.status || '').trim();
    const reopenRequested = ['1', 'true', 'yes'].includes(
      String(req.query.reopen_requested || '').trim().toLowerCase()
    );

    const conditions = [];
    const params = [];
    let idx = 1;
    if (status === 'active' || status === 'closed' || status === 'registered') {
      conditions.push(`e.status = $${idx++}`);
      params.push(status);
    }
    if (reopenRequested) {
      conditions.push(`e.reopen_requested = TRUE`);
    }
    const where = conditions.length ? `WHERE ${conditions.join(' AND ')}` : '';

    const listRes = await pool.query(
      `SELECT e.event_id, e.name, e.description, e.status, e.reopen_requested, e.created_by, e.closed_at, e.created_at, e.updated_at,
              owner.user_id AS owner_user_id,
              owner_u.username AS owner_username,
              (
                SELECT COUNT(*)::int FROM event_admins ea2
                WHERE ea2.event_id = e.event_id AND ea2.role = 'admin'
              ) AS admin_count,
              (
                SELECT COUNT(DISTINCT gpr.game_id)::int FROM game_player_records gpr
                WHERE gpr.event_id = e.event_id
              ) AS record_count
       FROM events e
       LEFT JOIN event_admins owner
         ON owner.event_id = e.event_id AND owner.role = 'owner'
       LEFT JOIN users owner_u ON owner_u.user_id = owner.user_id
       ${where}
       ORDER BY e.created_at DESC
       LIMIT $${idx++} OFFSET $${idx}`,
      [...params, limit, offset]
    );

    const countRes = await pool.query(
      `SELECT COUNT(*)::int AS cnt FROM events e ${where}`,
      params
    );

    res.json({
      success: true,
      data: {
        items: listRes.rows,
        page,
        limit,
        total: countRes.rows[0].cnt,
      },
    });
  } catch (err) {
    console.error('admin events list:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/', async (req, res) => {
  const client = await pool.connect();
  try {
    const { name, description, owner_user_id, reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写操作原因' });
    }
    const nameParsed = normalizeName(name);
    if (nameParsed.error) {
      return res.status(400).json({ success: false, message: nameParsed.error });
    }
    const descriptionText = String(description || '').trim();
    if (descriptionText.length > 2000) {
      return res.status(400).json({ success: false, message: '赛事介绍过长（最多 2000 字）' });
    }

    let ownerId = null;
    if (owner_user_id !== undefined && owner_user_id !== null && owner_user_id !== '') {
      const parsed = parseUserId(owner_user_id, '赛事主管理员用户 ID');
      if (parsed.error) {
        return res.status(400).json({ success: false, message: parsed.error });
      }
      const resolved = await resolveRegisteredUser(parsed.value);
      if (resolved.error) {
        return res.status(400).json({ success: false, message: resolved.error });
      }
      ownerId = parsed.value;
    }

    let eventId = generateEventId();
    for (let attempt = 0; attempt < 5; attempt += 1) {
      const exists = await client.query(
        `SELECT 1 FROM events WHERE event_id = $1`,
        [eventId]
      );
      if (exists.rows.length === 0) break;
      eventId = generateEventId();
    }

    await client.query('BEGIN');
    await client.query(
      `INSERT INTO events (event_id, name, description, status, created_by)
       VALUES ($1, $2, $3, 'registered', $4)`,
      [eventId, nameParsed.value, descriptionText, req.admin.userId]
    );
    if (ownerId != null) {
      await client.query(
        `INSERT INTO event_admins (event_id, user_id, role, added_by)
         VALUES ($1, $2, 'owner', $3)`,
        [eventId, ownerId, req.admin.userId]
      );
    }
    await client.query('COMMIT');

    const event = await fetchEventRow(eventId);
    const admins = await fetchEventAdmins(eventId);

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'event.create',
      targetType: 'event',
      targetId: eventId,
      payload: { event, owner_user_id: ownerId },
      reason: String(reason).trim(),
    });

    res.json({ success: true, data: { ...event, admins } });
  } catch (err) {
    try {
      await client.query('ROLLBACK');
    } catch (_) {
      /* ignore */
    }
    console.error('admin events create:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  } finally {
    client.release();
  }
});

router.get('/:eventId', async (req, res) => {
  try {
    const event = await fetchEventRow(req.params.eventId);
    if (!event) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }
    const admins = await fetchEventAdmins(event.event_id);
    const recordCount = await countEventRecords(event.event_id);
    res.json({
      success: true,
      data: { ...event, admins, record_count: recordCount },
    });
  } catch (err) {
    console.error('admin events detail:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/:eventId/close', async (req, res) => {
  try {
    const { reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写操作原因' });
    }
    const event = await fetchEventRow(req.params.eventId);
    if (!event) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }
    if (event.status !== 'active') {
      return res.status(400).json({
        success: false,
        message: event.status === 'closed' ? '赛事已关闭' : '仅已开启的赛事可以关闭',
      });
    }

    const result = await pool.query(
      `UPDATE events
       SET status = 'closed',
           closed_at = CURRENT_TIMESTAMP,
           reopen_requested = FALSE,
           updated_at = CURRENT_TIMESTAMP
       WHERE event_id = $1
       RETURNING event_id, name, description, status, reopen_requested, created_by, closed_at, created_at, updated_at`,
      [event.event_id]
    );

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'event.close',
      targetType: 'event',
      targetId: event.event_id,
      payload: { before: event, after: result.rows[0] },
      reason: String(reason).trim(),
    });

    res.json({ success: true, data: result.rows[0] });
  } catch (err) {
    console.error('admin events close:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/:eventId/activate', async (req, res) => {
  try {
    const { reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写操作原因' });
    }
    const event = await fetchEventRow(req.params.eventId);
    if (!event) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }
    if (event.status === 'active') {
      return res.status(400).json({ success: false, message: '赛事已处于开启状态' });
    }
    if (event.status === 'closed' && !event.reopen_requested) {
      return res.status(400).json({
        success: false,
        message: '关闭的赛事需先由赛事主管理员申请重新开启',
      });
    }

    const result = await pool.query(
      `UPDATE events
       SET status = 'active',
           closed_at = NULL,
           reopen_requested = FALSE,
           updated_at = CURRENT_TIMESTAMP
       WHERE event_id = $1
       RETURNING event_id, name, description, status, reopen_requested, created_by, closed_at, created_at, updated_at`,
      [event.event_id]
    );

    await writeAudit({
      adminUserId: req.admin.userId,
      action: event.status === 'closed' ? 'event.reopen.approve' : 'event.activate',
      targetType: 'event',
      targetId: event.event_id,
      payload: { before: event, after: result.rows[0] },
      reason: String(reason).trim(),
    });

    res.json({ success: true, data: result.rows[0] });
  } catch (err) {
    console.error('admin events activate:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/:eventId/reject-reopen', async (req, res) => {
  try {
    const { reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写操作原因' });
    }
    const event = await fetchEventRow(req.params.eventId);
    if (!event) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }
    if (event.status !== 'closed' || !event.reopen_requested) {
      return res.status(400).json({ success: false, message: '当前没有待审核的重新开启申请' });
    }

    const result = await pool.query(
      `UPDATE events
       SET reopen_requested = FALSE,
           updated_at = CURRENT_TIMESTAMP
       WHERE event_id = $1
       RETURNING event_id, name, description, status, reopen_requested, created_by, closed_at, created_at, updated_at`,
      [event.event_id]
    );

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'event.reopen.reject',
      targetType: 'event',
      targetId: event.event_id,
      payload: { before: event, after: result.rows[0] },
      reason: String(reason).trim(),
    });

    res.json({ success: true, data: result.rows[0] });
  } catch (err) {
    console.error('admin events reject-reopen:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.delete('/:eventId', async (req, res) => {
  const client = await pool.connect();
  try {
    const { reason, confirm_name } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写删除原因' });
    }
    const event = await fetchEventRow(req.params.eventId);
    if (!event) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }
    if (String(confirm_name || '').trim() !== event.name) {
      return res.status(400).json({
        success: false,
        message: '请输入完整赛事名称以确认删除',
      });
    }

    const gamesRes = await client.query(
      `SELECT DISTINCT game_id FROM game_player_records WHERE event_id = $1`,
      [event.event_id]
    );
    const gameIds = gamesRes.rows.map((r) => r.game_id);

    await client.query('BEGIN');
    if (gameIds.length > 0) {
      await client.query(
        `DELETE FROM game_player_metrics WHERE game_id = ANY($1::varchar[])`,
        [gameIds]
      );
      await client.query(
        `DELETE FROM game_records WHERE game_id = ANY($1::varchar[])`,
        [gameIds]
      );
    }
    await client.query(`DELETE FROM events WHERE event_id = $1`, [event.event_id]);
    await client.query('COMMIT');

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'event.delete',
      targetType: 'event',
      targetId: event.event_id,
      payload: {
        before: event,
        deleted_game_count: gameIds.length,
        deleted_game_ids: gameIds.slice(0, 100),
      },
      reason: String(reason).trim(),
    });

    res.json({
      success: true,
      message: `赛事已删除，并级联删除 ${gameIds.length} 局牌谱`,
      data: { deleted_game_count: gameIds.length },
    });
  } catch (err) {
    try {
      await client.query('ROLLBACK');
    } catch (_) {
      /* ignore */
    }
    console.error('admin events delete:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  } finally {
    client.release();
  }
});

router.get('/:eventId/admins', async (req, res) => {
  try {
    const event = await fetchEventRow(req.params.eventId);
    if (!event) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }
    const admins = await fetchEventAdmins(event.event_id);
    res.json({ success: true, data: { items: admins } });
  } catch (err) {
    console.error('admin events admins list:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.put('/:eventId/owner', async (req, res) => {
  const client = await pool.connect();
  try {
    const { user_id, reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写操作原因' });
    }
    const parsed = parseUserId(user_id, '赛事主管理员用户 ID');
    if (parsed.error) {
      return res.status(400).json({ success: false, message: parsed.error });
    }
    const resolved = await resolveRegisteredUser(parsed.value);
    if (resolved.error) {
      return res.status(400).json({ success: false, message: resolved.error });
    }

    const event = await fetchEventRow(req.params.eventId);
    if (!event) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }

    const beforeAdmins = await fetchEventAdmins(event.event_id);

    await client.query('BEGIN');
    // 原赛事主管理员降为赛事子管理员（若存在且不是同一人）
    await client.query(
      `UPDATE event_admins SET role = 'admin'
       WHERE event_id = $1 AND role = 'owner' AND user_id <> $2`,
      [event.event_id, parsed.value]
    );
    // 最终赛事子管理员 = 当前 admin 且不是新赛事主管理员（新主管理员下一步会升为 owner）
    const adminCountRes = await client.query(
      `SELECT COUNT(*)::int AS cnt FROM event_admins
       WHERE event_id = $1 AND role = 'admin' AND user_id <> $2`,
      [event.event_id, parsed.value]
    );
    if (adminCountRes.rows[0].cnt > MAX_EVENT_ADMINS) {
      await client.query('ROLLBACK');
      return res.status(400).json({
        success: false,
        message: `赛事子管理员已达上限 ${MAX_EVENT_ADMINS}，请先移除后再更换赛事主管理员`,
      });
    }

    await client.query(
      `INSERT INTO event_admins (event_id, user_id, role, added_by)
       VALUES ($1, $2, 'owner', $3)
       ON CONFLICT (event_id, user_id) DO UPDATE SET
         role = 'owner',
         added_by = EXCLUDED.added_by`,
      [event.event_id, parsed.value, req.admin.userId]
    );
    await client.query(
      `UPDATE events SET updated_at = CURRENT_TIMESTAMP WHERE event_id = $1`,
      [event.event_id]
    );
    await client.query('COMMIT');

    const admins = await fetchEventAdmins(event.event_id);
    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'event.owner.set',
      targetType: 'event',
      targetId: event.event_id,
      payload: {
        owner_user_id: parsed.value,
        before: beforeAdmins,
        after: admins,
      },
      reason: String(reason).trim(),
    });

    res.json({ success: true, data: { admins } });
  } catch (err) {
    try {
      await client.query('ROLLBACK');
    } catch (_) {
      /* ignore */
    }
    console.error('admin events set owner:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  } finally {
    client.release();
  }
});

router.post('/:eventId/admins', async (req, res) => {
  try {
    const { user_id, reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写操作原因' });
    }
    const parsed = parseUserId(user_id, '管理员用户 ID');
    if (parsed.error) {
      return res.status(400).json({ success: false, message: parsed.error });
    }
    const resolved = await resolveRegisteredUser(parsed.value);
    if (resolved.error) {
      return res.status(400).json({ success: false, message: resolved.error });
    }

    const event = await fetchEventRow(req.params.eventId);
    if (!event) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }

    const existing = await pool.query(
      `SELECT role FROM event_admins WHERE event_id = $1 AND user_id = $2`,
      [event.event_id, parsed.value]
    );
    if (existing.rows.length > 0) {
      return res.status(400).json({
        success: false,
        message: existing.rows[0].role === 'owner'
          ? '该用户已是赛事主管理员'
          : '该用户已是赛事子管理员',
      });
    }

    const countRes = await pool.query(
      `SELECT COUNT(*)::int AS cnt FROM event_admins
       WHERE event_id = $1 AND role = 'admin'`,
      [event.event_id]
    );
    if (countRes.rows[0].cnt >= MAX_EVENT_ADMINS) {
      return res.status(400).json({
        success: false,
        message: `赛事子管理员最多 ${MAX_EVENT_ADMINS} 名`,
      });
    }

    await pool.query(
      `INSERT INTO event_admins (event_id, user_id, role, added_by)
       VALUES ($1, $2, 'admin', $3)`,
      [event.event_id, parsed.value, req.admin.userId]
    );
    await pool.query(
      `UPDATE events SET updated_at = CURRENT_TIMESTAMP WHERE event_id = $1`,
      [event.event_id]
    );

    const admins = await fetchEventAdmins(event.event_id);
    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'event.admin.add',
      targetType: 'event',
      targetId: event.event_id,
      payload: { user_id: parsed.value, username: resolved.user.username },
      reason: String(reason).trim(),
    });

    res.json({ success: true, data: { admins } });
  } catch (err) {
    console.error('admin events add admin:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.delete('/:eventId/admins/:userId', async (req, res) => {
  try {
    const { reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写操作原因' });
    }
    const parsed = parseUserId(req.params.userId, '管理员用户 ID');
    if (parsed.error) {
      return res.status(400).json({ success: false, message: parsed.error });
    }

    const event = await fetchEventRow(req.params.eventId);
    if (!event) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }

    const existing = await pool.query(
      `SELECT role FROM event_admins WHERE event_id = $1 AND user_id = $2`,
      [event.event_id, parsed.value]
    );
    if (existing.rows.length === 0) {
      return res.status(404).json({ success: false, message: '该用户不是本赛事子管理员' });
    }
    if (existing.rows[0].role === 'owner') {
      return res.status(400).json({
        success: false,
        message: '不能直接删除赛事主管理员，请先更换赛事主管理员',
      });
    }

    await pool.query(
      `DELETE FROM event_admins WHERE event_id = $1 AND user_id = $2`,
      [event.event_id, parsed.value]
    );
    await pool.query(
      `UPDATE events SET updated_at = CURRENT_TIMESTAMP WHERE event_id = $1`,
      [event.event_id]
    );

    const admins = await fetchEventAdmins(event.event_id);
    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'event.admin.remove',
      targetType: 'event',
      targetId: event.event_id,
      payload: { user_id: parsed.value, role: existing.rows[0].role },
      reason: String(reason).trim(),
    });

    res.json({ success: true, data: { admins } });
  } catch (err) {
    console.error('admin events remove admin:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

// 赛事空房间：创建（代理游戏服）
router.post('/:eventId/rooms', async (req, res) => {
  try {
    const event = await fetchEventRow(req.params.eventId);
    if (!event) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }
    if (event.status !== 'active') {
      return res.status(400).json({
        success: false,
        message: event.status === 'registered' ? '赛事尚未开启，无法创建房间' : '赛事已关闭，无法创建房间',
      });
    }

    const { room_rule, room_config, password, reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写操作原因' });
    }
    if (!room_rule || !String(room_rule).trim()) {
      return res.status(400).json({ success: false, message: '请选择房间规则' });
    }

    const { status, data } = await proxyToGameServer('/admin/event/rooms/create', {
      event_id: event.event_id,
      room_rule: String(room_rule).trim(),
      room_config: room_config || {},
      password: password || '',
      created_by: req.admin.userId,
    });
    if (status >= 400) {
      return res.status(status).json({
        success: false,
        message: data?.detail || data?.message || '创建房间失败',
      });
    }

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'event.room.create',
      targetType: 'event',
      targetId: event.event_id,
      payload: {
        room_rule: String(room_rule).trim(),
        room_config: room_config || {},
        room_info: data?.room_info || null,
      },
      reason: String(reason).trim(),
    });

    res.json({ success: true, data });
  } catch (err) {
    console.error('admin events create room:', err);
    res.status(500).json({ success: false, message: '游戏服不可达' });
  }
});

// 赛事空房间：列表（代理游戏服）
router.get('/:eventId/rooms', async (req, res) => {
  try {
    const event = await fetchEventRow(req.params.eventId);
    if (!event) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }
    const qs = new URLSearchParams({ event_id: event.event_id }).toString();
    const { status, data } = await proxyToGameServer(
      `/admin/event/rooms?${qs}`,
      null,
      'GET'
    );
    if (status >= 400) {
      return res.status(status).json({
        success: false,
        message: data?.detail || data?.message || '获取房间列表失败',
      });
    }
    res.json({ success: true, data: { items: data?.items || [] } });
  } catch (err) {
    console.error('admin events list rooms:', err);
    res.status(500).json({ success: false, message: '游戏服不可达' });
  }
});

// 赛事空房间：删除（代理游戏服）
router.delete('/:eventId/rooms/:roomId', async (req, res) => {
  try {
    const event = await fetchEventRow(req.params.eventId);
    if (!event) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }
    const { reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写操作原因' });
    }

    const roomId = String(req.params.roomId || '').trim();
    if (!roomId) {
      return res.status(400).json({ success: false, message: '缺少 room_id' });
    }

    const qs = new URLSearchParams({ event_id: event.event_id }).toString();
    const { status, data } = await proxyToGameServer(
      `/admin/event/rooms/${encodeURIComponent(roomId)}?${qs}`,
      null,
      'DELETE'
    );
    if (status >= 400) {
      return res.status(status).json({
        success: false,
        message: data?.detail || data?.message || '删除房间失败',
      });
    }

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'event.room.delete',
      targetType: 'event',
      targetId: event.event_id,
      payload: { room_id: roomId },
      reason: String(reason).trim(),
    });

    res.json({ success: true, message: data?.message || '房间已删除' });
  } catch (err) {
    console.error('admin events delete room:', err);
    res.status(500).json({ success: false, message: '游戏服不可达' });
  }
});

/** 赛事玩家顺位统计（与数据站 rank-stats 口径一致） */
router.get('/:eventId/player-stats', async (req, res) => {
  try {
    const event = await fetchEventRow(req.params.eventId);
    if (!event) {
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }
    const data = await fetchEventPlayerStats(event.event_id, {
      rule: req.query.rule || null,
      sub_rule: req.query.sub_rule || null,
      game_type: req.query.game_type || null,
      date_from: req.query.date_from || null,
      date_to: req.query.date_to || null,
      q: req.query.q || null,
    });
    res.json({ success: true, data });
  } catch (err) {
    console.error('admin events player-stats:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
