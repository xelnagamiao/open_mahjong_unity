-- One-time store consolidation requested on 2026-09-23. Keep the rename card and
-- the initial title; archive removed catalog/ownership data in the same transaction.
-- A durable marker prevents a restart from deleting later administrator additions.
DO $store$
DECLARE
    kept_title INTEGER;
    archived JSONB;
BEGIN
    IF EXISTS (SELECT 1 FROM admin_audit_log WHERE action = 'store.catalog_reset'
               AND target_id = '20260923_minimal_store') THEN
        RETURN;
    END IF;
    SELECT title_id INTO kept_title FROM titles WHERE name IN ('最初的初段','初段')
        ORDER BY (name = '最初的初段') DESC, title_id LIMIT 1;
    IF kept_title IS NULL THEN kept_title := 2; END IF;
    SELECT jsonb_build_object(
        'titles', COALESCE((SELECT jsonb_agg(t) FROM titles t), '[]'::jsonb),
        'user_titles', COALESCE((SELECT jsonb_agg(t) FROM user_titles t WHERE title_id <> kept_title), '[]'::jsonb),
        'item_definitions', COALESCE((SELECT jsonb_agg(t) FROM item_definitions t), '[]'::jsonb),
        'user_inventory', COALESCE((SELECT jsonb_agg(t) FROM user_inventory t WHERE item_id <> 3001), '[]'::jsonb),
        'user_equipment', COALESCE((SELECT jsonb_agg(t) FROM user_equipment t), '[]'::jsonb),
        'inventory_ledger', COALESCE((SELECT jsonb_agg(t) FROM inventory_ledger t WHERE item_id <> 3001), '[]'::jsonb),
        'user_settings', COALESCE((SELECT jsonb_agg(t) FROM (
            SELECT user_id,title_id,character_id,voice_id,profile_image_id FROM user_settings
            WHERE title_id NOT IN (1,kept_title) OR user_id IN (SELECT user_id FROM user_equipment)
               OR profile_image_id IN (SELECT item_id FROM item_definitions WHERE slot = 'avatar' AND item_id <> 3001)
        ) t), '[]'::jsonb)
    ) INTO archived;

    UPDATE user_settings SET title_id = 1, updated_at = CURRENT_TIMESTAMP WHERE title_id NOT IN (1,kept_title);
    UPDATE user_settings SET character_id = 1, voice_id = 1, updated_at = CURRENT_TIMESTAMP
        WHERE user_id IN (SELECT user_id FROM user_equipment WHERE slot = 'character');
    UPDATE user_settings SET profile_image_id = 1, updated_at = CURRENT_TIMESTAMP
        WHERE user_id IN (SELECT user_id FROM user_equipment WHERE slot = 'avatar')
           OR profile_image_id IN (SELECT item_id FROM item_definitions WHERE slot = 'avatar' AND item_id <> 3001);
    DELETE FROM user_equipment;
    DELETE FROM user_inventory WHERE item_id <> 3001;
    DELETE FROM inventory_ledger WHERE item_id <> 3001;
    DELETE FROM item_definitions WHERE item_id <> 3001;
    DELETE FROM user_titles WHERE title_id <> kept_title;
    DELETE FROM titles WHERE title_id <> kept_title;
    UPDATE titles SET name = '最初的初段', description = '初始头衔', is_enabled = TRUE,
        updated_at = CURRENT_TIMESTAMP WHERE title_id = kept_title;
    UPDATE item_definitions SET name = '改名卡', description = '使用后增加 1 次账号改名机会，可在网页账号中心改名。',
        category = 'item', asset_key = 'item.rename_credit', slot = '', is_default = FALSE,
        max_quantity = 1000000, config = '{}', updated_at = CURRENT_TIMESTAMP WHERE item_id = 3001;
    INSERT INTO admin_audit_log (admin_user_id,action,target_type,target_id,payload,reason)
        VALUES (0,'store.catalog_reset','system','20260923_minimal_store',archived,
                '按要求合并商店面板，仅保留改名卡和最初的初段；原目录及受影响授权已归档');
END $store$;
