-- Run explicitly with psql, outside any BEGIN/COMMIT block. Never run this in
-- application startup. CONCURRENTLY keeps ordinary game reads/writes available.
-- Inspect pg_indexes for equivalent indexes before applying on an existing DB.
-- IF NOT EXISTS checks names only; also check pg_index.indisvalid after a retry.
SET lock_timeout = '3s';
SET statement_timeout = '5min';

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_game_records_created_game
  ON game_records (created_at DESC, game_id DESC);

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_game_player_records_user_game
  ON game_player_records (user_id, game_id);

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_game_player_records_room_tier_game
  ON game_player_records (room_type, match_tier, game_id);

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_game_player_records_event_game
  ON game_player_records (event_id, game_id) WHERE room_type = 'events';

CREATE STATISTICS IF NOT EXISTS stat_game_player_records_room_tier (mcv, dependencies)
  ON room_type, match_tier FROM game_player_records;

ANALYZE game_player_records;
ANALYZE game_records;
ANALYZE duplicate_games;
ANALYZE duplicate_walls;

RESET statement_timeout;
RESET lock_timeout;
