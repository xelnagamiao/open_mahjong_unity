using TMPro;
using UnityEngine;

public sealed class InventoryRow : MonoBehaviour {
    [SerializeField] private UnityEngine.UI.Button button;
    [SerializeField] private UnityEngine.UI.Image icon, selectionFrame;
    [SerializeField] private WarehouseIconGraphic artwork;
    [SerializeField] private TMP_Text glyph, label, badge, kind;
    public string Key { get; private set; }
    public int ItemId { get; private set; }

    private System.Action<string> onSelect;

    public void Bind(WarehouseEntry entry, System.Action<string> select) {
        Key = entry.Key; ItemId = entry.Id; onSelect = select;
        icon.sprite = ConfigManager.GetInventoryIcon(entry.Definition);
        icon.enabled = icon.sprite != null;
        AvatarFrameGraphic.Apply(icon, entry.Slot == "avatar_frame" ? entry.Id : -1);
        artwork.gameObject.SetActive(!icon.enabled); artwork.SetKind(entry.IconKind);
        glyph.text = entry.Glyph; glyph.gameObject.SetActive(!icon.enabled);
        label.richText = false; label.text = entry.Name;
        badge.text = entry.Badge;
        badge.color = entry.IsEquipped ? new Color(.65f,.87f,.76f) : new Color(.82f,.87f,.97f);
        kind.text = entry.IsDefault ? "默认物品" : entry.IsEquipment ? "可装配" : "消耗品";
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(Clicked);
    }

    private void Clicked() => onSelect?.Invoke(Key);

    public void Select(bool selected, bool interactable) {
        button.interactable = interactable;
        button.targetGraphic.color = selected ? new Color(.23f,.29f,.44f) : new Color(.16f,.19f,.29f);
        selectionFrame.gameObject.SetActive(selected);
    }
}
