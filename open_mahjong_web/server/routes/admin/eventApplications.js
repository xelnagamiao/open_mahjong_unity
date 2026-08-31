const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const { writeAudit } = require('../../utils/audit');
const { generateEventId } = require('../../utils/eventsTables');
const { appendRemark, ensureHistory, withRemarkHistory } = require('../../utils/applicationRemarks');

function normalizeName(name) {
  const text = String(name || '').trim();
  if (!text) return { error: '请填写赛事名称' };
  if (text.length > 128) return { error: '赛事名称过长（最多 128 字）' };
  return { value: text };
}

router.get('/', async (req, res) => {
  try {
    const status = String(req.query.status || '').trim();
    const kind = req.query.kind === 'base' ? 'base' : req.query.kind === 'event' ? 'event' : '';
    const page = Math.max(1, parseInt(req.query.page, 10) || 1);
    const pageSize = Math.min(50, Math.max(1, parseInt(req.query.page_size, 10) || 20));
    const offset = (page - 1) * pageSize;

    const params = [];
    const conditions = ['TRUE'];
    if (status && ['pending', 'approved', 'rejected', 'cancelled'].includes(status)) {
      params.push(status);
      conditions.push(`a.status = $${params.length}`);
    }
    if (kind) {
      params.push(kind);
      conditions.push(`a.kind = $${params.length}`);
    }
    const where = conditions.join(' AND ');

    const countRes = await pool.query(
      `SELECT COUNT(*)::int AS cnt FROM event_applications a WHERE ${where}`,
      params
    );
    const listParams = [...params, pageSize, offset];
    const result = await pool.query(
      `SELECT a.application_id, a.applicant_user_id, a.name, a.description, a.remark, a.reason,
              a.planned_start_at, a.planned_end_at, a.kind,
              a.status, a.reviewer_user_id, a.review_note, a.event_id,
              a.organizer_name, a.organizer_phone, a.remark_history,
              a.created_at, a.updated_at, a.reviewed_at,
              u.username AS applicant_username
       FROM event_applications a
       LEFT JOIN users u ON u.user_id = a.applicant_user_id
       WHERE ${where}
       ORDER BY CASE a.status WHEN 'pending' THEN 0 ELSE 1 END, a.created_at DESC
       LIMIT $${listParams.length - 1} OFFSET $${listParams.length}`,
      listParams
    );

    res.json({
      success: true,
      data: {
        items: result.rows.map(withRemarkHistory),
        total: countRes.rows[0].cnt,
        page,
        page_size: pageSize,
      },
    });
  } catch (err) {
    console.error('admin event-applications list:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/:id/approve', async (req, res) => {
  const client = await pool.connect();
  try {
    const applicationId = parseInt(req.params.id, 10);
    if (Number.isNaN(applicationId) || applicationId <= 0) {
      return res.status(400).json({ success: false, message: '无效的申请 ID' });
    }
    const reviewNote = String(req.body?.review_note || '').trim();
    let overrideName = null;
    if (req.body?.name !== undefined && req.body?.name !== null && String(req.body.name).trim()) {
      const parsed = normalizeName(req.body.name);
      if (parsed.error) {
        return res.status(400).json({ success: false, message: parsed.error });
      }
      overrideName = parsed.value;
    }

    await client.query('BEGIN');
    const appRes = await client.query(
      `SELECT * FROM event_applications WHERE application_id = $1 FOR UPDATE`,
      [applicationId]
    );
    if (appRes.rows.length === 0) {
      await client.query('ROLLBACK');
      return res.status(404).json({ success: false, message: '申请不存在' });
    }
    const application = appRes.rows[0];
    if (application.status !== 'pending') {
      await client.query('ROLLBACK');
      return res.status(400).json({ success: false, message: '该申请已处理' });
    }

    const userRes = await client.query(
      `SELECT user_id, username, is_tourist FROM users WHERE user_id = $1`,
      [application.applicant_user_id]
    );
    if (userRes.rows.length === 0 || userRes.rows[0].is_tourist) {
      await client.query('ROLLBACK');
      return res.status(400).json({ success: false, message: '申请人账号无效' });
    }

    const eventName = overrideName || application.name;
    const eventDescription = String(
      application.description || application.reason || ''
    ).trim();
    let eventId = generateEventId();
    for (let attempt = 0; attempt < 5; attempt += 1) {
      const exists = await client.query(`SELECT 1 FROM events WHERE event_id = $1`, [eventId]);
      if (exists.rows.length === 0) break;
      eventId = generateEventId();
    }

    const kind = application.kind === 'base' ? 'base' : 'event';
    const entryConfig = kind === 'base'
      ? { forbid_tourist: true, auto_approve: true, create_room_permission: 'registered', member_can_create_room: true, unregistered_can_create_room: false, unregistered_can_ready: false }
      : { forbid_tourist: false, auto_approve: false, create_room_permission: 'admin', member_can_create_room: false, unregistered_can_create_room: false, unregistered_can_ready: false };
    await client.query(
      `INSERT INTO events (event_id, name, description, status, created_by, kind, entry_config)
       VALUES ($1, $2, $3, 'registered', $4, $5, $6::jsonb)`,
      [eventId, eventName, eventDescription, req.admin.userId, kind, JSON.stringify(entryConfig)]
    );
    await client.query(
      `INSERT INTO event_admins (event_id, user_id, role, added_by)
       VALUES ($1, $2, 'owner', $3)`,
      [eventId, application.applicant_user_id, req.admin.userId]
    );
    const history = appendRemark(ensureHistory(application), {
      role: 'admin',
      action: 'approve',
      text: reviewNote,
    });
    const updated = await client.query(
      `UPDATE event_applications
       SET status = 'approved',
           reviewer_user_id = $1,
           review_note = $2,
           event_id = $3,
           name = $4,
           remark_history = $5::jsonb,
           reviewed_at = CURRENT_TIMESTAMP,
           updated_at = CURRENT_TIMESTAMP
       WHERE application_id = $6
       RETURNING *`,
      [req.admin.userId, reviewNote || null, eventId, eventName, JSON.stringify(history), applicationId]
    );
    await client.query('COMMIT');

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'event_application.approve',
      targetType: 'event_application',
      targetId: String(applicationId),
      payload: {
        application_id: applicationId,
        event_id: eventId,
        applicant_user_id: application.applicant_user_id,
      },
      reason: reviewNote || '批准办赛申请',
    });

    res.json({
      success: true,
      data: {
        application: withRemarkHistory(updated.rows[0]),
        event_id: eventId,
      },
    });
  } catch (err) {
    try {
      await client.query('ROLLBACK');
    } catch (_) {
      /* ignore */
    }
    console.error('admin event-applications approve:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  } finally {
    client.release();
  }
});

router.post('/:id/reject', async (req, res) => {
  try {
    const applicationId = parseInt(req.params.id, 10);
    if (Number.isNaN(applicationId) || applicationId <= 0) {
      return res.status(400).json({ success: false, message: '无效的申请 ID' });
    }
    const reviewNote = String(req.body?.review_note || '').trim();
    if (!reviewNote) {
      return res.status(400).json({ success: false, message: '请填写拒绝原因' });
    }

    const current = await pool.query(
      `SELECT * FROM event_applications WHERE application_id = $1 AND status = 'pending'`,
      [applicationId]
    );
    if (current.rows.length === 0) {
      return res.status(400).json({ success: false, message: '申请不存在或已处理' });
    }
    const history = appendRemark(ensureHistory(current.rows[0]), {
      role: 'admin',
      action: 'reject',
      text: reviewNote,
    });
    const result = await pool.query(
      `UPDATE event_applications
       SET status = 'rejected',
           reviewer_user_id = $1,
           review_note = $2,
           remark_history = $3::jsonb,
           reviewed_at = CURRENT_TIMESTAMP,
           updated_at = CURRENT_TIMESTAMP
       WHERE application_id = $4 AND status = 'pending'
       RETURNING *`,
      [req.admin.userId, reviewNote, JSON.stringify(history), applicationId]
    );
    if (result.rows.length === 0) {
      return res.status(400).json({ success: false, message: '申请不存在或已处理' });
    }

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'event_application.reject',
      targetType: 'event_application',
      targetId: String(applicationId),
      payload: { application_id: applicationId },
      reason: reviewNote,
    });

    res.json({ success: true, data: withRemarkHistory(result.rows[0]) });
  } catch (err) {
    console.error('admin event-applications reject:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
