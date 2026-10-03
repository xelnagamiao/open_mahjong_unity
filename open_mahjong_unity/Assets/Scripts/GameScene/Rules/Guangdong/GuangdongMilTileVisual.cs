using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>实体花牌保留原图；当前广东花鬼模式只附加“鬼”角标。</summary>
internal static class GuangdongMilTileVisual {
    internal static void Refresh(Transform card, int tile) {
        bool show = GuangdongMilRules.IsMil(RuleRegistry.CurrentSubRule) && GuangdongMilRules.IsGhost(tile);
        Transform badge = card.Find("GuangdongGhostBadge");
        if (!show) { if (badge != null) badge.gameObject.SetActive(false); return; }
        if (badge == null) {
            var go = new GameObject("GuangdongGhostBadge", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.layer = card.gameObject.layer; go.transform.SetParent(card,false); badge=go.transform;
            var rect=(RectTransform)badge; rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(1,1); rect.anchoredPosition=new Vector2(-1,-1); rect.sizeDelta=new Vector2(22,25);
            var image=go.GetComponent<UnityEngine.UI.Image>(); image.color=new Color(.50f,.12f,.20f,.95f); image.raycastTarget=false;
            var textObject=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI)); textObject.layer=go.layer; textObject.transform.SetParent(badge,false);
            var label=textObject.GetComponent<TextMeshProUGUI>(); label.text="鬼"; label.fontSize=17; label.alignment=TextAlignmentOptions.Center; label.raycastTarget=false; label.color=Color.white;
            label.font=GameCanvas.Instance?.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t=>t.font!=null)?.font??TMP_Settings.defaultFontAsset;
            label.rectTransform.anchorMin=Vector2.zero; label.rectTransform.anchorMax=Vector2.one; label.rectTransform.offsetMin=label.rectTransform.offsetMax=Vector2.zero;
        }
        badge.gameObject.SetActive(true); badge.SetAsLastSibling();
    }
}
