const express = require('express');
const router = express.Router({ mergeParams: true });
const pool = require('../../config/database');
const { writeAudit } = require('../../utils/audit');
const {
  requireEventMembership,
  requireEventOwner,
} = require('../../middleware/requireEventAdmin');

function normalizeName(name) {
  const text = String(name || '').trim();
  if (!text) return { error: '请填写赛事名称' };
  if (text.length > 128) return { error: '赛事名称过长（最多 128 字）' };
  return { value: text };
}

function normalizeDescription(description) {
  const text = String(description || '').trim();
  if (text.length > 2000) return { error: '赛事介绍过长（最多 2000 字）' };
  return { value: text };
}

function normalizeTitle(title) {
  const text = String(title || '').trim();
  if (!text) return { error: '请填写公告标题' };
  if (text.length > 200) return { error: '公告标题过长（最多 200 字）' };
  return { value: text };
}

function normalizeBody(body) {
  const text = String(body || '').trim();
  if (!text) return { error: '请填写公告内容' };
  if (text.length > 10000) return { error: '公告内容过长（最多 10000 字）' };
  return { value: text };
}

async function fetchAnnouncements(eventId) {
  const result = await pool.query(
    `SELECT a.announcement_id, a.event_id, a.title, a.body, a.created_by, a.created_at, a.updated_at,
            u.username AS author_username
     FROM event_announcements a
     LEFT JOIN users u ON u.user_id = a.created_by
     WHERE a.event_id = $1
     ORDER BY a.created_at DESC`,
    [eventId]
  );
  return result.rows;
}

async function fetchPendingProfileChange(eventId) {
  const result = await pool.query(
    `SELECT r.request_id, r.event_id, r.requested_by, r.proposed_name, r.proposed_description,
            r.reason, r.status, r.review_note, r.created_at, r.reviewed_at,
            u.username AS requester_username
     FROM event_profile_change_requests r
     LEFT JOIN users u ON u.user_id = r.requested_by
     WHERE r.event_id = $1 AND r.status = 'pending'
     ORDER BY r.created_at DESC
     LIMIT 1`,
    [eventId]
  );
  return result.rows[0] || null;
}

async function fetchLatestRejectedProfileChange(eventId) {
  const result = await pool.query(
    `SELECT r.request_id, r.event_id, r.requested_by, r.proposed_name, r.proposed_description,
            r.reason, r.status, r.review_note, r.created_at, r.reviewed_at,
            u.username AS requester_username
     FROM event_profile_change_requests r
     LEFT JOIN users u ON u.user_id = r.requested_by
     WHERE r.event_id = $1 AND r.status = 'rejected'
     ORDER BY r.reviewed_at DESC NULLS LAST, r.updated_at DESC
     LIMIT 1`,
    [eventId]
  );
  return result.rows[0] || null;
}

router.get('/announcements', requireEventMembership, async (req, res) => {
  try {
    const items = await fetchAnnouncements(req.event.event_id);
    res.json({ success: true, data: { items } });
  } catch (err) {
    console.error('event-admin announcements list:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/announcements', requireEventMembership, async (req, res) => {
  try {
    const titleParsed = normalizeTitle(req.body?.title);
    if (titleParsed.error) {
      return res.status(400).json({ success: false, message: titleParsed.error });
    }
    const bodyParsed = normalizeBody(req.body?.body);
    if (bodyParsed.error) {
      return res.status(400).json({ success: false, message: bodyParsed.error });
    }

    const result = await pool.query(
      `INSERT INTO event_announcements (event_id, title, body, created_by)
       VALUES ($1, $2, $3, $4)
       RETURNING announcement_id, event_id, title, body, created_by, created_at, updated_at`,
      [req.event.event_id, titleParsed.value, bodyParsed.value, req.eventAdmin.userId]
    );

    await writeAudit({
      adminUserId: req.eventAdmin.userId,
      action: 'event_admin.announcement.create',
      targetType: 'event',
      targetId: req.event.event_id,
      payload: { announcement_id: result.rows[0].announcement_id, title: titleParsed.value },
    });

    const items = await fetchAnnouncements(req.event.event_id);
    res.json({ success: true, data: { item: result.rows[0], items }, message: '公告已发布' });
  } catch (err) {
    console.error('event-admin announcement create:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.delete('/announcements/:announcementId', requireEventMembership, async (req, res) => {
  try {
    const announcementId = parseInt(req.params.announcementId, 10);
    if (Number.isNaN(announcementId) || announcementId <= 0) {
      return res.status(400).json({ success: false, message: '无效的公告 ID' });
    }

    const existing = await pool.query(
      `SELECT announcement_id, created_by, title FROM event_announcements
       WHERE announcement_id = $1 AND event_id = $2`,
      [announcementId, req.event.event_id]
    );
    if (existing.rows.length === 0) {
      return res.status(404).json({ success: false, message: '公告不存在' });
    }
    if (
      req.eventRole !== 'owner' &&
      Number(existing.rows[0].created_by) !== Number(req.eventAdmin.userId)
    ) {
      return res.status(403).json({ success: false, message: '仅可删除自己发布的公告，或由主管理员删除' });
    }

    await pool.query(`DELETE FROM event_announcements WHERE announcement_id = $1`, [announcementId]);
    await writeAudit({
      adminUserId: req.eventAdmin.userId,
      action: 'event_admin.announcement.delete',
      targetType: 'event',
      targetId: req.event.event_id,
      payload: {
        announcement_id: announcementId,
        title: existing.rows[0].title,
      },
    });

    const items = await fetchAnnouncements(req.event.event_id);
    res.json({ success: true, data: { items }, message: '公告已删除' });
  } catch (err) {
    console.error('event-admin announcement delete:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/profile-change', requireEventMembership, async (req, res) => {
  try {
    const pending = await fetchPendingProfileChange(req.event.event_id);
    const lastRejected = pending
      ? null
      : await fetchLatestRejectedProfileChange(req.event.event_id);
    res.json({
      success: true,
      data: {
        current_name: req.event.name,
        current_description: req.event.description || '',
        pending,
        last_rejected: lastRejected,
      },
    });
  } catch (err) {
    console.error('event-admin profile-change get:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/profile-change', requireEventMembership, requireEventOwner, async (req, res) => {
  try {
    const nameParsed = normalizeName(req.body?.name);
    if (nameParsed.error) {
      return res.status(400).json({ success: false, message: nameParsed.error });
    }
    const descParsed = normalizeDescription(req.body?.description);
    if (descParsed.error) {
      return res.status(400).json({ success: false, message: descParsed.error });
    }
    const reason = String(req.body?.reason || '').trim();

    const sameName = nameParsed.value === String(req.event.name || '').trim();
    const sameDesc = descParsed.value === String(req.event.description || '').trim();
    if (sameName && sameDesc) {
      return res.status(400).json({ success: false, message: '赛事名与简介均未变更' });
    }

    const pending = await fetchPendingProfileChange(req.event.event_id);
    if (pending) {
      return res.status(400).json({
        success: false,
        message: '已有待审核的资料修改申请，请等待平台管理员处理',
      });
    }

    const result = await pool.query(
      `INSERT INTO event_profile_change_requests
         (event_id, requested_by, proposed_name, proposed_description, reason)
       VALUES ($1, $2, $3, $4, $5)
       RETURNING request_id, event_id, requested_by, proposed_name, proposed_description,
                 reason, status, created_at`,
      [
        req.event.event_id,
        req.eventAdmin.userId,
        nameParsed.value,
        descParsed.value,
        reason,
      ]
    );

    await writeAudit({
      adminUserId: req.eventAdmin.userId,
      action: 'event_admin.profile_change.request',
      targetType: 'event',
      targetId: req.event.event_id,
      payload: {
        request_id: result.rows[0].request_id,
        proposed_name: nameParsed.value,
        before: { name: req.event.name, description: req.event.description },
      },
      reason,
    });

    res.json({
      success: true,
      data: { pending: result.rows[0] },
      message: '已提交资料修改申请，等待平台管理员审核',
    });
  } catch (err) {
    console.error('event-admin profile-change create:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/profile-change/cancel', requireEventMembership, requireEventOwner, async (req, res) => {
  try {
    const pending = await fetchPendingProfileChange(req.event.event_id);
    if (!pending) {
      return res.status(400).json({ success: false, message: '当前没有待审核的资料修改申请' });
    }
    await pool.query(
      `UPDATE event_profile_change_requests
       SET status = 'cancelled', updated_at = CURRENT_TIMESTAMP
       WHERE request_id = $1`,
      [pending.request_id]
    );
    await writeAudit({
      adminUserId: req.eventAdmin.userId,
      action: 'event_admin.profile_change.cancel',
      targetType: 'event',
      targetId: req.event.event_id,
      payload: { request_id: pending.request_id },
    });
    res.json({ success: true, message: '已撤销资料修改申请' });
  } catch (err) {
    console.error('event-admin profile-change cancel:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
