const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const { writeAudit } = require('../../utils/audit');

async function fetchRequest(requestId) {
  const result = await pool.query(
    `SELECT r.request_id, r.event_id, r.requested_by, r.proposed_name, r.proposed_description,
            r.reason, r.status, r.reviewer_user_id, r.review_note, r.created_at, r.reviewed_at,
            e.name AS current_name, e.description AS current_description, e.status AS event_status, e.kind,
            u.username AS requester_username
     FROM event_profile_change_requests r
     INNER JOIN events e ON e.event_id = r.event_id
     LEFT JOIN users u ON u.user_id = r.requested_by
     WHERE r.request_id = $1`,
    [requestId]
  );
  return result.rows[0] || null;
}

router.get('/', async (req, res) => {
  try {
    const page = Math.max(1, parseInt(req.query.page, 10) || 1);
    const limit = Math.min(100, Math.max(1, parseInt(req.query.limit, 10) || 20));
    const offset = (page - 1) * limit;
    const status = String(req.query.status || '').trim();
    const kind = req.query.kind === 'base' ? 'base' : req.query.kind === 'event' ? 'event' : '';

    const conditions = [];
    const params = [];
    let idx = 1;
    if (['pending', 'approved', 'rejected', 'cancelled'].includes(status)) {
      conditions.push(`r.status = $${idx++}`);
      params.push(status);
    }
    if (kind) {
      conditions.push(`e.kind = $${idx++}`);
      params.push(kind);
    }
    const where = conditions.length ? `WHERE ${conditions.join(' AND ')}` : '';

    const listRes = await pool.query(
      `SELECT r.request_id, r.event_id, r.requested_by, r.proposed_name, r.proposed_description,
              r.reason, r.status, r.created_at, r.reviewed_at,
              e.name AS current_name, e.description AS current_description, e.kind,
              u.username AS requester_username
       FROM event_profile_change_requests r
       INNER JOIN events e ON e.event_id = r.event_id
       LEFT JOIN users u ON u.user_id = r.requested_by
       ${where}
       ORDER BY
         CASE r.status WHEN 'pending' THEN 0 ELSE 1 END,
         r.created_at DESC
       LIMIT $${idx++} OFFSET $${idx++}`,
      [...params, limit, offset]
    );
    const countRes = await pool.query(
      `SELECT COUNT(*)::int AS cnt
       FROM event_profile_change_requests r
       INNER JOIN events e ON e.event_id = r.event_id
       ${where}`,
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
    console.error('admin event-profile-changes list:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/by-event/:eventId', async (req, res) => {
  try {
    const eventId = String(req.params.eventId || '').trim();
    const result = await pool.query(
      `SELECT r.request_id, r.event_id, r.requested_by, r.proposed_name, r.proposed_description,
              r.reason, r.status, r.created_at, r.reviewed_at,
              e.name AS current_name, e.description AS current_description,
              u.username AS requester_username
       FROM event_profile_change_requests r
       INNER JOIN events e ON e.event_id = r.event_id
       LEFT JOIN users u ON u.user_id = r.requested_by
       WHERE r.event_id = $1 AND r.status = 'pending'
       ORDER BY r.created_at DESC
       LIMIT 1`,
      [eventId]
    );
    res.json({ success: true, data: { pending: result.rows[0] || null } });
  } catch (err) {
    console.error('admin event-profile-changes by-event:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/:requestId/approve', async (req, res) => {
  const client = await pool.connect();
  try {
    const reason = String(req.body?.reason || req.body?.review_note || '').trim();
    if (!reason) {
      return res.status(400).json({ success: false, message: '请填写操作原因' });
    }
    const requestId = parseInt(req.params.requestId, 10);
    if (Number.isNaN(requestId) || requestId <= 0) {
      return res.status(400).json({ success: false, message: '无效的申请 ID' });
    }

    await client.query('BEGIN');
    const locked = await client.query(
      `SELECT * FROM event_profile_change_requests WHERE request_id = $1 FOR UPDATE`,
      [requestId]
    );
    if (locked.rows.length === 0) {
      await client.query('ROLLBACK');
      return res.status(404).json({ success: false, message: '申请不存在' });
    }
    const row = locked.rows[0];
    if (row.status !== 'pending') {
      await client.query('ROLLBACK');
      return res.status(400).json({ success: false, message: '该申请已处理' });
    }

    const eventBefore = await client.query(
      `SELECT event_id, name, description FROM events WHERE event_id = $1 FOR UPDATE`,
      [row.event_id]
    );
    if (eventBefore.rows.length === 0) {
      await client.query('ROLLBACK');
      return res.status(404).json({ success: false, message: '赛事不存在' });
    }

    const updatedEvent = await client.query(
      `UPDATE events
       SET name = $1, description = $2, updated_at = CURRENT_TIMESTAMP
       WHERE event_id = $3
       RETURNING event_id, name, description, status, reopen_requested, created_by, closed_at, created_at, updated_at`,
      [row.proposed_name, row.proposed_description || '', row.event_id]
    );
    const updatedReq = await client.query(
      `UPDATE event_profile_change_requests
       SET status = 'approved',
           reviewer_user_id = $1,
           review_note = $2,
           reviewed_at = CURRENT_TIMESTAMP,
           updated_at = CURRENT_TIMESTAMP
       WHERE request_id = $3
       RETURNING *`,
      [req.admin.userId, reason, requestId]
    );
    await client.query('COMMIT');

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'event.profile_change.approve',
      targetType: 'event',
      targetId: row.event_id,
      payload: {
        request_id: requestId,
        before: eventBefore.rows[0],
        after: updatedEvent.rows[0],
      },
      reason,
    });

    res.json({
      success: true,
      data: { request: updatedReq.rows[0], event: updatedEvent.rows[0] },
      message: '已通过资料修改',
    });
  } catch (err) {
    try {
      await client.query('ROLLBACK');
    } catch (_) {
      /* ignore */
    }
    console.error('admin event-profile-changes approve:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  } finally {
    client.release();
  }
});

router.post('/:requestId/reject', async (req, res) => {
  try {
    const reason = String(req.body?.reason || req.body?.review_note || '').trim();
    if (!reason) {
      return res.status(400).json({ success: false, message: '请填写拒绝原因' });
    }
    const requestId = parseInt(req.params.requestId, 10);
    if (Number.isNaN(requestId) || requestId <= 0) {
      return res.status(400).json({ success: false, message: '无效的申请 ID' });
    }

    const row = await fetchRequest(requestId);
    if (!row) {
      return res.status(404).json({ success: false, message: '申请不存在' });
    }
    if (row.status !== 'pending') {
      return res.status(400).json({ success: false, message: '该申请已处理' });
    }

    const updated = await pool.query(
      `UPDATE event_profile_change_requests
       SET status = 'rejected',
           reviewer_user_id = $1,
           review_note = $2,
           reviewed_at = CURRENT_TIMESTAMP,
           updated_at = CURRENT_TIMESTAMP
       WHERE request_id = $3
       RETURNING *`,
      [req.admin.userId, reason, requestId]
    );

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'event.profile_change.reject',
      targetType: 'event',
      targetId: row.event_id,
      payload: { request_id: requestId, proposed_name: row.proposed_name },
      reason,
    });

    res.json({ success: true, data: { request: updated.rows[0] }, message: '已拒绝资料修改' });
  } catch (err) {
    console.error('admin event-profile-changes reject:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
