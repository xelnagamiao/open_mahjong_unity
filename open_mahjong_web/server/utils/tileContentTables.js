const pool = require('../config/database');

async function ensureTileContentTables() {
  await pool.query(`
    CREATE TABLE IF NOT EXISTS tile_content_submissions (
      submission_id BIGSERIAL PRIMARY KEY,
      user_id BIGINT NOT NULL,
      kind VARCHAR(32) NOT NULL,
      name VARCHAR(64) NOT NULL DEFAULT '',
      original_filename VARCHAR(255) NOT NULL,
      storage_key VARCHAR(64) NOT NULL UNIQUE,
      status VARCHAR(16) NOT NULL DEFAULT 'pending',
      review_note TEXT,
      reviewer_user_id BIGINT,
      file_size INT NOT NULL DEFAULT 0,
      meta JSONB NOT NULL DEFAULT '{}'::jsonb,
      created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      reviewed_at TIMESTAMP NULL,
      CONSTRAINT tile_content_kind_chk CHECK (kind IN ('tile_face', 'tile_background')),
      CONSTRAINT tile_content_status_chk CHECK (status IN ('pending', 'approved', 'rejected'))
    )
  `);
  await pool.query(`
    ALTER TABLE tile_content_submissions
      ADD COLUMN IF NOT EXISTS description TEXT NOT NULL DEFAULT ''
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_tile_content_status_created
      ON tile_content_submissions (status, created_at DESC)
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_tile_content_user_created
      ON tile_content_submissions (user_id, created_at DESC)
  `);
  await pool.query(`
    CREATE UNIQUE INDEX IF NOT EXISTS idx_tile_content_one_pending
      ON tile_content_submissions (user_id, kind)
      WHERE status = 'pending'
  `);
}

module.exports = { ensureTileContentTables };
