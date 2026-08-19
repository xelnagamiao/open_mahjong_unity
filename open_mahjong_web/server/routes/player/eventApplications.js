const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const { requirePlayer } = require('../../middleware/requirePlayer');

function venueApplyLabel(kind) {
  return kind === 'base' ? '基地申请' : '办赛申请';
}

function normalizeName(name, kind) {
  const label = kind === 'base' ? '基地名称' : '赛事名称';
  const text = String(name || '').trim();
  if (!text) return { error: `请填写${label}` };
  if (text.length > 128) return { error: `${label}过长（最多 128 字）` };
  return { value: text };
}

function normalizeText(value, { required, label, maxLen }) {
  const text = String(value || '').trim();
  if (!text) {
    if (required) return { error: `请填写${label}` };
    return { value: '' };
  }
  if (text.length > maxLen) {
    return { error: `${label}过长（最多 ${maxLen} 字）` };
  }
  return { value: text };
}

function normalizeDate(value, { required, label }) {
  if (value === undefined || value === null || String(value).trim() === '') {
    if (required) return { error: `请填写${label}` };
    return { value: null };
  }
  const text = String(value).trim().slice(0, 10);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(text)) {
    return { error: `${label}格式不正确` };
  }
  const d = new Date(`${text}T00:00:00`);
  if (Number.isNaN(d.getTime())) {
    return { error: `${label}无效` };
  }
  return { value: text };
}

router.use(requirePlayer);

router.post('/', async (req, res) => {
  try {
    const parsed = parseApplicationBody(req.body);
    if (parsed.error) {
      return res.status(400).json({ success: false, message: parsed.error });
    }
    const v = parsed.value;

    const pending = await pool.query(
      `SELECT application_id FROM event_applications
       WHERE applicant_user_id = $1
         AND status = 'pending'
         AND COALESCE(kind, 'event') = $2
       LIMIT 1`,
      [req.player.userId, v.kind]
    );
    if (pending.rows.length > 0) {
      return res.status(400).json({
        success: false,
        message: `您已有一条待审核的${venueApplyLabel(v.kind)}，请等待处理后再提交`,
      });
    }

    const result = await pool.query(
      `INSERT INTO event_applications
         (applicant_user_id, name, description, remark, reason,
          planned_start_at, planned_end_at, status, kind)
       VALUES ($1, $2, $3, $4, $3, $5, $6, 'pending', $7)
       RETURNING application_id, applicant_user_id, name, description, remark, reason,
                 planned_start_at, planned_end_at, kind,
                 status, event_id, created_at, updated_at, reviewed_at, review_note`,
      [
        req.player.userId,
        v.name,
        v.description,
        v.remark,
        v.planned_start_at,
        v.planned_end_at,
        v.kind,
      ]
    );

    res.json({ success: true, data: result.rows[0] });
  } catch (err) {
    if (err.code === '23505') {
      return res.status(400).json({
        success: false,
        message: `您已有一条待审核的${venueApplyLabel(parsed?.value?.kind)}，请等待处理后再提交`,
      });
    }
    console.error('player event-applications create:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/mine', async (req, res) => {
  try {
    const result = await pool.query(
      `SELECT application_id, applicant_user_id, name, description, remark, reason,
              planned_start_at, planned_end_at, kind, status,
              reviewer_user_id, review_note, event_id,
              created_at, updated_at, reviewed_at
       FROM event_applications
       WHERE applicant_user_id = $1
       ORDER BY created_at DESC
       LIMIT 50`,
      [req.player.userId]
    );
    res.json({ success: true, data: { items: result.rows } });
  } catch (err) {
    console.error('player event-applications mine:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

function parseApplicationBody(body) {
  const { name, description, remark, planned_start_at, planned_end_at, kind } = body || {};
  const kindValue = kind === 'base' ? 'base' : 'event';
  const nameParsed = normalizeName(name, kindValue);
  if (nameParsed.error) return { error: nameParsed.error };
  const startParsed = normalizeDate(planned_start_at, {
    required: kindValue === 'event',
    label: '拟定开始时间',
  });
  if (startParsed.error) return { error: startParsed.error };
  const endParsed = normalizeDate(planned_end_at, {
    required: false,
    label: '拟定结束时间',
  });
  if (endParsed.error) return { error: endParsed.error };
  if (startParsed.value && endParsed.value && endParsed.value < startParsed.value) {
    return { error: '拟定结束时间不能早于拟定开始时间' };
  }
  const descParsed = normalizeText(description, {
    required: true,
    label: kindValue === 'base' ? '基地介绍' : '赛事介绍',
    maxLen: 2000,
  });
  if (descParsed.error) return { error: descParsed.error };
  const remarkParsed = normalizeText(remark, {
    required: false,
    label: '备注',
    maxLen: 1000,
  });
  if (remarkParsed.error) return { error: remarkParsed.error };
  return {
    value: {
      name: nameParsed.value,
      description: descParsed.value,
      remark: remarkParsed.value,
      planned_start_at: startParsed.value,
      planned_end_at: endParsed.value,
      kind: kindValue,
    },
  };
}

/** 修改待审申请内容 */
router.put('/:id', async (req, res) => {
  try {
    const applicationId = parseInt(req.params.id, 10);
    if (Number.isNaN(applicationId) || applicationId <= 0) {
      return res.status(400).json({ success: false, message: '无效的申请 ID' });
    }
    const parsed = parseApplicationBody(req.body);
    if (parsed.error) {
      return res.status(400).json({ success: false, message: parsed.error });
    }
    const v = parsed.value;

    const result = await pool.query(
      `UPDATE event_applications
       SET name = $1,
           description = $2,
           remark = $3,
           reason = $2,
           planned_start_at = $4,
           planned_end_at = $5,
           kind = $6,
           updated_at = CURRENT_TIMESTAMP
       WHERE application_id = $7
         AND applicant_user_id = $8
         AND status = 'pending'
       RETURNING application_id, applicant_user_id, name, description, remark, reason,
                 planned_start_at, planned_end_at, kind, status, event_id,
                 created_at, updated_at, reviewed_at, review_note`,
      [
        v.name,
        v.description,
        v.remark,
        v.planned_start_at,
        v.planned_end_at,
        v.kind,
        applicationId,
        req.player.userId,
      ]
    );
    if (result.rows.length === 0) {
      return res.status(400).json({
        success: false,
        message: '只能修改本人的待审核申请',
      });
    }
    res.json({ success: true, data: result.rows[0], message: '申请已更新' });
  } catch (err) {
    if (err.code === '23505') {
      return res.status(400).json({
        success: false,
        message: `您已有一条待审核的${venueApplyLabel(req.body?.kind)}`,
      });
    }
    console.error('player event-applications update:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

/** 被拒绝后重新提交（同一条申请回到 pending） */
router.post('/:id/resubmit', async (req, res) => {
  try {
    const applicationId = parseInt(req.params.id, 10);
    if (Number.isNaN(applicationId) || applicationId <= 0) {
      return res.status(400).json({ success: false, message: '无效的申请 ID' });
    }
    const parsed = parseApplicationBody(req.body);
    if (parsed.error) {
      return res.status(400).json({ success: false, message: parsed.error });
    }
    const v = parsed.value;

    const otherPending = await pool.query(
      `SELECT application_id FROM event_applications
       WHERE applicant_user_id = $1
         AND status = 'pending'
         AND application_id <> $2
         AND COALESCE(kind, 'event') = $3
       LIMIT 1`,
      [req.player.userId, applicationId, v.kind]
    );
    if (otherPending.rows.length > 0) {
      return res.status(400).json({
        success: false,
        message: `您已有一条待审核的${venueApplyLabel(v.kind)}，请等待处理后再重新提交`,
      });
    }

    const result = await pool.query(
      `UPDATE event_applications
       SET name = $1,
           description = $2,
           remark = $3,
           reason = $2,
           planned_start_at = $4,
           planned_end_at = $5,
           kind = $6,
           status = 'pending',
           reviewer_user_id = NULL,
           review_note = NULL,
           reviewed_at = NULL,
           event_id = NULL,
           updated_at = CURRENT_TIMESTAMP
       WHERE application_id = $7
         AND applicant_user_id = $8
         AND status = 'rejected'
       RETURNING application_id, applicant_user_id, name, description, remark, reason,
                 planned_start_at, planned_end_at, kind, status, event_id,
                 created_at, updated_at, reviewed_at, review_note`,
      [
        v.name,
        v.description,
        v.remark,
        v.planned_start_at,
        v.planned_end_at,
        v.kind,
        applicationId,
        req.player.userId,
      ]
    );
    if (result.rows.length === 0) {
      return res.status(400).json({
        success: false,
        message: '只能重新提交本人被拒绝的申请',
      });
    }
    res.json({
      success: true,
      data: result.rows[0],
      message: '已重新提交，请等待管理员审核',
    });
  } catch (err) {
    if (err.code === '23505') {
      return res.status(400).json({
        success: false,
        message: `您已有一条待审核的${venueApplyLabel(parsed?.value?.kind)}，请等待处理后再重新提交`,
      });
    }
    console.error('player event-applications resubmit:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
