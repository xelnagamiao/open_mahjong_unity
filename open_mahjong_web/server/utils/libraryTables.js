const pool = require('../config/database');

async function ensureLibraryTables() {
  await pool.query(`
    CREATE TABLE IF NOT EXISTS library_posts (
      post_id         BIGSERIAL PRIMARY KEY,
      rule_key        VARCHAR(64) NOT NULL,
      title           VARCHAR(200) NOT NULL,
      body            TEXT NOT NULL,
      author_user_id  BIGINT NOT NULL,
      created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      updated_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_library_posts_rule_updated
      ON library_posts(rule_key, updated_at DESC);
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_library_posts_author
      ON library_posts(author_user_id);
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS library_replies (
      reply_id        BIGSERIAL PRIMARY KEY,
      post_id         BIGINT NOT NULL REFERENCES library_posts(post_id) ON DELETE CASCADE,
      body            TEXT NOT NULL,
      author_user_id  BIGINT NOT NULL,
      created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      updated_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_library_replies_post
      ON library_replies(post_id, created_at ASC);
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_library_replies_author
      ON library_replies(author_user_id);
  `);
}

module.exports = { ensureLibraryTables };
