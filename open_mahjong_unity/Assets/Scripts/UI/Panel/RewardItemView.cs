using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RewardItemView : MonoBehaviour {
    public Image icon;
    public WarehouseIconGraphic fallbackIcon;
    public TMP_Text title, quantity;
    public void Bind(RewardItem item) {
        RewardUiText.Set(title,item.name); quantity.text = "× " + item.quantity;
        icon.sprite = ConfigManager.GetInventoryIcon(item);
        icon.enabled = icon.sprite != null;
        AvatarFrameGraphic.Apply(icon, item.slot == "avatar_frame" ? item.item_id : -1);
        fallbackIcon.gameObject.SetActive(!icon.enabled);
        fallbackIcon.SetKind(item.effect == "rename_credit" ? 2 : 0);
    }
}

internal static class RewardUiText {
    public static void Set(TMP_Text label,string source) {
        var localized=label.GetComponent<LocalizedPlatformLabel>();
        if(localized) localized.SetSource(source); else label.text=PlatformText.Get(source);
    }
}
