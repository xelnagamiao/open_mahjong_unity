using System;
using System.Collections.Generic;

public enum WarehouseCategory { Backpack, Title, Character, Cosmetic }

/// <summary>One player-facing collection, projected from the independently administered catalogs.</summary>
public sealed class WarehouseEntry {
    public string Key { get; private set; }
    public int Id { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public string Slot { get; private set; }
    public string KindLabel { get; private set; }
    public int Quantity { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsTitle { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsEquipped { get; private set; }
    public bool Enabled { get; private set; }
    public bool GuestRestricted { get; private set; }
    public InventoryItem Definition { get; private set; }
    public bool IsEquipment => !string.IsNullOrEmpty(Slot);
    public WarehouseCategory Category => IsTitle ? WarehouseCategory.Title
        : Definition?.category == "character" || Slot == "character" ? WarehouseCategory.Character
        : Definition?.category == "cosmetic" || IsEquipment ? WarehouseCategory.Cosmetic : WarehouseCategory.Backpack;
    public bool CanAct => Enabled && !GuestRestricted && (!IsDefault || !IsEquipped);
    public string ActionLabel => !Enabled ? "暂不可用" : GuestRestricted ? "游客不可使用"
        : IsDefault ? IsEquipped ? "当前使用" : "使用默认" + KindLabel
        : IsEquipment ? IsEquipped ? "卸下" : IsTitle ? "佩戴" : "装配" : "使用";
    public string Badge => !Enabled ? "已停用" : IsEquipped ? "使用中" : IsDefault ? "默认" : IsEquipment ? "已拥有" : Quantity + " 件";
    public string Glyph => IsDefault ? "无" : IsTitle ? "衔" : IsEquipment ? "饰" : "名";
    public int IconKind => IsDefault ? 0 : IsTitle ? 1 : IsEquipment ? 3 : 2;

    public InventoryCommand CreateCommand(int quantity = 1) {
        if (!CanAct) return null;
        if (IsTitle) return new InventoryCommand { type = "title/equip", item_id = IsEquipped ? 1 : Id };
        if (IsEquipment) return new InventoryCommand { type = "inventory/equip", slot = Slot, item_id = IsEquipped ? 0 : Id };
        if (quantity < 1 || quantity > Math.Min(100, Quantity)) return null;
        return new InventoryCommand { type = "inventory/use", item_id = Id, quantity = quantity };
    }

    public static List<WarehouseEntry> Build(TitleState titles, InventoryState inventory, int equippedTitleId) {
        var result = new List<WarehouseEntry>();
        var ownedTitles = new HashSet<int>();
        foreach (var grant in titles?.owned ?? Array.Empty<TitleGrant>()) if (grant != null) ownedTitles.Add(grant.title_id);
        var validEquippedTitle = equippedTitleId > 1 && ownedTitles.Contains(equippedTitleId)
            && Array.Exists(titles?.catalog ?? Array.Empty<TitleDefinition>(), t => t != null && t.title_id == equippedTitleId && t.is_enabled);
        result.Add(new WarehouseEntry {
            Key = "title:1", Id = 1, Name = ConfigManager.DefaultTitleName, KindLabel = "头衔",
            Description = "",
            Slot = "title", Quantity = 1, IsTitle = true, IsDefault = true, IsEquipped = !validEquippedTitle, Enabled = true
        });
        var keys = new HashSet<string> { "title:1" };
        foreach (var definition in titles?.catalog ?? Array.Empty<TitleDefinition>()) {
            if (definition == null || definition.title_id <= 1 || !ownedTitles.Contains(definition.title_id)) continue;
            string key = "title:" + definition.title_id;
            if (!keys.Add(key)) continue;
            result.Add(new WarehouseEntry {
                Key = key, Id = definition.title_id, Name = definition.name, Description = definition.description ?? "",
                KindLabel = "头衔", Slot = "title", Quantity = 1, SortOrder = definition.sort_order,
                IsTitle = true, IsEquipped = validEquippedTitle && equippedTitleId == definition.title_id, Enabled = definition.is_enabled
            });
        }
        var quantities = new Dictionary<int, int>();
        foreach (var balance in inventory?.owned ?? Array.Empty<InventoryBalance>()) if (balance != null) quantities[balance.item_id] = balance.quantity;
        foreach (var item in inventory?.catalog ?? Array.Empty<InventoryItem>()) {
            if (item == null) continue;
            quantities.TryGetValue(item.item_id, out int quantity);
            if (quantity <= 0 && !item.is_default) continue;
            string key = "item:" + item.item_id;
            if (!keys.Add(key)) continue;
            bool equipment = !string.IsNullOrEmpty(item.slot);
            result.Add(new WarehouseEntry {
                Key = key, Id = item.item_id, Name = item.name,
                Description = item.effect == "rename_credit" ? "使用后增加 1 次改名机会。" : item.description ?? "", Definition = item,
                KindLabel = equipment ? SlotLabel(item.slot) : "道具", Slot = item.slot,
                Quantity = item.is_default ? Math.Max(1, quantity) : quantity, SortOrder = item.sort_order,
                // Built-in frames are both permanently owned; only A1 is the selected fallback.
                IsDefault = item.is_default && (item.slot != "avatar_frame" || item.item_id == AvatarFrameGraphic.DefaultItemId), Enabled = item.use_enabled,
                IsEquipped = equipment && Array.Exists(inventory?.equipment ?? Array.Empty<InventoryEquipment>(), e => e != null && e.slot == item.slot && e.item_id == item.item_id),
                GuestRestricted = !equipment && item.effect == "rename_credit" && inventory.is_tourist
            });
        }
        result.Sort((a, b) => {
            if (a.Key == "title:1") return b.Key == a.Key ? 0 : -1;
            if (b.Key == "title:1") return 1;
            int order = a.SortOrder.CompareTo(b.SortOrder);
            if (order != 0) return order;
            order = b.IsEquipment.CompareTo(a.IsEquipment);
            return order != 0 ? order : string.CompareOrdinal(a.Key, b.Key);
        });
        return result;
    }

    private static string SlotLabel(string slot) => slot == "character" ? "角色" : slot == "avatar" ? "头像" : slot == "avatar_frame" ? "头像框" : "装扮";
}
