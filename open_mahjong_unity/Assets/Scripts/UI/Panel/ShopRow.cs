using System;
using TMPro;
using UnityEngine;

public sealed class ShopRow : MonoBehaviour {
    public UnityEngine.UI.Button button;
    public UnityEngine.UI.Image icon, surface;
    public WarehouseIconGraphic artwork;
    public TMP_Text label, price, owned, kind, glyph;
    public GameObject selection;
    private string offerId;
    private Action<string> select;

    public void Bind(ShopOffer offer, Action<string> selected) {
        offerId = offer.offer_id; select = selected;
        var item = offer.item;
        label.text = PlatformText.Get(item.name);
        price.text = ShopPanel.Money(offer.unit_price_minor);
        owned.text = PlatformText.Format("已拥有 {0} 件", offer.owned_quantity);
        kind.text = PlatformText.Get(ShopPanel.Kind(item));
        icon.sprite = ConfigManager.GetInventoryIcon(item);
        icon.enabled = icon.sprite != null;
        if (item.slot == "avatar_frame") AvatarFrameGraphic.Apply(icon, item.item_id);
        artwork.gameObject.SetActive(!icon.enabled);
        artwork.SetKind(string.IsNullOrEmpty(item.slot) ? 2 : 3);
        glyph.text = item.effect == "rename_credit" ? PlatformText.Get("名") : PlatformText.Get("礼");
        glyph.gameObject.SetActive(!icon.enabled);
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => select?.Invoke(offerId));
    }

    public void SetSelected(bool selected, bool enabled) {
        selection.SetActive(selected);
        surface.color = selected ? new Color(.41f,.35f,.29f) : new Color(.33f,.33f,.33f);
        button.interactable = enabled;
    }
    public void RefreshOwned(int quantity) => owned.text = PlatformText.Format("已拥有 {0} 件", quantity);
}
