const DUPLICATE_SCHEMA_SQL = `
CREATE TABLE IF NOT EXISTS duplicate_walls (
  id BIGSERIAL PRIMARY KEY,
  key VARCHAR(40) NOT NULL UNIQUE,
  owner_user_id BIGINT NOT NULL,
  event_id VARCHAR(32) NULL,
  scope VARCHAR(16) NOT NULL CHECK (scope IN ('personal', 'event')),
  wall_type VARCHAR(16) NOT NULL CHECK (wall_type IN ('manual', 'seed', 'key')),
  rule VARCHAR(32) NOT NULL,
  name VARCHAR(80) NOT NULL DEFAULT '',
  tiles JSONB NOT NULL CHECK (jsonb_typeof(tiles) = 'array'),
  seed TEXT NULL,
  use_flowers BOOLEAN NOT NULL DEFAULT TRUE,
  is_unlocked BOOLEAN NOT NULL DEFAULT FALSE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
  unlocked_at TIMESTAMPTZ NULL,
  ended_at TIMESTAMPTZ NULL,
  deleted_at TIMESTAMPTZ NULL,
  CHECK ((scope = 'personal' AND event_id IS NULL) OR (scope = 'event' AND event_id IS NOT NULL))
);
-- Add nullable first so pre-existing walls inherit their actual stored tile set.
ALTER TABLE duplicate_walls ADD COLUMN IF NOT EXISTS use_flowers BOOLEAN;
UPDATE duplicate_walls SET use_flowers = EXISTS (
  SELECT 1 FROM jsonb_array_elements(tiles) tile WHERE tile IN ('51'::jsonb, '52'::jsonb, '53'::jsonb, '54'::jsonb, '55'::jsonb, '56'::jsonb, '57'::jsonb, '58'::jsonb)
) WHERE use_flowers IS NULL;
ALTER TABLE duplicate_walls ALTER COLUMN use_flowers SET DEFAULT TRUE;
ALTER TABLE duplicate_walls ALTER COLUMN use_flowers SET NOT NULL;
ALTER TABLE duplicate_walls ADD COLUMN IF NOT EXISTS round_count INTEGER NOT NULL DEFAULT 1
  CHECK (round_count IN (1, 4, 8, 12, 16));
ALTER TABLE duplicate_walls ADD COLUMN IF NOT EXISTS round_tiles JSONB
  CHECK (round_tiles IS NULL OR (jsonb_typeof(round_tiles) = 'array' AND jsonb_array_length(round_tiles) = round_count));
CREATE INDEX IF NOT EXISTS idx_duplicate_walls_owner ON duplicate_walls(owner_user_id, scope, created_at);
CREATE INDEX IF NOT EXISTS idx_duplicate_walls_event ON duplicate_walls(event_id, created_at);
CREATE TABLE IF NOT EXISTS duplicate_games (
  game_id VARCHAR(16) PRIMARY KEY,
  wall_id BIGINT NOT NULL REFERENCES duplicate_walls(id),
  ended_at TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS idx_duplicate_games_wall ON duplicate_games(wall_id);
`;

async function ensureDuplicateTables(db = require('../config/database')) {
  await db.query(DUPLICATE_SCHEMA_SQL);
}

module.exports = { DUPLICATE_SCHEMA_SQL, ensureDuplicateTables };
