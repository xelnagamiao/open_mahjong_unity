const pool = require('../config/database');

const LADDER_PASS_COLUMNS = [
  'is_beginner_qualified',
  'is_intermediate_qualified',
  'is_advanced_qualified',
  'is_mcrpl_qualified',
];

const SPONSOR_AMOUNT_COLUMN = 'sponsor_amount';
const ADMIN_NOTE_COLUMN = 'admin_note';

async function ensureUserLadderPassColumns() {
  for (const column of LADDER_PASS_COLUMNS) {
    await pool.query(`
      ALTER TABLE users
        ADD COLUMN IF NOT EXISTS ${column} BOOLEAN NOT NULL DEFAULT FALSE
    `);
  }
  // Older permissions have no origin recorded. Protect those players on the
  // first migration only, so later bulk grants remain ordinary sponsor grants.
  await pool.query(`
    DO $migration$
    BEGIN
      ALTER TABLE users ADD COLUMN has_special_settings BOOLEAN NOT NULL DEFAULT FALSE;
      UPDATE users SET has_special_settings = TRUE WHERE ${LADDER_PASS_COLUMNS.join(' OR ')};
    EXCEPTION WHEN duplicate_column THEN
      NULL;
    END $migration$
  `);
}

async function ensureSponsorAmountColumn() {
  await pool.query(`
    ALTER TABLE users
      ADD COLUMN IF NOT EXISTS ${SPONSOR_AMOUNT_COLUMN} NUMERIC(12, 2) NOT NULL DEFAULT 0
  `);
}

async function ensureAdminNoteColumn() {
  await pool.query(`
    ALTER TABLE users
      ADD COLUMN IF NOT EXISTS ${ADMIN_NOTE_COLUMN} TEXT NOT NULL DEFAULT ''
  `);
}

module.exports = {
  ensureUserLadderPassColumns,
  ensureSponsorAmountColumn,
  ensureAdminNoteColumn,
  LADDER_PASS_COLUMNS,
  SPONSOR_AMOUNT_COLUMN,
  ADMIN_NOTE_COLUMN,
};
