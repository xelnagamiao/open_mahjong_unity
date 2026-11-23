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
    private ShopOffer bound;
    private AppLanguage language;
    private int displayedOwned;

    public bool Matches(ShopOffer offer) {
        if (bound == null || bound.offer_id != offer.offer_id || language != ConfigManager.CurrentLanguage) return false;
        var old = bound.item;
        var item = offer.item;
        return bound.unit_price_minor == offer.unit_price_minor && displayedOwned == offer.owned_quantity &&
            old.item_id == item.item_id && old.name == item.name && old.icon_path == item.icon_path &&
            old.effect == item.effect && old.slot == item.slot && old.frame_color == item.frame_color;
    }

    public void Bind(ShopOffer offer, Action<string> selected) {
        if (bound == null) button.onClick.AddListener(() => select?.Invoke(offerId));
        bound = offer;
        language = ConfigManager.CurrentLanguage;
        offerId = offer.offer_id; select = selected;
        var item = offer.item;
        ShopPanel.SetLabelText(label, PlatformText.Get(item.name));
        price.text = ShopPanel.Money(offer.unit_price_minor);
        RefreshOwned(offer.owned_quantity);
        ShopPanel.SetLabelText(kind, PlatformText.Get(ShopPanel.Kind(item)));
        ConfigManager.SetInventoryIcon(icon, item);
        icon.enabled = icon.sprite != null;
        AvatarFrameGraphic.Apply(icon, item.slot == "avatar_frame" ? item.item_id : -1);
        artwork.gameObject.SetActive(!icon.enabled);
        artwork.SetKind(string.IsNullOrEmpty(item.slot) ? 2 : 3);
        ShopPanel.SetLabelText(glyph, item.effect == "rename_credit" ? PlatformText.Get("名") : PlatformText.Get("礼"));
        glyph.gameObject.SetActive(!icon.enabled);
    }

    public void SetSelected(bool selected, bool enabled) {
        selection.SetActive(selected);
        surface.color = selected ? new Color(.41f,.35f,.29f) : new Color(.33f,.33f,.33f);
        button.interactable = enabled;
    }
    public void RefreshOwned(int quantity) {
        displayedOwned = quantity;
        ShopPanel.SetLabelText(owned, PlatformText.Format("已拥有 {0} 件", quantity));
    }
}
