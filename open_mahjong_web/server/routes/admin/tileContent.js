const express = require('express');
const pool = require('../../config/database');
const { writeAudit } = require('../../utils/audit');
const store = require('../../services/tileContentStore');

const router = express.Router();
const STATUSES = new Set(['pending', 'approved', 'rejected']);
const KINDS = new Set(['tile_face', 'tile_background']);

function publicRow(row) {
  return {
    submission_id: Number(row.submission_id),
    user_id: Number(row.user_id),
    username: row.username || '',
    kind: row.kind,
    name: row.name,
    description: row.description || '',
    original_filename: row.original_filename,
    status: row.status,
    review_note: row.review_note || '',
    reviewer_user_id: row.reviewer_user_id,
    file_size: Number(row.file_size) || 0,
    meta: row.meta || {},
    created_at: row.created_at,
    updated_at: row.updated_at,
    reviewed_at: row.reviewed_at,
  };
}

function contentDisposition(filename) {
  const ascii = String(filename || 'pack.zip').replace(/[^\x20-\x7E]/g, '_');
  return `attachment; filename="${ascii}"; filename*=UTF-8''${encodeURIComponent(filename || 'pack.zip')}`;
}

async function loadSubmission(id) {
  const submissionId = parseInt(id, 10);
  if (!Number.isInteger(submissionId) || submissionId <= 0) return null;
  const result = await pool.query(
    `SELECT s.*, u.username
     FROM tile_content_submissions s
     LEFT JOIN users u ON u.user_id = s.user_id
     WHERE s.submission_id = $1`,
    [submissionId]
  );
  return result.rows[0] || null;
}

router.get('/', async (req, res) => {
  try {
    const status = String(req.query.status || '').trim();
    const kind = String(req.query.kind || '').trim();
    const page = Math.max(1, parseInt(req.query.page, 10) || 1);
    const pageSize = Math.min(50, Math.max(1, parseInt(req.query.page_size, 10) || 20));
    const offset = (page - 1) * pageSize;
    const params = [];
    const conditions = ['TRUE'];
    if (STATUSES.has(status)) {
      params.push(status);
      conditions.push(`s.status = $${params.length}`);
    }
    if (KINDS.has(kind)) {
      params.push(kind);
      conditions.push(`s.kind = $${params.length}`);
    }
    const where = conditions.join(' AND ');
    const countRes = await pool.query(
      `SELECT COUNT(*)::int AS cnt FROM tile_content_submissions s WHERE ${where}`,
      params
    );
    const listParams = [...params, pageSize, offset];
    const result = await pool.query(
      `SELECT s.submission_id, s.user_id, s.kind, s.name, s.description, s.original_filename, s.status,
              s.review_note, s.reviewer_user_id, s.file_size, s.meta,
              s.created_at, s.updated_at, s.reviewed_at, u.username
       FROM tile_content_submissions s
       LEFT JOIN users u ON u.user_id = s.user_id
       WHERE ${where}
       ORDER BY CASE s.status WHEN 'pending' THEN 0 ELSE 1 END, s.created_at DESC
       LIMIT $${listParams.length - 1} OFFSET $${listParams.length}`,
      listParams
    );
    res.json({
      success: true,
      data: {
        items: result.rows.map(publicRow),
        total: countRes.rows[0].cnt,
        page,
        page_size: pageSize,
      },
    });
  } catch (err) {
    console.error('admin tile-content list:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/:id/file', async (req, res) => {
  try {
    const row = await loadSubmission(req.params.id);
    if (!row) return res.status(404).json({ success: false, message: '记录不存在' });
    const buf = store.readZipFile(row.storage_key);
    res.setHeader('Content-Type', 'application/zip');
    res.setHeader('Content-Disposition', contentDisposition(row.original_filename));
    res.send(buf);
  } catch (err) {
    const status = err.status || 500;
    if (status >= 500) console.error('admin tile-content file:', err);
    res.status(status).json({ success: false, message: err.status ? err.message : '服务器内部错误' });
  }
});

router.get('/:id/preview/:name', async (req, res) => {
  try {
    const row = await loadSubmission(req.params.id);
    if (!row) return res.status(404).json({ success: false, message: '记录不存在' });
    const buf = store.readPreviewFile(row.storage_key, String(req.params.name || ''));
    res.setHeader('Content-Type', 'image/png');
    res.setHeader('Cache-Control', 'private, max-age=60');
    res.send(buf);
  } catch (err) {
    const status = err.status || 500;
    if (status >= 500) console.error('admin tile-content preview:', err);
    res.status(status).json({ success: false, message: err.status ? err.message : '服务器内部错误' });
  }
});

router.get('/:id', async (req, res) => {
  try {
    const row = await loadSubmission(req.params.id);
    if (!row) return res.status(404).json({ success: false, message: '记录不存在' });
    res.json({ success: true, data: publicRow(row) });
  } catch (err) {
    console.error('admin tile-content detail:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/:id/approve', async (req, res) => {
  try {
    const reviewNote = String(req.body?.review_note || '').trim();
    const current = await pool.query(
      `SELECT * FROM tile_content_submissions WHERE submission_id = $1 AND status = 'pending'`,
      [parseInt(req.params.id, 10)]
    );
    if (current.rows.length === 0) {
      return res.status(400).json({ success: false, message: '记录不存在或已处理' });
    }
    const result = await pool.query(
      `UPDATE tile_content_submissions
       SET status = 'approved',
           reviewer_user_id = $1,
           review_note = $2,
           reviewed_at = CURRENT_TIMESTAMP,
           updated_at = CURRENT_TIMESTAMP
       WHERE submission_id = $3 AND status = 'pending'
       RETURNING submission_id, user_id, kind, name, original_filename, status, review_note,
                 reviewer_user_id, file_size, meta, created_at, updated_at, reviewed_at`,
      [req.admin.userId, reviewNote || null, current.rows[0].submission_id]
    );
    if (result.rows.length === 0) {
      return res.status(400).json({ success: false, message: '记录不存在或已处理' });
    }
    const row = result.rows[0];
    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'tile_content.approve',
      targetType: 'tile_content',
      targetId: String(row.submission_id),
      payload: { kind: row.kind, user_id: row.user_id },
      reason: reviewNote || '通过牌面审核',
    });
    res.json({ success: true, data: publicRow(row) });
  } catch (err) {
    console.error('admin tile-content approve:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/:id/reject', async (req, res) => {
  try {
    const reviewNote = String(req.body?.review_note || '').trim();
    if (!reviewNote) {
      return res.status(400).json({ success: false, message: '请填写拒绝原因' });
    }
    const result = await pool.query(
      `UPDATE tile_content_submissions
       SET status = 'rejected',
           reviewer_user_id = $1,
           review_note = $2,
           reviewed_at = CURRENT_TIMESTAMP,
           updated_at = CURRENT_TIMESTAMP
       WHERE submission_id = $3 AND status = 'pending'
       RETURNING submission_id, user_id, kind, name, original_filename, status, review_note,
                 reviewer_user_id, file_size, meta, created_at, updated_at, reviewed_at`,
      [req.admin.userId, reviewNote, parseInt(req.params.id, 10)]
    );
    if (result.rows.length === 0) {
      return res.status(400).json({ success: false, message: '记录不存在或已处理' });
    }
    const row = result.rows[0];
    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'tile_content.reject',
      targetType: 'tile_content',
      targetId: String(row.submission_id),
      payload: { kind: row.kind, user_id: row.user_id },
      reason: reviewNote,
    });
    res.json({ success: true, data: publicRow(row) });
  } catch (err) {
    console.error('admin tile-content reject:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
