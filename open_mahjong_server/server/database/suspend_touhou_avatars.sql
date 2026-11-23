-- Run after the inventory seed. Standalone execution on an existing DB is also safe.
-- The marker and all data changes commit together. A later release can re-enable
-- these definitions without subsequent server restarts unequipping players again.
DO $migration$
DECLARE
    blocked_ids integer[];
BEGIN
    -- Same lock as inventory.ensure_inventory_tables and save_definition.
    PERFORM pg_advisory_xact_lock(220922041);
    CREATE TABLE IF NOT EXISTS app_meta (
        meta_key VARCHAR(100) PRIMARY KEY, meta_value TEXT,
        updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    );
    INSERT INTO app_meta(meta_key, meta_value)
        VALUES ('inventory.suspend_touhou_avatars.v1', 'done')
        ON CONFLICT (meta_key) DO NOTHING;
    IF NOT FOUND THEN RETURN; END IF;

    SELECT ARRAY[2302, 2303] || ARRAY(
        SELECT item_id FROM item_definitions
        WHERE asset_key IN ('avatar.reimu', 'avatar.cirno')
    ) INTO blocked_ids;
    UPDATE item_definitions SET use_enabled = FALSE, grant_enabled = FALSE,
        updated_at = CURRENT_TIMESTAMP
        WHERE item_id = ANY(blocked_ids) AND (use_enabled OR grant_enabled);
    DELETE FROM user_equipment WHERE slot = 'avatar' AND item_id = ANY(blocked_ids);

    -- Older accounts may have only the settings projection, or it may lag behind
    -- a valid equipped avatar. The default item 2301 is stored as profile image 1.
    UPDATE user_settings s SET profile_image_id = COALESCE((
        SELECT NULLIF(e.item_id, 2301) FROM user_equipment e
        JOIN item_definitions d USING (item_id)
        LEFT JOIN user_inventory i ON i.user_id = e.user_id AND i.item_id = e.item_id
        WHERE e.user_id = s.user_id AND e.slot = 'avatar' AND d.slot = 'avatar'
            AND d.use_enabled AND (d.is_default OR i.quantity > 0)
    ), 1)
    WHERE s.profile_image_id = ANY(blocked_ids);
END;
$migration$;
