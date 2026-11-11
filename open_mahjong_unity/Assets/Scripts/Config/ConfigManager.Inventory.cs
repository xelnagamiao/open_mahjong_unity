using System.Collections.Generic;
using UnityEngine;

public partial class ConfigManager {
    public const int DefaultAvatarItemId = 2301;
    public const int ReimuAvatarItemId = 2302;
    public const int CirnoAvatarItemId = 2303;
    public static bool IsDevelopmentOnlyAvatar(int itemId) => itemId == ReimuAvatarItemId || itemId == CirnoAvatarItemId;
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
        return LoadInventorySprite(item.icon_path);
    }

    private static Sprite LoadInventorySprite(string path) {
        // Catalog paths are server-validated bundled assets, never remote URLs.
        if (!inventorySprites.TryGetValue(path, out var sprite)) {
            sprite = IosResourceCache.Load<Sprite>(path);
            if (sprite == null) {
                var sprites = IosResourceCache.LoadAll<Sprite>(path);
                if (sprites.Length > 0) sprite = sprites[0];
                else {
                    // Some existing portrait textures use Multiple mode without
                    // slice data. Create one cached sprite for the full texture.
                    var texture = IosResourceCache.Load<Texture2D>(path);
                    if (texture != null) {
                        sprite = Sprite.Create(texture, new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));
                        sprite.name = texture.name;
                    }
                }
            }
            inventorySprites[path] = sprite;
        }
        return sprite;
    }

    public static Sprite GetProfileSprite(int profileId) {
        if (inventoryCatalog.TryGetValue(profileId, out var item) && item.slot == "avatar")
            return GetInventoryIcon(item) ?? IosResourceCache.Load<Sprite>("image/Profiles/1");
        if (profileId == DefaultAvatarItemId) profileId = 1;
        return LoadInventorySprite("image/Profiles/" + profileId) ?? IosResourceCache.Load<Sprite>("image/Profiles/1");
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
