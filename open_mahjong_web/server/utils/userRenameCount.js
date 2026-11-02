const pool = require('../config/database');

async function ensureUserRenameCountColumn(db = pool) {
  // Retained only for the explicit one-time card migration. Never seed credits.
  await db.query(`
    ALTER TABLE users ADD COLUMN IF NOT EXISTS rename_count INTEGER NOT NULL DEFAULT 0
  `);
}

module.exports = { ensureUserRenameCountColumn };
