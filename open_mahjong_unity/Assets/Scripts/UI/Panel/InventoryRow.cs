using TMPro;
using UnityEngine;

public sealed class InventoryRow : MonoBehaviour {
    [SerializeField] private UnityEngine.UI.Button button;
    [SerializeField] private UnityEngine.UI.Image icon, selectionFrame;
    [SerializeField] private WarehouseIconGraphic artwork;
    [SerializeField] private TMP_Text glyph, label, badge, kind;
    [SerializeField] private Color surface = new Color(.16f,.19f,.29f);
    [SerializeField] private Color selectedSurface = new Color(.23f,.29f,.44f);
    [SerializeField] private Color badgeInk = new Color(.82f,.87f,.97f);
    [SerializeField] private Color equippedBadgeInk = new Color(.65f,.87f,.76f);
    [SerializeField] private UnityEngine.UI.Image badgeBackground;
    [SerializeField] private bool compactCard;
    public string Key { get; private set; }
    public int ItemId { get; private set; }

    private System.Action<string> onSelect;
    private bool isEquipped;

    public void ApplyAppearance(Salasasa.Home.InventoryPalette.Palette palette) {
        surface = palette[Salasasa.Home.InventoryPalette.Role.Card];
        selectedSurface = palette[Salasasa.Home.InventoryPalette.Role.SelectedCard];
        badgeInk = palette[Salasasa.Home.InventoryPalette.Role.BadgeInk];
        equippedBadgeInk = palette[Salasasa.Home.InventoryPalette.Role.EquippedInk];
        label.color = palette[Salasasa.Home.InventoryPalette.Role.Ink];
        kind.color = palette[Salasasa.Home.InventoryPalette.Role.Muted];
        badge.color = isEquipped ? equippedBadgeInk : badgeInk;
        if (badgeBackground) badgeBackground.color = palette[Salasasa.Home.InventoryPalette.Role.Badge];
        PaintBorder(selectionFrame.transform, palette[Salasasa.Home.InventoryPalette.Role.Accent]);
        var boundary = transform.Find("CardBoundary");
        if (boundary) PaintBorder(boundary, palette[Salasasa.Home.InventoryPalette.Role.Stroke]);
        button.targetGraphic.color = selectionFrame.gameObject.activeSelf ? selectedSurface : surface;
    }

    private static void PaintBorder(Transform root, Color color) {
        foreach (var image in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            image.color = image.transform == root && root.childCount > 0 ? Color.clear : color;
    }

    public void Bind(WarehouseEntry entry, System.Action<string> select) {
        Key = entry.Key; ItemId = entry.Id; onSelect = select;
        isEquipped = entry.IsEquipped;
        icon.sprite = ConfigManager.GetInventoryIcon(entry.Definition);
        icon.enabled = icon.sprite != null;
        AvatarFrameGraphic.Apply(icon, entry.Slot == "avatar_frame" ? entry.Id : -1);
        artwork.gameObject.SetActive(!icon.enabled); artwork.SetKind(entry.IconKind);
        glyph.text = entry.Glyph; glyph.gameObject.SetActive(!icon.enabled);
        label.richText = false; label.text = entry.Name;
        badge.text = compactCard && entry.Enabled && entry.IsEquipment && !entry.IsEquipped ? "" : entry.Badge;
        if (badgeBackground) badgeBackground.gameObject.SetActive(!string.IsNullOrEmpty(badge.text));
        badge.color = entry.IsEquipped ? equippedBadgeInk : badgeInk;
        kind.text = entry.IsEquipment ? "可装配" : entry.IsCollectible ? "收藏品" : "消耗品";
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(Clicked);
    }

    private void Clicked() => onSelect?.Invoke(Key);

    public void Select(bool selected, bool interactable) {
        button.interactable = interactable;
        button.targetGraphic.color = selected ? selectedSurface : surface;
        selectionFrame.gameObject.SetActive(selected);
    }
}
