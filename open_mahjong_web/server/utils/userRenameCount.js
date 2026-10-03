const pool = require('../config/database');

async function ensureUserRenameCountColumn(db = pool) {
  try {
    await db.query(`
      ALTER TABLE users
        ADD COLUMN rename_count INTEGER NOT NULL DEFAULT 0
    `);
  } catch (err) {
    if (err.code === '42701') {
      return;
    }
    throw err;
  }
  await db.query(`
    UPDATE users
       SET rename_count = 1
     WHERE COALESCE(is_tourist, FALSE) = FALSE
  `);
}

module.exports = { ensureUserRenameCountColumn };
