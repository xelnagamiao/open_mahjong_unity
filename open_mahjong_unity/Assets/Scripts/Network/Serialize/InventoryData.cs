using System;

[Serializable] public class InventoryReward { public int item_id; public int quantity; }
[Serializable] public class InventoryConfig { public InventoryReward[] rewards; }
[Serializable] public class InventoryItem {
    public int item_id;
    public string code, name, description, category, asset_key, slot, icon_path, frame_color, effect;
    public int max_quantity, sort_order, character_id, voice_id;
    public bool is_default, grant_enabled, use_enabled;
    public InventoryConfig config;
}
[Serializable] public class InventoryBalance { public int item_id, quantity; }
[Serializable] public class InventoryEquipment { public string slot; public int item_id; }
[Serializable] public class InventoryAppearance {
    public int user_id, character_id, voice_id, profile_image_id, avatar_frame_id;
}
[Serializable] public class InventoryState {
    public int user_id, rename_count;
    public bool is_tourist;
    public InventoryItem[] catalog;
    public InventoryBalance[] owned;
    public InventoryEquipment[] equipment;
    public InventoryAppearance appearance;
}
[Serializable] public class InventoryCommand {
    public string type, request_id, slot;
    public int item_id, quantity = 1;
}
