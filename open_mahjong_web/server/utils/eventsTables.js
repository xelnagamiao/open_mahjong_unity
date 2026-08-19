const pool = require('../config/database');

async function ensureEventsTables() {
  await pool.query(`
    CREATE TABLE IF NOT EXISTS events (
      event_id   VARCHAR(32) PRIMARY KEY,
      name       VARCHAR(128) NOT NULL,
      description TEXT NOT NULL DEFAULT '',
      status     VARCHAR(16) NOT NULL DEFAULT 'registered',
      reopen_requested BOOLEAN NOT NULL DEFAULT FALSE,
      created_by BIGINT NOT NULL,
      closed_at  TIMESTAMP NULL,
      created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      kind VARCHAR(16) NOT NULL DEFAULT 'event',
      entry_config JSONB NOT NULL DEFAULT '{}'::jsonb,
      CONSTRAINT events_status_chk CHECK (status IN ('registered', 'active', 'closed')),
      CONSTRAINT events_kind_chk CHECK (kind IN ('event', 'base'))
    );
  `);
  await pool.query(`
    ALTER TABLE events
      ADD COLUMN IF NOT EXISTS description TEXT NOT NULL DEFAULT ''
  `);
  await pool.query(`
    ALTER TABLE events
      ADD COLUMN IF NOT EXISTS reopen_requested BOOLEAN NOT NULL DEFAULT FALSE
  `);
  // 扩展状态：registered | active | closed
  await pool.query(`
    ALTER TABLE events DROP CONSTRAINT IF EXISTS events_status_chk
  `);
  await pool.query(`
    ALTER TABLE events
      ADD CONSTRAINT events_status_chk
      CHECK (status IN ('registered', 'active', 'closed'))
  `);
  await pool.query(`
    ALTER TABLE events ALTER COLUMN status SET DEFAULT 'registered'
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_events_status ON events(status);
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_events_created_at ON events(created_at DESC);
  `);
  await pool.query(`
    CREATE TABLE IF NOT EXISTS event_admins (
      event_id   VARCHAR(32) NOT NULL REFERENCES events(event_id) ON DELETE CASCADE,
      user_id    BIGINT NOT NULL,
      role       VARCHAR(16) NOT NULL,
      added_by   BIGINT NOT NULL,
      created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      PRIMARY KEY (event_id, user_id),
      CONSTRAINT event_admins_role_chk CHECK (role IN ('owner', 'admin'))
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_event_admins_user ON event_admins(user_id);
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS event_applications (
      application_id     BIGSERIAL PRIMARY KEY,
      applicant_user_id  BIGINT NOT NULL,
      name               VARCHAR(128) NOT NULL,
      description        TEXT NOT NULL DEFAULT '',
      remark             TEXT NOT NULL DEFAULT '',
      reason             TEXT NOT NULL DEFAULT '',
      status             VARCHAR(16) NOT NULL DEFAULT 'pending',
      reviewer_user_id   BIGINT NULL,
      review_note        TEXT NULL,
      event_id           VARCHAR(32) NULL REFERENCES events(event_id) ON DELETE SET NULL,
      created_at         TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      updated_at         TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      reviewed_at        TIMESTAMP NULL,
      planned_start_at   DATE NULL,
      planned_end_at     DATE NULL,
      kind               VARCHAR(16) NOT NULL DEFAULT 'event',
      CONSTRAINT event_applications_status_chk
        CHECK (status IN ('pending', 'approved', 'rejected', 'cancelled')),
      CONSTRAINT event_applications_kind_chk CHECK (kind IN ('event', 'base'))
    );
  `);
  await pool.query(`
    ALTER TABLE event_applications
      ADD COLUMN IF NOT EXISTS description TEXT NOT NULL DEFAULT ''
  `);
  await pool.query(`
    ALTER TABLE event_applications
      ADD COLUMN IF NOT EXISTS remark TEXT NOT NULL DEFAULT ''
  `);
  await pool.query(`
    ALTER TABLE event_applications
      ADD COLUMN IF NOT EXISTS planned_start_at DATE NULL
  `);
  await pool.query(`
    ALTER TABLE event_applications
      ADD COLUMN IF NOT EXISTS planned_end_at DATE NULL
  `);
  await pool.query(`
    UPDATE event_applications
       SET description = reason
     WHERE (description IS NULL OR description = '')
       AND reason IS NOT NULL
       AND reason <> ''
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_event_applications_applicant
      ON event_applications(applicant_user_id, created_at DESC);
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_event_applications_status
      ON event_applications(status, created_at DESC);
  `);

  await pool.query(`
    UPDATE events e
       SET description = COALESCE(NULLIF(a.description, ''), NULLIF(a.reason, ''), e.description)
      FROM event_applications a
     WHERE a.event_id = e.event_id
       AND a.status = 'approved'
       AND (e.description IS NULL OR e.description = '')
       AND (
         (a.description IS NOT NULL AND a.description <> '')
         OR (a.reason IS NOT NULL AND a.reason <> '')
       )
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS event_announcements (
      announcement_id BIGSERIAL PRIMARY KEY,
      event_id        VARCHAR(32) NOT NULL REFERENCES events(event_id) ON DELETE CASCADE,
      title           VARCHAR(200) NOT NULL,
      body            TEXT NOT NULL,
      created_by      BIGINT NOT NULL,
      created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      updated_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_event_announcements_event
      ON event_announcements(event_id, created_at DESC);
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS event_profile_change_requests (
      request_id            BIGSERIAL PRIMARY KEY,
      event_id              VARCHAR(32) NOT NULL REFERENCES events(event_id) ON DELETE CASCADE,
      requested_by          BIGINT NOT NULL,
      proposed_name         VARCHAR(128) NOT NULL,
      proposed_description  TEXT NOT NULL DEFAULT '',
      reason                TEXT NOT NULL DEFAULT '',
      status                VARCHAR(16) NOT NULL DEFAULT 'pending',
      reviewer_user_id      BIGINT NULL,
      review_note           TEXT NULL,
      created_at            TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      updated_at            TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      reviewed_at           TIMESTAMP NULL,
      CONSTRAINT event_profile_change_status_chk
        CHECK (status IN ('pending', 'approved', 'rejected', 'cancelled'))
    );
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_event_profile_change_event
      ON event_profile_change_requests(event_id, created_at DESC);
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_event_profile_change_status
      ON event_profile_change_requests(status, created_at DESC);
  `);
  await pool.query(`
    CREATE UNIQUE INDEX IF NOT EXISTS idx_event_profile_change_one_pending
      ON event_profile_change_requests(event_id)
      WHERE status = 'pending';
  `);

  await pool.query(`
    ALTER TABLE events
      ADD COLUMN IF NOT EXISTS kind VARCHAR(16) NOT NULL DEFAULT 'event'
  `);
  await pool.query(`ALTER TABLE events DROP CONSTRAINT IF EXISTS events_kind_chk`);
  await pool.query(`
    ALTER TABLE events
      ADD CONSTRAINT events_kind_chk CHECK (kind IN ('event', 'base'))
  `);
  await pool.query(`
    ALTER TABLE events
      ADD COLUMN IF NOT EXISTS entry_config JSONB NOT NULL DEFAULT '{}'::jsonb
  `);
  await pool.query(`CREATE INDEX IF NOT EXISTS idx_events_kind_status ON events(kind, status)`);

  await pool.query(`
    ALTER TABLE event_applications
      ADD COLUMN IF NOT EXISTS kind VARCHAR(16) NOT NULL DEFAULT 'event'
  `);
  await pool.query(`ALTER TABLE event_applications DROP CONSTRAINT IF EXISTS event_applications_kind_chk`);
  await pool.query(`
    ALTER TABLE event_applications
      ADD CONSTRAINT event_applications_kind_chk CHECK (kind IN ('event', 'base'))
  `);
  // 必须在 kind 列存在之后再建：办赛 / 基地各允许一条待审
  await pool.query(`DROP INDEX IF EXISTS idx_event_applications_one_pending`);
  await pool.query(`
    CREATE UNIQUE INDEX IF NOT EXISTS idx_event_applications_one_pending_kind
      ON event_applications(applicant_user_id, kind)
      WHERE status = 'pending';
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS event_registrations (
      event_id     VARCHAR(32) NOT NULL REFERENCES events(event_id) ON DELETE CASCADE,
      user_id      BIGINT NOT NULL,
      status       VARCHAR(16) NOT NULL DEFAULT 'pending',
      contact      TEXT NOT NULL DEFAULT '',
      remark       TEXT NOT NULL DEFAULT '',
      reviewed_by  BIGINT NULL,
      review_note  TEXT NULL,
      created_at   TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      updated_at   TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      PRIMARY KEY (event_id, user_id),
      CONSTRAINT event_registrations_status_chk
        CHECK (status IN ('pending', 'approved', 'rejected', 'cancelled'))
    )
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_event_registrations_event_status
      ON event_registrations(event_id, status, created_at DESC)
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_event_registrations_user
      ON event_registrations(user_id, created_at DESC)
  `);

  await pool.query(`
    CREATE TABLE IF NOT EXISTS event_ready_pool (
      event_id  VARCHAR(32) NOT NULL REFERENCES events(event_id) ON DELETE CASCADE,
      user_id   BIGINT NOT NULL,
      ready_at  TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
      PRIMARY KEY (event_id, user_id)
    )
  `);
  await pool.query(`
    CREATE INDEX IF NOT EXISTS idx_event_ready_pool_event
      ON event_ready_pool(event_id, ready_at)
  `);
}

const EVENT_ID_ALPHABET = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz';

function generateEventId(length = 10) {
  let body = '';
  for (let i = 0; i < length; i += 1) {
    body += EVENT_ID_ALPHABET[Math.floor(Math.random() * EVENT_ID_ALPHABET.length)];
  }
  return `evt_${body}`;
}

module.exports = { ensureEventsTables, generateEventId };
