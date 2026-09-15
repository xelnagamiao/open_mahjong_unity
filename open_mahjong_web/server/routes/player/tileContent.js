const express = require('express');
const multer = require('multer');
const pool = require('../../config/database');
const { requirePlayer, optionalPlayer } = require('../../middleware/requirePlayer');
const { createWindowLimiter, getClientIp } = require('../../middleware/rateLimit');
const { MAX_UNCOMPRESSED } = require('../../utils/tilePackValidate');
const store = require('../../services/tileContentStore');

const router = express.Router();
const KINDS = new Set(['tile_face', 'tile_background']);
const KIND_LABEL = {
  tile_face: '牌面',
  tile_background: '牌面背景',
};

const upload = multer({
  storage: multer.memoryStorage(),
  limits: { fileSize: MAX_UNCOMPRESSED, files: 1 },
  fileFilter: (_req, file, cb) => {
    const name = String(file.originalname || '').toLowerCase();
    if (!name.endsWith('.zip')) {
      cb(Object.assign(new Error('请上传 .zip 文件'), { status: 400 }));
      return;
    }
    cb(null, true);
  },
});

const uploadLimiter = createWindowLimiter({
  windowMs: 60_000,
  max: 8,
  keyFn: (req) => `${getClientIp(req)}:tile-content:${req.player?.userId || 0}`,
});

const publicLimiter = createWindowLimiter({
  windowMs: 60_000,
  max: 60,
  keyFn: (req) => `${getClientIp(req)}:tile-content-public`,
});

function kindLabel(kind) {
  return KIND_LABEL[kind] || '内容';
}

function parseKind(value) {
  const kind = String(value || '').trim();
  if (!KINDS.has(kind)) return { error: '请选择上传类型' };
  return { value: kind };
}

function parseName(value, fallback) {
  const text = String(value || '').trim();
  if (text.length > 64) return { error: '名称过长（最多 64 字）' };
  if (text) return { value: text };
  const base = String(fallback || '').replace(/\\/g, '/').split('/').pop() || '';
  const stripped = base.replace(/\.zip$/i, '').trim().slice(0, 64);
  return { value: stripped };
}

function parseDescription(value) {
  const text = String(value || '').trim();
  if (text.length > 500) return { error: '描述过长（最多 500 字）' };
  return { value: text };
}

function originalName(file) {
  const base = String(file?.originalname || 'pack.zip').replace(/\\/g, '/').split('/').pop();
  return (base || 'pack.zip').slice(0, 180);
}

function publicRow(row) {
  return {
    submission_id: Number(row.submission_id),
    kind: row.kind,
    name: row.name,
    description: row.description || '',
    original_filename: row.original_filename,
    status: row.status,
    review_note: row.review_note || '',
    file_size: Number(row.file_size) || 0,
    meta: row.meta || {},
    created_at: row.created_at,
    updated_at: row.updated_at,
    reviewed_at: row.reviewed_at,
  };
}

function catalogRow(row) {
  return {
    submission_id: Number(row.submission_id),
    kind: row.kind,
    name: row.name,
    description: row.description || '',
    original_filename: row.original_filename,
    file_size: Number(row.file_size) || 0,
    username: row.username || '',
    created_at: row.created_at,
    reviewed_at: row.reviewed_at,
  };
}

function sendMulter(req, res, next) {
  upload.single('file')(req, res, (err) => {
    if (!err) return next();
    const message = err.code === 'LIMIT_FILE_SIZE' ? '压缩包过大（超过 20MB）' : err.message || '上传失败';
    return res.status(400).json({ success: false, message });
  });
}

function contentDisposition(filename) {
  const ascii = String(filename || 'pack.zip').replace(/[^\x20-\x7E]/g, '_');
  return `attachment; filename="${ascii}"; filename*=UTF-8''${encodeURIComponent(filename || 'pack.zip')}`;
}

router.get('/catalog', publicLimiter, async (req, res) => {
  try {
    const kind = String(req.query.kind || '').trim();
    const params = [];
    const conditions = [`s.status = 'approved'`];
    if (KINDS.has(kind)) {
      params.push(kind);
      conditions.push(`s.kind = $${params.length}`);
    }
    const result = await pool.query(
      `SELECT s.submission_id, s.kind, s.name, s.description, s.original_filename,
              s.file_size, s.created_at, s.reviewed_at, u.username
       FROM tile_content_submissions s
       LEFT JOIN users u ON u.user_id = s.user_id
       WHERE ${conditions.join(' AND ')}
       ORDER BY s.reviewed_at DESC NULLS LAST, s.created_at DESC
       LIMIT 100`,
      params
    );
    res.json({ success: true, data: { items: result.rows.map(catalogRow) } });
  } catch (err) {
    console.error('player tile-content catalog:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.get('/:id/file', publicLimiter, optionalPlayer, async (req, res) => {
  try {
    const submissionId = parseInt(req.params.id, 10);
    if (!Number.isInteger(submissionId) || submissionId <= 0) {
      return res.status(400).json({ success: false, message: '无效的提交 ID' });
    }
    const result = await pool.query(
      `SELECT user_id, status, original_filename, storage_key
       FROM tile_content_submissions
       WHERE submission_id = $1`,
      [submissionId]
    );
    if (result.rows.length === 0) {
      return res.status(404).json({ success: false, message: '记录不存在' });
    }
    const row = result.rows[0];
    const isOwner = req.player && Number(req.player.userId) === Number(row.user_id);
    if (row.status !== 'approved' && !isOwner) {
      return res.status(404).json({ success: false, message: '记录不存在' });
    }
    const buf = store.readZipFile(row.storage_key);
    res.setHeader('Content-Type', 'application/zip');
    res.setHeader('Content-Disposition', contentDisposition(row.original_filename));
    res.send(buf);
  } catch (err) {
    const status = err.status || 500;
    if (status >= 500) console.error('player tile-content file:', err);
    res.status(status).json({ success: false, message: err.status ? err.message : '服务器内部错误' });
  }
});

router.use(requirePlayer);

router.get('/mine', async (req, res) => {
  try {
    const result = await pool.query(
      `SELECT submission_id, kind, name, description, original_filename, status, review_note,
              file_size, meta, created_at, updated_at, reviewed_at
       FROM tile_content_submissions
       WHERE user_id = $1
       ORDER BY created_at DESC
       LIMIT 50`,
      [req.player.userId]
    );
    res.json({ success: true, data: { items: result.rows.map(publicRow) } });
  } catch (err) {
    console.error('player tile-content mine:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

async function createOrReplace(req, res, { resubmitId }) {
  const kindParsed = parseKind(req.body?.kind);
  if (kindParsed.error) {
    return res.status(400).json({ success: false, message: kindParsed.error });
  }
  if (!req.file?.buffer) {
    return res.status(400).json({ success: false, message: '请选择 zip 文件' });
  }
  const filename = originalName(req.file);
  const nameParsed = parseName(req.body?.name, filename);
  if (nameParsed.error) {
    return res.status(400).json({ success: false, message: nameParsed.error });
  }
  const descParsed = parseDescription(req.body?.description);
  if (descParsed.error) {
    return res.status(400).json({ success: false, message: descParsed.error });
  }

  let existing = null;
  if (resubmitId) {
    const current = await pool.query(
      `SELECT * FROM tile_content_submissions
       WHERE submission_id = $1 AND user_id = $2 AND status = 'rejected'`,
      [resubmitId, req.player.userId]
    );
    if (current.rows.length === 0) {
      return res.status(400).json({ success: false, message: '只能重新提交本人被拒绝的内容' });
    }
    existing = current.rows[0];
    if (kindParsed.value !== existing.kind) {
      return res.status(400).json({ success: false, message: '不能更改上传类型' });
    }
  }

  const pending = await pool.query(
    `SELECT submission_id FROM tile_content_submissions
     WHERE user_id = $1 AND kind = $2 AND status = 'pending'
       AND ($3::bigint IS NULL OR submission_id <> $3)
     LIMIT 1`,
    [req.player.userId, kindParsed.value, resubmitId || null]
  );
  if (pending.rows.length > 0) {
    return res.status(400).json({
      success: false,
      message: `您已有一条待审核的${kindLabel(kindParsed.value)}，请等待处理后再提交`,
    });
  }

  let saved;
  try {
    saved = store.saveZip({
      kind: kindParsed.value,
      zipBuffer: req.file.buffer,
      storageKey: existing?.storage_key || undefined,
    });
  } catch (err) {
    const status = err.status || 400;
    return res.status(status).json({ success: false, message: err.message || '校验失败' });
  }

  try {
    let result;
    if (existing) {
      result = await pool.query(
        `UPDATE tile_content_submissions
         SET name = $1,
             description = $2,
             original_filename = $3,
             file_size = $4,
             meta = $5::jsonb,
             status = 'pending',
             review_note = NULL,
             reviewer_user_id = NULL,
             reviewed_at = NULL,
             updated_at = CURRENT_TIMESTAMP
         WHERE submission_id = $6 AND user_id = $7 AND status = 'rejected'
         RETURNING submission_id, kind, name, description, original_filename, status, review_note,
                   file_size, meta, created_at, updated_at, reviewed_at`,
        [
          nameParsed.value,
          descParsed.value || existing.description || '',
          filename,
          saved.fileSize,
          JSON.stringify(saved.meta),
          resubmitId,
          req.player.userId,
        ]
      );
    } else {
      result = await pool.query(
        `INSERT INTO tile_content_submissions
           (user_id, kind, name, description, original_filename, storage_key, status, file_size, meta)
         VALUES ($1, $2, $3, $4, $5, $6, 'pending', $7, $8::jsonb)
         RETURNING submission_id, kind, name, description, original_filename, status, review_note,
                   file_size, meta, created_at, updated_at, reviewed_at`,
        [
          req.player.userId,
          kindParsed.value,
          nameParsed.value,
          descParsed.value,
          filename,
          saved.storageKey,
          saved.fileSize,
          JSON.stringify(saved.meta),
        ]
      );
    }
    if (result.rows.length === 0) {
      if (!existing) store.removePack(saved.storageKey);
      return res.status(400).json({ success: false, message: '提交失败' });
    }
    res.json({
      success: true,
      data: publicRow(result.rows[0]),
      message: existing ? '已重新提交，请等待管理员审核' : '已提交，请等待管理员审核',
    });
  } catch (err) {
    if (!existing && saved?.storageKey) store.removePack(saved.storageKey);
    if (err.code === '23505') {
      return res.status(400).json({
        success: false,
        message: `您已有一条待审核的${kindLabel(kindParsed.value)}，请等待处理后再提交`,
      });
    }
    console.error('player tile-content save:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
}

router.post('/', uploadLimiter, sendMulter, async (req, res) => {
  await createOrReplace(req, res, { resubmitId: null });
});

router.post('/:id/resubmit', uploadLimiter, sendMulter, async (req, res) => {
  const submissionId = parseInt(req.params.id, 10);
  if (!Number.isInteger(submissionId) || submissionId <= 0) {
    return res.status(400).json({ success: false, message: '无效的提交 ID' });
  }
  await createOrReplace(req, res, { resubmitId: submissionId });
});

module.exports = router;
