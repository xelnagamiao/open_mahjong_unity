const express = require('express');
const router = express.Router();
const pool = require('../../config/database');
const { writeAudit } = require('../../utils/audit');

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

function normalizeIp(ip) {
  const text = String(ip || '').trim();
  if (!text) return { error: '请输入 IP 地址' };
  if (text.length > 45) return { error: 'IP 地址过长' };
  return { value: text };
}

router.get('/', async (req, res) => {
  try {
    const page = Math.max(1, parseInt(req.query.page, 10) || 1);
    const limit = Math.min(100, Math.max(1, parseInt(req.query.limit, 10) || 50));
    const offset = (page - 1) * limit;
    const result = await pool.query(
      `SELECT id, ip_address, ban_expires_at, ban_reason, admin_note, created_by, created_at, updated_at
       FROM ip_bans
       ORDER BY updated_at DESC, id DESC
       LIMIT $1 OFFSET $2`,
      [limit, offset]
    );
    const countRes = await pool.query(`SELECT COUNT(*)::int AS cnt FROM ip_bans`);
    res.json({
      success: true,
      data: {
        items: result.rows,
        page,
        limit,
        total: countRes.rows[0].cnt,
      },
    });
  } catch (err) {
    console.error('admin ip_bans list:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.post('/', async (req, res) => {
  try {
    const { ip_address, ban_expires_at, ban_reason, reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写操作原因' });
    }
    const ipParsed = normalizeIp(ip_address);
    if (ipParsed.error) {
      return res.status(400).json({ success: false, message: ipParsed.error });
    }
    const expiresParsed = parseBanExpiresAt(ban_expires_at);
    if (expiresParsed?.error) {
      return res.status(400).json({ success: false, message: expiresParsed.error });
    }
    const banReason = ban_reason === null || ban_reason === undefined
      ? null
      : String(ban_reason).trim() || null;

    const result = await pool.query(
      `INSERT INTO ip_bans (ip_address, ban_expires_at, ban_reason, created_by, updated_at)
       VALUES ($1, $2, $3, $4, CURRENT_TIMESTAMP)
       ON CONFLICT (ip_address) DO UPDATE SET
         ban_expires_at = EXCLUDED.ban_expires_at,
         ban_reason = EXCLUDED.ban_reason,
         created_by = EXCLUDED.created_by,
         updated_at = CURRENT_TIMESTAMP
       RETURNING id, ip_address, ban_expires_at, ban_reason, created_by, created_at, updated_at`,
      [ipParsed.value, expiresParsed.value, banReason, req.admin.userId]
    );

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'ip_ban.upsert',
      targetType: 'ip',
      targetId: ipParsed.value,
      payload: { after: result.rows[0] },
      reason: String(reason).trim(),
    });

    res.json({ success: true, data: result.rows[0] });
  } catch (err) {
    console.error('admin ip_bans create:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

router.patch('/:id/note', async (req, res) => {
  const { admin_note, reason } = req.body || {};
  if (!/^\d{1,19}$/.test(req.params.id) || BigInt(req.params.id) > 9223372036854775807n) {
    return res.status(400).json({ success: false, message: '无效的封禁记录 ID' });
  }
  if (typeof admin_note !== 'string' || admin_note.length > 2000) {
    return res.status(400).json({ success: false, message: '备注须为 2000 字以内的文本' });
  }
  if (typeof reason !== 'string' || !reason.trim()) {
    return res.status(400).json({ success: false, message: '请填写变更原因' });
  }

  let client;
  let connectionError;
  try {
    client = await pool.connect();
    await client.query('BEGIN');
    const { rows: [before] } = await client.query(
      'SELECT ip_address, admin_note FROM ip_bans WHERE id = $1 FOR UPDATE', [req.params.id]
    );
    if (!before) {
      await client.query('ROLLBACK');
      return res.status(404).json({ success: false, message: '该封禁记录已不存在，请刷新列表' });
    }
    await client.query('UPDATE ip_bans SET admin_note = $1 WHERE id = $2', [admin_note, req.params.id]);
    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'ip_ban.note',
      targetType: 'ip',
      targetId: before.ip_address,
      payload: { before: { admin_note: before.admin_note }, after: { admin_note } },
      reason: reason.trim(),
    }, client);
    await client.query('COMMIT');
    res.json({ success: true, data: { admin_note } });
  } catch (err) {
    if (client) {
      try { await client.query('ROLLBACK'); } catch (rollbackError) { connectionError = rollbackError; }
    }
    console.error('admin ip_bans note:', err);
    res.status(500).json({ success: false, message: '保存 IP 备注失败' });
  } finally {
    client?.release(connectionError);
  }
});

router.delete('/:ipAddress', async (req, res) => {
  try {
    const { reason } = req.body || {};
    if (!reason || !String(reason).trim()) {
      return res.status(400).json({ success: false, message: '请填写操作原因' });
    }
    const ipParsed = normalizeIp(decodeURIComponent(req.params.ipAddress));
    if (ipParsed.error) {
      return res.status(400).json({ success: false, message: ipParsed.error });
    }

    const before = await pool.query(
      `SELECT id, ip_address, ban_expires_at, ban_reason FROM ip_bans WHERE ip_address = $1`,
      [ipParsed.value]
    );
    if (before.rows.length === 0) {
      return res.status(404).json({ success: false, message: '该 IP 未被封禁' });
    }

    await pool.query(`DELETE FROM ip_bans WHERE ip_address = $1`, [ipParsed.value]);

    await writeAudit({
      adminUserId: req.admin.userId,
      action: 'ip_ban.delete',
      targetType: 'ip',
      targetId: ipParsed.value,
      payload: { before: before.rows[0] },
      reason: String(reason).trim(),
    });

    res.json({ success: true, message: '已解除 IP 封禁' });
  } catch (err) {
    console.error('admin ip_bans delete:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
