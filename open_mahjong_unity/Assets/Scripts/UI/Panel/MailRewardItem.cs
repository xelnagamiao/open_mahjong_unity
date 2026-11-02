using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MailRewardItem : MonoBehaviour
{
    public Image surface, icon;
    public TMP_Text title, quantity;
    public void Bind(MailReward reward)
    {
        title.text = reward.name; quantity.text = "×" + reward.quantity;
        icon.sprite = ConfigManager.GetInventoryIcon(new InventoryItem { item_id = reward.item_id, icon_path = reward.icon_path });
        icon.enabled = icon.sprite;
    }
    public void Apply(MailPalette p) { surface.color = p.selected; title.color = p.ink; quantity.color = p.accent; }
}
