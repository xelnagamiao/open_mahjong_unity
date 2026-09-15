const pool = require('../config/database');

async function ensureUserEmailTables() {
  await pool.query(`
    CREATE TABLE IF NOT EXISTS password_reset_codes (
      email VARCHAR(255) PRIMARY KEY,
      user_id BIGINT NOT NULL,
      code_hash VARCHAR(64) NOT NULL,
      password_digest VARCHAR(64) NOT NULL,
      expires_at TIMESTAMP NOT NULL,
      created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
      attempts INTEGER NOT NULL DEFAULT 0
    )
  `);
  await pool.query(`
    ALTER TABLE users
      ADD COLUMN IF NOT EXISTS email VARCHAR(255) NULL
  `);
  await pool.query(`
    ALTER TABLE users
      ADD COLUMN IF NOT EXISTS email_verified_at TIMESTAMP NULL
  `);
  await pool.query(`
    CREATE UNIQUE INDEX IF NOT EXISTS idx_users_email_verified_unique
      ON users (LOWER(email))
      WHERE email IS NOT NULL AND email_verified_at IS NOT NULL
  `);
  await pool.query(`
    CREATE TABLE IF NOT EXISTS email_bind_codes (
      user_id    BIGINT PRIMARY KEY,
      email      VARCHAR(255) NOT NULL,
      code       VARCHAR(16) NOT NULL,
      expires_at TIMESTAMP NOT NULL,
      created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )
  `);
}

module.exports = { ensureUserEmailTables };
