const pool = require('../config/database');

async function ensureAuditTable() {
  await pool.query(`
    CREATE TABLE IF NOT EXISTS admin_audit_log (
      id BIGSERIAL PRIMARY KEY,
      admin_user_id BIGINT NOT NULL,
      action VARCHAR(64) NOT NULL,
      target_type VARCHAR(32),
      target_id VARCHAR(64),
      payload JSONB,
      reason TEXT,
      created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_admin_audit_created
    ON admin_audit_log (created_at DESC);
  `);
}

async function writeAudit({
  adminUserId,
  action,
  targetType = null,
  targetId = null,
  payload = null,
  reason = null,
}) {
  await pool.query(
    `INSERT INTO admin_audit_log
      (admin_user_id, action, target_type, target_id, payload, reason)
     VALUES ($1, $2, $3, $4, $5::jsonb, $6)`,
    [
      adminUserId,
      action,
      targetType,
      targetId != null ? String(targetId) : null,
      payload ? JSON.stringify(payload) : null,
      reason,
    ]
  );
}

module.exports = { ensureAuditTable, writeAudit };
