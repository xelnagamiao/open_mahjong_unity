using System.Collections.Generic;
using UnityEngine;

public partial class ConfigManager {
    private static readonly Dictionary<int, InventoryItem> inventoryCatalog = new Dictionary<int, InventoryItem>();
    private static readonly Dictionary<string, Sprite> inventorySprites = new Dictionary<string, Sprite>();

    public static void ApplyInventoryCatalog(InventoryItem[] catalog) {
        if (catalog == null) return;
        inventoryCatalog.Clear();
        foreach (var item in catalog) if (item != null) inventoryCatalog[item.item_id] = item;
        GameSettings.NotifyAppearanceChanged(null);
    }

    public static Sprite GetInventoryIcon(InventoryItem item) {
        if (item == null || string.IsNullOrEmpty(item.icon_path)) return null;
        // Catalog paths are server-validated bundled assets, never remote URLs.
        if (!inventorySprites.TryGetValue(item.icon_path, out var sprite)) {
            sprite = Resources.Load<Sprite>(item.icon_path);
            if (sprite == null) {
                var sprites = Resources.LoadAll<Sprite>(item.icon_path);
                if (sprites.Length > 0) sprite = sprites[0];
                else {
                    // Some existing portrait textures use Multiple mode without
                    // slice data. Create one cached sprite for the full texture.
                    var texture = Resources.Load<Texture2D>(item.icon_path);
                    if (texture != null) {
                        sprite = Sprite.Create(texture, new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));
                        sprite.name = texture.name;
                    }
                }
            }
            inventorySprites[item.icon_path] = sprite;
        }
        return sprite;
    }

    public static Sprite GetProfileSprite(int profileId) {
        if (inventoryCatalog.TryGetValue(profileId, out var item) && item.slot == "avatar")
            return GetInventoryIcon(item) ?? Resources.Load<Sprite>("image/Profiles/1");
        return Resources.Load<Sprite>("image/Profiles/" + profileId) ?? Resources.Load<Sprite>("image/Profiles/1");
    }

    public static Color GetAvatarFrameColor(int itemId) {
        if (itemId == 0) itemId = AvatarFrameGraphic.DefaultItemId;
        // Disabled owned cosmetics are removed from live equipment by the server;
        // old replay snapshots can still resolve their original appearance.
        if (inventoryCatalog.TryGetValue(itemId, out var item) && item.slot == "avatar_frame"
            && ColorUtility.TryParseHtmlString(item.frame_color, out var color)) return color;
        return AvatarFrameGraphic.BuiltinColor(itemId);
    }
}
