using TMPro;
using UnityEngine;

/// <summary>A rule-provided text corner badge preserves the physical tile face.</summary>
public sealed class RuleTileBadge : MonoBehaviour {
    public const float FontSize = 25f;
    public static readonly Color Ink = new Color32(177, 25, 22, 255);
    private TMP_Text label;
    public static void Apply(TileCard card,int tile) => Apply(card.transform,tile);
    public static void Apply(Transform card,int tile) {
        string caption=RuleRegistry.Current?.TileBadgeText?.Invoke(tile);
        var badge=card.GetComponent<RuleTileBadge>();
        if (string.IsNullOrEmpty(caption)) { if(badge?.label!=null) badge.label.gameObject.SetActive(false); return; }
        if (badge==null) badge=card.gameObject.AddComponent<RuleTileBadge>();
        Transform parent=TileFaceOverlay.Get(card);
        if (badge.label==null) {
            var go=new GameObject("RuleTileBadge",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);go.layer=card.gameObject.layer;
            badge.label=go.GetComponent<TextMeshProUGUI>();
            badge.label.font=PlatformTypography.ChineseRegular ?? TMP_Settings.defaultFontAsset;
            badge.label.fontStyle=FontStyles.Normal;
            badge.label.fontSize=FontSize;badge.label.alignment=TextAlignmentOptions.TopRight;badge.label.color=Ink;badge.label.raycastTarget=false;
            var rect=badge.label.rectTransform;rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one;rect.anchoredPosition=-TileFaceOverlay.CornerInset;rect.sizeDelta=new Vector2(32,32);
        }
        if (badge.label.transform.parent!=parent) badge.label.transform.SetParent(parent,false);
        TileFaceOverlay.SetRuleCorner(badge.label.rectTransform,TileCornerLabel.ShowsOn(card,tile));
        badge.label.text=caption;badge.label.gameObject.SetActive(true);badge.label.transform.SetAsLastSibling();
    }
}
