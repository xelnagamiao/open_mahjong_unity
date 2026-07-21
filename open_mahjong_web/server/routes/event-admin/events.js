const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const config = require('../../config/config');
const { writeAudit } = require('../../utils/audit');
const {
  requireEventMembership,
  requireEventOwner,
} = require('../../middleware/requireEventAdmin');
const { listUserEvents } = require('../../utils/eventAdminHelpers');
const {
  fetchEventPlayerStats,
  GAME_TYPE_MATCH_TYPES,
} = require('../../services/eventPlayerStats');

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

function parseUserId(value, label = '\u7528\u6237 ID') {
  const id = parseInt(value, 10);
  if (Number.isNaN(id) || id <= 0) {
    return { error: `\u65e0\u6548\u7684${label}` };
  }
  return { value: id };
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
    return { error: '\u7528\u6237\u4e0d\u5b58\u5728' };
  }
  const user = result.rows[0];
  if (user.is_tourist) {
    return { error: '\u4e0d\u80fd\u5c06\u6e38\u5ba2\u8bbe\u4e3a\u8d5b\u4e8b\u5b50\u7ba1\u7406\u5458' };
  }
  return { user };
}

async function countEventRecords(eventId) {
  const result = await pool.query(
    `SELECT COUNT(DISTINCT game_id)::int AS cnt
     FROM game_player_records WHERE event_id = $1`,
    [eventId]
  );
  return result.rows[0]?.cnt || 0;
}

router.get('/', async (req, res) => {
  try {
    const events = await listUserEvents(req.eventAdmin.userId);
    res.json({ success: true, data: { items: events } });
  } catch (err) {
    console.error('event-admin events list:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

router.get('/:eventId', requireEventMembership, async (req, res) => {
  try {
    const admins = await fetchEventAdmins(req.event.event_id);
    const recordCount = await countEventRecords(req.event.event_id);
    res.json({
      success: true,
      data: {
        ...req.event,
        my_role: req.eventRole,
        admins,
        record_count: recordCount,
      },
    });
  } catch (err) {
    console.error('event-admin event detail:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

router.post('/:eventId/close', requireEventMembership, requireEventOwner, async (req, res) => {
  try {
    const { reason } = req.body || {};
    if (req.event.status !== 'active') {
      return res.status(400).json({
        success: false,
        message: req.event.status === 'closed'
          ? '\u8d5b\u4e8b\u5df2\u5173\u95ed'
          : '\u4ec5\u5df2\u5f00\u542f\u7684\u8d5b\u4e8b\u53ef\u4ee5\u5173\u95ed',
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
      [req.event.event_id]
    );

    await writeAudit({
      adminUserId: req.eventAdmin.userId,
      action: 'event_admin.event.close',
      targetType: 'event',
      targetId: req.event.event_id,
      payload: { before: req.event, after: result.rows[0] },
      reason: String(reason || '').trim(),
    });

    res.json({ success: true, data: result.rows[0] });
  } catch (err) {
    console.error('event-admin close:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

router.post('/:eventId/open', requireEventMembership, requireEventOwner, async (req, res) => {
  try {
    const { reason } = req.body || {};
    if (req.event.status !== 'registered') {
      return res.status(400).json({
        success: false,
        message:
          req.event.status === 'active'
            ? '\u8d5b\u4e8b\u5df2\u5904\u4e8e\u5f00\u542f\u72b6\u6001'
            : '\u5df2\u5173\u95ed\u7684\u8d5b\u4e8b\u9700\u7533\u8bf7\u91cd\u65b0\u5f00\u542f\uff0c\u5e76\u7531\u5e73\u53f0\u7ba1\u7406\u5458\u5ba1\u6838',
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
      [req.event.event_id]
    );

    await writeAudit({
      adminUserId: req.eventAdmin.userId,
      action: 'event_admin.event.open',
      targetType: 'event',
      targetId: req.event.event_id,
      payload: { before: req.event, after: result.rows[0] },
      reason: String(reason || '').trim(),
    });

    res.json({ success: true, data: result.rows[0] });
  } catch (err) {
    console.error('event-admin open:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

router.post('/:eventId/request-reopen', requireEventMembership, requireEventOwner, async (req, res) => {
  try {
    const { reason } = req.body || {};
    if (req.event.status !== 'closed') {
      return res.status(400).json({ success: false, message: '\u4ec5\u5df2\u5173\u95ed\u7684\u8d5b\u4e8b\u53ef\u7533\u8bf7\u91cd\u65b0\u5f00\u542f' });
    }
    if (req.event.reopen_requested) {
      return res.status(400).json({ success: false, message: '\u5df2\u63d0\u4ea4\u91cd\u65b0\u5f00\u542f\u7533\u8bf7\uff0c\u8bf7\u7b49\u5f85\u5e73\u53f0\u7ba1\u7406\u5458\u5ba1\u6838' });
    }

    const result = await pool.query(
      `UPDATE events
       SET reopen_requested = TRUE,
           updated_at = CURRENT_TIMESTAMP
       WHERE event_id = $1
       RETURNING event_id, name, description, status, reopen_requested, created_by, closed_at, created_at, updated_at`,
      [req.event.event_id]
    );

    await writeAudit({
      adminUserId: req.eventAdmin.userId,
      action: 'event_admin.event.request_reopen',
      targetType: 'event',
      targetId: req.event.event_id,
      payload: { before: req.event, after: result.rows[0] },
      reason: String(reason || '').trim(),
    });

    res.json({ success: true, data: result.rows[0], message: '\u5df2\u63d0\u4ea4\u91cd\u65b0\u5f00\u542f\u7533\u8bf7' });
  } catch (err) {
    console.error('event-admin request-reopen:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

router.get('/:eventId/admins', requireEventMembership, async (req, res) => {
  try {
    const admins = await fetchEventAdmins(req.event.event_id);
    res.json({ success: true, data: { items: admins, my_role: req.eventRole } });
  } catch (err) {
    console.error('event-admin admins list:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

router.post('/:eventId/admins', requireEventMembership, requireEventOwner, async (req, res) => {
  try {
    const { user_id, reason } = req.body || {};
    const parsed = parseUserId(user_id, '\u7528\u6237 ID');
    if (parsed.error) {
      return res.status(400).json({ success: false, message: parsed.error });
    }
    const resolved = await resolveRegisteredUser(parsed.value);
    if (resolved.error) {
      return res.status(400).json({ success: false, message: resolved.error });
    }

    const existing = await pool.query(
      `SELECT role FROM event_admins WHERE event_id = $1 AND user_id = $2`,
      [req.event.event_id, parsed.value]
    );
    if (existing.rows.length > 0) {
      return res.status(400).json({
        success: false,
        message: existing.rows[0].role === 'owner'
          ? '\u8be5\u7528\u6237\u5df2\u662f\u8d5b\u4e8b\u4e3b\u7ba1\u7406\u5458'
          : '\u8be5\u7528\u6237\u5df2\u662f\u8d5b\u4e8b\u5b50\u7ba1\u7406\u5458',
      });
    }

    const countRes = await pool.query(
      `SELECT COUNT(*)::int AS cnt FROM event_admins
       WHERE event_id = $1 AND role = 'admin'`,
      [req.event.event_id]
    );
    if (countRes.rows[0].cnt >= MAX_EVENT_ADMINS) {
      return res.status(400).json({
        success: false,
        message: `\u8d5b\u4e8b\u5b50\u7ba1\u7406\u5458\u6700\u591a ${MAX_EVENT_ADMINS} \u540d`,
      });
    }

    await pool.query(
      `INSERT INTO event_admins (event_id, user_id, role, added_by)
       VALUES ($1, $2, 'admin', $3)`,
      [req.event.event_id, parsed.value, req.eventAdmin.userId]
    );
    await pool.query(
      `UPDATE events SET updated_at = CURRENT_TIMESTAMP WHERE event_id = $1`,
      [req.event.event_id]
    );

    const admins = await fetchEventAdmins(req.event.event_id);
    await writeAudit({
      adminUserId: req.eventAdmin.userId,
      action: 'event_admin.admin.add',
      targetType: 'event',
      targetId: req.event.event_id,
      payload: { user_id: parsed.value, username: resolved.user.username },
      reason: String(reason || '').trim(),
    });

    res.json({ success: true, data: { admins } });
  } catch (err) {
    console.error('event-admin add admin:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

router.delete('/:eventId/admins/:userId', requireEventMembership, requireEventOwner, async (req, res) => {
  try {
    const { reason } = req.body || {};
    const parsed = parseUserId(req.params.userId, '\u7528\u6237 ID');
    if (parsed.error) {
      return res.status(400).json({ success: false, message: parsed.error });
    }

    const existing = await pool.query(
      `SELECT role FROM event_admins WHERE event_id = $1 AND user_id = $2`,
      [req.event.event_id, parsed.value]
    );
    if (existing.rows.length === 0) {
      return res.status(404).json({ success: false, message: '\u8be5\u7528\u6237\u4e0d\u662f\u672c\u8d5b\u4e8b\u5b50\u7ba1\u7406\u5458' });
    }
    if (existing.rows[0].role === 'owner') {
      return res.status(400).json({
        success: false,
        message: '\u4e0d\u80fd\u79fb\u9664\u8d5b\u4e8b\u4e3b\u7ba1\u7406\u5458',
      });
    }

    await pool.query(
      `DELETE FROM event_admins WHERE event_id = $1 AND user_id = $2`,
      [req.event.event_id, parsed.value]
    );
    await pool.query(
      `UPDATE events SET updated_at = CURRENT_TIMESTAMP WHERE event_id = $1`,
      [req.event.event_id]
    );

    const admins = await fetchEventAdmins(req.event.event_id);
    await writeAudit({
      adminUserId: req.eventAdmin.userId,
      action: 'event_admin.admin.remove',
      targetType: 'event',
      targetId: req.event.event_id,
      payload: { user_id: parsed.value, role: existing.rows[0].role },
      reason: String(reason || '').trim(),
    });

    res.json({ success: true, data: { admins } });
  } catch (err) {
    console.error('event-admin remove admin:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

router.post('/:eventId/rooms', requireEventMembership, async (req, res) => {
  try {
    if (req.event.status !== 'active') {
      return res.status(400).json({
        success: false,
        message:
          req.event.status === 'registered'
            ? '\u8d5b\u4e8b\u5c1a\u672a\u5f00\u542f\uff0c\u65e0\u6cd5\u521b\u5efa\u623f\u95f4'
            : '\u8d5b\u4e8b\u5df2\u5173\u95ed\uff0c\u65e0\u6cd5\u521b\u5efa\u623f\u95f4',
      });
    }
    const { room_rule, room_config, password, reason } = req.body || {};
    if (!room_rule || !String(room_rule).trim()) {
      return res.status(400).json({ success: false, message: '\u8bf7\u9009\u62e9\u89c4\u5219' });
    }

    const { status, data } = await proxyToGameServer('/admin/event/rooms/create', {
      event_id: req.event.event_id,
      room_rule: String(room_rule).trim(),
      room_config: room_config || {},
      password: password || '',
      created_by: req.eventAdmin.userId,
    });
    if (status >= 400) {
      return res.status(status).json({
        success: false,
        message: data?.detail || data?.message || '\u521b\u5efa\u623f\u95f4\u5931\u8d25',
      });
    }

    await writeAudit({
      adminUserId: req.eventAdmin.userId,
      action: 'event_admin.room.create',
      targetType: 'event',
      targetId: req.event.event_id,
      payload: {
        room_rule: String(room_rule).trim(),
        room_config: room_config || {},
        room_info: data?.room_info || null,
      },
      reason: String(reason || '').trim(),
    });

    res.json({ success: true, data });
  } catch (err) {
    console.error('event-admin create room:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

router.get('/:eventId/rooms', requireEventMembership, async (req, res) => {
  try {
    const qs = new URLSearchParams({ event_id: req.event.event_id }).toString();
    const { status, data } = await proxyToGameServer(
      `/admin/event/rooms?${qs}`,
      null,
      'GET'
    );
    if (status >= 400) {
      return res.status(status).json({
        success: false,
        message: data?.detail || data?.message || '\u83b7\u53d6\u623f\u95f4\u5217\u8868\u5931\u8d25',
      });
    }
    res.json({ success: true, data: { items: data?.items || [] } });
  } catch (err) {
    console.error('event-admin list rooms:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

router.delete('/:eventId/rooms/:roomId', requireEventMembership, async (req, res) => {
  try {
    const { reason } = req.body || {};
    const roomId = String(req.params.roomId || '').trim();
    if (!roomId) {
      return res.status(400).json({ success: false, message: '\u65e0\u6548\u7684\u623f\u95f4 ID' });
    }

    const qs = new URLSearchParams({ event_id: req.event.event_id }).toString();
    const { status, data } = await proxyToGameServer(
      `/admin/event/rooms/${encodeURIComponent(roomId)}?${qs}`,
      null,
      'DELETE'
    );
    if (status >= 400) {
      return res.status(status).json({
        success: false,
        message: data?.detail || data?.message || '\u5220\u9664\u623f\u95f4\u5931\u8d25',
      });
    }

    await writeAudit({
      adminUserId: req.eventAdmin.userId,
      action: 'event_admin.room.delete',
      targetType: 'event',
      targetId: req.event.event_id,
      payload: { room_id: roomId },
      reason: String(reason || '').trim(),
    });

    res.json({ success: true, message: data?.message || '\u623f\u95f4\u5df2\u5220\u9664' });
  } catch (err) {
    console.error('event-admin delete room:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

router.get('/:eventId/games', requireEventMembership, async (req, res) => {
  try {
    const { status, data } = await proxyToGameServer('/admin/game/list', null, 'GET');
    if (status >= 400) {
      return res.status(status).json({
        success: false,
        message: data?.detail || data?.message || '\u83b7\u53d6\u5bf9\u5c40\u5217\u8868\u5931\u8d25',
      });
    }
    const filtered = (data?.items || []).filter((g) => g.event_id === req.event.event_id);
    res.json({ success: true, data: { items: filtered } });
  } catch (err) {
    console.error('event-admin games list:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

async function callGameControl(path, gameId, eventAdmin, actionLabel, eventId) {
  const { status, data } = await proxyToGameServer(path, { gamestate_id: gameId });
  if (status >= 400) {
    return { status, body: { success: false, message: data?.detail || `${actionLabel}\u5931\u8d25` } };
  }
  await writeAudit({
    adminUserId: eventAdmin.userId,
    action: `event_admin.game.${actionLabel}`,
    targetType: 'game',
    targetId: gameId,
    payload: { gamestate_id: gameId, event_id: eventId },
  });
  return { status: 200, body: { success: true, message: data?.message || actionLabel } };
}

async function assertGameBelongsToEvent(gameId, eventId) {
  const { status, data } = await proxyToGameServer('/admin/game/list', null, 'GET');
  if (status >= 400) {
    return { error: data?.detail || '\u83b7\u53d6\u5bf9\u5c40\u5217\u8868\u5931\u8d25' };
  }
  const hit = (data?.items || []).find((g) => g.gamestate_id === gameId);
  if (!hit) {
    return { error: '\u5bf9\u5c40\u4e0d\u5b58\u5728\u6216\u5df2\u7ed3\u675f' };
  }
  if (hit.event_id !== eventId) {
    return { error: '\u5bf9\u5c40\u4e0d\u5c5e\u4e8e\u672c\u8d5b\u4e8b' };
  }
  return { ok: true };
}

router.post('/:eventId/games/:gameId/pause', requireEventMembership, async (req, res) => {
  try {
    const gameId = String(req.params.gameId || '').trim();
    const check = await assertGameBelongsToEvent(gameId, req.event.event_id);
    if (check.error) {
      return res.status(400).json({ success: false, message: check.error });
    }
    const { status, body } = await callGameControl(
      '/admin/game/pause',
      gameId,
      req.eventAdmin,
      'pause',
      req.event.event_id
    );
    res.status(status).json(body);
  } catch (err) {
    console.error('event-admin pause:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

router.post('/:eventId/games/:gameId/resume', requireEventMembership, async (req, res) => {
  try {
    const gameId = String(req.params.gameId || '').trim();
    const check = await assertGameBelongsToEvent(gameId, req.event.event_id);
    if (check.error) {
      return res.status(400).json({ success: false, message: check.error });
    }
    const { status, body } = await callGameControl(
      '/admin/game/resume',
      gameId,
      req.eventAdmin,
      'resume',
      req.event.event_id
    );
    res.status(status).json(body);
  } catch (err) {
    console.error('event-admin resume:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

router.post('/:eventId/games/:gameId/end', requireEventMembership, async (req, res) => {
  try {
    const gameId = String(req.params.gameId || '').trim();
    const check = await assertGameBelongsToEvent(gameId, req.event.event_id);
    if (check.error) {
      return res.status(400).json({ success: false, message: check.error });
    }
    const { status, body } = await callGameControl(
      '/admin/game/end',
      gameId,
      req.eventAdmin,
      'end',
      req.event.event_id
    );
    res.status(status).json(body);
  } catch (err) {
    console.error('event-admin end:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

/** 赛事玩家顺位统计（与数据站 rank-stats 口径一致） */
router.get('/:eventId/player-stats', requireEventMembership, async (req, res) => {
  try {
    const data = await fetchEventPlayerStats(req.event.event_id, {
      rule: req.query.rule || null,
      sub_rule: req.query.sub_rule || null,
      game_type: req.query.game_type || null,
      date_from: req.query.date_from || null,
      date_to: req.query.date_to || null,
      q: req.query.q || null,
    });
    res.json({ success: true, data });
  } catch (err) {
    console.error('event-admin player-stats:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

/** 赛事牌谱筛选条件（与 records 列表一致） */
function buildEventRecordConditions(eventId, query) {
  const params = [eventId];
  const conditions = [
    'gpr.event_id = $1',
    `gpr.room_type = 'events'`,
  ];
  if (query.rule) {
    params.push(String(query.rule));
    conditions.push(`gpr.rule = $${params.length}`);
  }
  if (query.game_type) {
    const mts = GAME_TYPE_MATCH_TYPES[query.game_type];
    if (mts && mts.length) {
      params.push(mts);
      conditions.push(`gpr.match_type = ANY($${params.length}::varchar[])`);
    }
  }
  if (query.user_id) {
    const uid = parseInt(query.user_id, 10);
    if (!Number.isNaN(uid) && uid > 0) {
      params.push(uid);
      conditions.push(`gpr.user_id = $${params.length}`);
    }
  }
  return { params, conditions, whereSql: conditions.join(' AND ') };
}

const DOWNLOAD_MAX_GAMES = 50;

router.get('/:eventId/records', requireEventMembership, async (req, res) => {
  try {
    const page = Math.max(1, parseInt(req.query.page, 10) || 1);
    const limit = Math.min(50, Math.max(1, parseInt(req.query.limit, 10) || 20));
    const offset = (page - 1) * limit;

    const { params, whereSql } = buildEventRecordConditions(req.event.event_id, req.query);
    const limitIdx = params.length + 1;
    const offsetIdx = params.length + 2;
    params.push(limit, offset);

    const listRes2 = await pool.query(
      `SELECT gr.game_id, gr.created_at,
              MAX(gpr.rule) AS rule,
              MAX(gpr.sub_rule) AS sub_rule,
              MAX(gpr.match_type) AS match_type,
              MAX(gpr.room_type) AS room_type,
              MAX(gpr.event_id) AS event_id
       FROM game_records gr
       JOIN game_player_records gpr ON gpr.game_id = gr.game_id
       WHERE ${whereSql}
       GROUP BY gr.game_id, gr.created_at
       ORDER BY gr.created_at DESC
       LIMIT $${limitIdx} OFFSET $${offsetIdx}`,
      params
    );

    const countParams = params.slice(0, -2);
    const countRes = await pool.query(
      `SELECT COUNT(DISTINCT gpr.game_id)::int AS cnt
       FROM game_player_records gpr
       WHERE ${whereSql}`,
      countParams
    );

    const gameIds = listRes2.rows.map((r) => r.game_id);
    let playersByGame = new Map();
    if (gameIds.length > 0) {
      const playersRes = await pool.query(
        `SELECT game_id, user_id, username, score, rank
         FROM game_player_records
         WHERE game_id = ANY($1::varchar[])
         ORDER BY rank`,
        [gameIds]
      );
      for (const row of playersRes.rows) {
        if (!playersByGame.has(row.game_id)) playersByGame.set(row.game_id, []);
        playersByGame.get(row.game_id).push(row);
      }
    }

    const eventName = req.event.name || null;
    const items = listRes2.rows.map((row) => ({
      game_id: row.game_id,
      created_at: row.created_at,
      rule: row.rule,
      sub_rule: row.sub_rule,
      match_type: row.match_type,
      room_type: row.room_type || 'events',
      event_id: row.event_id || req.event.event_id,
      event_name: eventName,
      players: playersByGame.get(row.game_id) || [],
    }));

    res.json({
      success: true,
      data: {
        items,
        page,
        limit,
        total: countRes.rows[0].cnt,
      },
    });
  } catch (err) {
    console.error('event-admin records:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

/** 单局牌谱下载（须属于本赛事） */
router.get('/:eventId/record/:gameId', requireEventMembership, async (req, res) => {
  try {
    const gameId = String(req.params.gameId || '').trim();
    if (!gameId) {
      return res.status(400).json({ success: false, message: '\u65e0\u6548\u7684 game_id' });
    }
    const owned = await pool.query(
      `SELECT 1 FROM game_player_records
       WHERE game_id = $1 AND event_id = $2 AND room_type = 'events'
       LIMIT 1`,
      [gameId, req.event.event_id]
    );
    if (owned.rows.length === 0) {
      return res.status(404).json({ success: false, message: '\u724c\u8c31\u4e0d\u5b58\u5728' });
    }
    const result = await pool.query(
      `SELECT record FROM game_records WHERE game_id = $1`,
      [gameId]
    );
    if (result.rows.length === 0) {
      return res.status(404).json({ success: false, message: '\u724c\u8c31\u4e0d\u5b58\u5728' });
    }
    const raw = result.rows[0].record;
    const body = typeof raw === 'string' ? raw : JSON.stringify(raw);
    res.setHeader('Content-Type', 'application/json; charset=utf-8');
    res.setHeader('Content-Disposition', `attachment; filename="${gameId}.json"`);
    res.send(body);
  } catch (err) {
    console.error('event-admin record download:', err);
    res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
  }
});

/** 批量牌谱 ZIP：指定 game_ids 或当前筛选全部 */
router.post('/:eventId/records/download', requireEventMembership, async (req, res) => {
  try {
    const eventId = req.event.event_id;
    const requestedIds = Array.isArray(req.body?.game_ids)
      ? req.body.game_ids.map(String).filter(Boolean)
      : [];

    let gameIds = [];
    if (requestedIds.length > 0) {
      const idResult = await pool.query(
        `SELECT DISTINCT game_id FROM game_player_records
         WHERE event_id = $1 AND room_type = 'events' AND game_id = ANY($2::varchar[])`,
        [eventId, requestedIds]
      );
      gameIds = idResult.rows.map((r) => r.game_id);
      if (gameIds.length > DOWNLOAD_MAX_GAMES) {
        return res.status(400).json({
          success: false,
          message: `\u5355\u6b21\u6700\u591a\u4e0b\u8f7d ${DOWNLOAD_MAX_GAMES} \u5c40\uff0c\u5f53\u524d\u9009\u4e2d ${gameIds.length} \u5c40\uff0c\u8bf7\u51cf\u5c11\u9009\u62e9`,
        });
      }
    } else {
      const { params, whereSql } = buildEventRecordConditions(eventId, {
        rule: req.body?.rule || null,
        game_type: req.body?.game_type || null,
        user_id: req.body?.user_id || null,
      });
      const idResult = await pool.query(
        `SELECT game_id FROM (
           SELECT DISTINCT gpr.game_id, gr.created_at
           FROM game_player_records gpr
           JOIN game_records gr ON gr.game_id = gpr.game_id
           WHERE ${whereSql}
         ) sub
         ORDER BY created_at DESC`,
        params
      );
      gameIds = idResult.rows.map((r) => r.game_id);
      if (gameIds.length > DOWNLOAD_MAX_GAMES) {
        return res.status(400).json({
          success: false,
          message: `\u5355\u6b21\u6700\u591a\u4e0b\u8f7d ${DOWNLOAD_MAX_GAMES} \u5c40\uff0c\u5f53\u524d\u7b5b\u9009\u547d\u4e2d ${gameIds.length} \u5c40\uff0c\u8bf7\u7f29\u5c0f\u8303\u56f4`,
        });
      }
    }

    if (gameIds.length === 0) {
      return res.status(404).json({ success: false, message: '\u6ca1\u6709\u5339\u914d\u7684\u724c\u8c31' });
    }

    const recordsResult = await pool.query(
      `SELECT game_id, record FROM game_records WHERE game_id = ANY($1::varchar[])`,
      [gameIds]
    );
    const byGame = new Map(recordsResult.rows.map((r) => [r.game_id, r.record]));

    const archiver = require('archiver');
    const safeName = String(eventId).replace(/[^\w.-]+/g, '_');
    res.setHeader('Content-Type', 'application/zip');
    res.setHeader('Content-Disposition', `attachment; filename="event_${safeName}_records.zip"`);
    const archive = archiver('zip', { zlib: { level: 5 } });
    archive.on('error', (err) => {
      console.error('event-admin zip error:', err);
      if (!res.headersSent) {
        res.status(500).json({ success: false, message: '\u6253\u5305\u5931\u8d25' });
      } else {
        res.end();
      }
    });
    archive.pipe(res);
    for (const gameId of gameIds) {
      const raw = byGame.get(gameId);
      if (raw === undefined || raw === null) continue;
      const body = typeof raw === 'string' ? raw : JSON.stringify(raw);
      archive.append(body, { name: `${gameId}.json` });
    }
    await archive.finalize();
  } catch (err) {
    console.error('event-admin records download:', err);
    if (!res.headersSent) {
      res.status(500).json({ success: false, message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef' });
    }
  }
});

module.exports = router;
