const pool = require('../config/database');

const LADDER_PASS_COLUMNS = [
  'is_beginner_qualified',
  'is_intermediate_qualified',
  'is_advanced_qualified',
];

async function ensureUserLadderPassColumns() {
  for (const column of LADDER_PASS_COLUMNS) {
    await pool.query(`
      ALTER TABLE users
        ADD COLUMN IF NOT EXISTS ${column} BOOLEAN NOT NULL DEFAULT FALSE
    `);
  }
}

module.exports = { ensureUserLadderPassColumns, LADDER_PASS_COLUMNS };
