using UnityEngine;

public partial class GamePlayerPanel {
    private Transform exchangeSixBadgeParent;
    private Vector2 exchangeSixBadgeAnchorMin, exchangeSixBadgeAnchorMax, exchangeSixBadgePivot, exchangeSixBadgeSize, exchangeSixBadgePosition;
    private Vector3 exchangeSixBadgeScale;
    private Vector3 exchangeSixBadgeLabelScale;
    private int exchangeSixBadgeSibling;

    public void ShowExchangeSixDeclaration(string caption) {
        SetPlayerVariable(VariableDeclaredReady, caption);
        var label = FindPlayerVariable(VariableDeclaredReady);
        if (label == null) return;
        var badge = (RectTransform)label.transform.parent;
        if (!string.IsNullOrEmpty(caption)) {
            if (exchangeSixBadgeParent == null) {
                exchangeSixBadgeParent = badge.parent;
                exchangeSixBadgeSibling = badge.GetSiblingIndex();
                exchangeSixBadgeAnchorMin = badge.anchorMin; exchangeSixBadgeAnchorMax = badge.anchorMax;
                exchangeSixBadgePivot = badge.pivot; exchangeSixBadgeSize = badge.sizeDelta;
                exchangeSixBadgePosition = badge.anchoredPosition; exchangeSixBadgeScale = badge.localScale;
                exchangeSixBadgeLabelScale = label.transform.localScale;
            }
            // 长沙声明放进头像底部，不伸到头像旁边的副露区。退出时还原原布局。
            badge.SetParent(playerProfilePicture.transform, false);
            badge.anchorMin = new Vector2(0, 0); badge.anchorMax = new Vector2(1, 0);
            badge.pivot = new Vector2(.5f, 0);
            badge.sizeDelta = new Vector2(-8, 48);
            badge.anchoredPosition = new Vector2(0, 4);
            badge.localScale = Vector3.one;
            // 多项声明共用一行时按头像内宽缩放，文字也不能伸到副露上。
            float captionWidth = label.GetPreferredValues(caption).x * exchangeSixBadgeLabelScale.x;
            float availableWidth = Mathf.Max(1, playerProfilePicture.rectTransform.rect.width - 16);
            label.transform.localScale = exchangeSixBadgeLabelScale * Mathf.Min(1, availableWidth / Mathf.Max(1, captionWidth));
        } else if (exchangeSixBadgeParent != null) {
            badge.SetParent(exchangeSixBadgeParent, false);
            badge.SetSiblingIndex(exchangeSixBadgeSibling);
            badge.anchorMin = exchangeSixBadgeAnchorMin; badge.anchorMax = exchangeSixBadgeAnchorMax;
            badge.pivot = exchangeSixBadgePivot; badge.sizeDelta = exchangeSixBadgeSize;
            badge.anchoredPosition = exchangeSixBadgePosition; badge.localScale = exchangeSixBadgeScale;
            label.transform.localScale = exchangeSixBadgeLabelScale;
            exchangeSixBadgeParent = null;
        }
    }
}
