-- Normal schema indexes, required for fresh databases as well as existing ones.
-- Existing production indexes were created concurrently during maintenance.
CREATE INDEX IF NOT EXISTS idx_game_records_created_game
    ON game_records (created_at DESC, game_id DESC);
CREATE INDEX IF NOT EXISTS idx_game_player_records_user_game
    ON game_player_records (user_id, game_id);
CREATE INDEX IF NOT EXISTS idx_game_player_records_room_tier_game
    ON game_player_records (room_type, match_tier, game_id);
CREATE INDEX IF NOT EXISTS idx_game_player_records_event_game
    ON game_player_records (event_id, game_id) WHERE room_type = 'events';
CREATE STATISTICS IF NOT EXISTS stat_game_player_records_room_tier (mcv, dependencies)
    ON room_type, match_tier FROM game_player_records;
