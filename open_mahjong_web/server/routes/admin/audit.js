const express = require('express');
const router = express.Router();
const pool = require('../../config/database');

router.get('/', async (req, res) => {
  try {
    const page = Math.max(1, parseInt(req.query.page, 10) || 1);
    const limit = Math.min(100, Math.max(1, parseInt(req.query.limit, 10) || 30));
    const offset = (page - 1) * limit;
    const adminId = req.query.admin_id ? parseInt(req.query.admin_id, 10) : null;
    const action = (req.query.action || '').trim();

    const conditions = [];
    const params = [];
    let idx = 1;

    if (adminId && !Number.isNaN(adminId)) {
      conditions.push(`admin_user_id = $${idx++}`);
      params.push(adminId);
    }
    if (action) {
      conditions.push(`action ILIKE $${idx++}`);
      params.push(`%${action}%`);
    }

    const where = conditions.length ? `WHERE ${conditions.join(' AND ')}` : '';
    params.push(limit, offset);

    const result = await pool.query(
      `SELECT id, admin_user_id, action, target_type, target_id, payload, reason, created_at
       FROM admin_audit_log
       ${where}
       ORDER BY created_at DESC
       LIMIT $${idx++} OFFSET $${idx}`,
      params
    );

    res.json({
      success: true,
      data: { items: result.rows, page, limit },
    });
  } catch (err) {
    console.error('admin audit list:', err);
    res.status(500).json({ success: false, message: '服务器内部错误' });
  }
});

module.exports = router;
