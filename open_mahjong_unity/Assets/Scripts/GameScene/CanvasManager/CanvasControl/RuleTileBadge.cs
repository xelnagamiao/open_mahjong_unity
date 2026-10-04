using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>A rule-provided text corner badge preserves the physical tile face.</summary>
public sealed class RuleTileBadge : MonoBehaviour {
    private TMP_Text label;
    public static void Apply(TileCard card,int tile) => Apply(card.transform,tile);
    public static void Apply(Transform card,int tile) {
        string caption=RuleRegistry.Current?.TileBadgeText?.Invoke(tile);
        var badge=card.GetComponent<RuleTileBadge>();
        if (string.IsNullOrEmpty(caption)) { if(badge?.label!=null) badge.label.gameObject.SetActive(false); return; }
        if (badge==null) badge=card.gameObject.AddComponent<RuleTileBadge>();
        if (badge.label==null) {
            var go=new GameObject("RuleTileBadge",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(card,false);go.layer=card.gameObject.layer;
            badge.label=go.GetComponent<TextMeshProUGUI>();
            badge.label.font=GameCanvas.Instance?.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t=>t.font!=null)?.font ?? TMP_Settings.defaultFontAsset;
            badge.label.fontSize=22;badge.label.alignment=TextAlignmentOptions.TopRight;badge.label.color=new Color(.8f,.19f,.08f);badge.label.raycastTarget=false;
            var rect=badge.label.rectTransform;rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one;rect.anchoredPosition=new Vector2(-4,-3);rect.sizeDelta=new Vector2(32,32);
        }
        badge.label.text=caption;badge.label.gameObject.SetActive(true);badge.label.transform.SetAsLastSibling();
    }
}
