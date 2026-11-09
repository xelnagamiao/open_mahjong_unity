using TMPro;
using UnityEngine;

/// <summary>实体花牌保留原图；当前广东花鬼模式只附加“鬼”角标。</summary>
internal static class GuangdongMilTileVisual {
    internal static void Refresh(Transform card, int tile) {
        bool show = GuangdongMilRules.IsMil(RuleRegistry.CurrentSubRule) && GuangdongMilRules.IsGhost(tile);
        Transform badge = card.Find("TileFaceOverlay/GuangdongGhostBadge")
            ?? card.Find("Button/GuangdongGhostBadge") ?? card.Find("GuangdongGhostBadge");
        if (!show) { if (badge != null) badge.gameObject.SetActive(false); return; }
        Transform parent = TileFaceOverlay.Get(card);
        if (badge == null) {
            var go = new GameObject("GuangdongGhostBadge", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.layer = card.gameObject.layer; go.transform.SetParent(parent,false); badge=go.transform;
            var rect=(RectTransform)badge; rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(1,1); rect.anchoredPosition=new Vector2(-1,-1); rect.sizeDelta=new Vector2(25,28);
            var image=go.GetComponent<UnityEngine.UI.Image>(); image.color=new Color(.50f,.12f,.20f,.95f); image.raycastTarget=false;
            var textObject=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI)); textObject.layer=go.layer; textObject.transform.SetParent(badge,false);
            var label=textObject.GetComponent<TextMeshProUGUI>(); label.text="鬼"; label.fontSize=19; label.alignment=TextAlignmentOptions.Center; label.raycastTarget=false; label.color=Color.white;
            label.font=PlatformTypography.ChineseRegular??TMP_Settings.defaultFontAsset;
            label.fontStyle=FontStyles.Normal;
            label.rectTransform.anchorMin=Vector2.zero; label.rectTransform.anchorMax=Vector2.one; label.rectTransform.offsetMin=label.rectTransform.offsetMax=Vector2.zero;
        }
        if (badge.parent != parent) badge.SetParent(parent,false);
        TileFaceOverlay.SetRuleCorner((RectTransform)badge,TileCornerLabel.ShowsOn(card,tile));
        badge.gameObject.SetActive(true); badge.SetAsLastSibling();
    }
}
