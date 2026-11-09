using UnityEngine;
using UnityEngine.UI;

/// <summary>整个牌面预览使用一个滚动区域，固定牌组的内层容器只负责排列。</summary>
public sealed class CardFacePreviewReadingArea : MonoBehaviour
{
    public ScrollRect scroll;
    public RectTransform surfaces, standardHand, standardTable;
    public CardFaceFamilyPreview family;
    const float Width = 884;
    const float Gap = 14;

    public void ShowFamily(int selected)
    {
        CardFaceLayoutPresentation.Place(surfaces, 0, 0, Width, 220);
        float y = 220 + Gap;
        if (selected == 0) {
            y = PlaceStandard(standardHand, y);
            y = PlaceStandard(standardTable, y);
        } else {
            var handContent = selected == 1 ? family.hongqueContent : family.expandedContent;
            var tableContent = selected == 1 ? family.hongqueTableContent : family.expandedTableContent;
            var root = (RectTransform)family.previewRoot.transform;
            float height = PlaceGallery(family.scroll, handContent, 0);
            height = PlaceGallery(family.tableScroll, tableContent, height);
            CardFaceLayoutPresentation.Place(root, 0, y, Width, height);
            y += height;
        }
        scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, y);
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = 1;
    }

    static float PlaceStandard(RectTransform group, float y)
    {
        float height = group.rect.height;
        CardFaceLayoutPresentation.Place(group, 0, y, Width, height);
        return y + height + Gap;
    }

    static float PlaceGallery(ScrollRect inner, RectTransform content, float y)
    {
        inner.enabled = false;
        if (inner.verticalScrollbar != null) inner.verticalScrollbar.gameObject.SetActive(false);
        float height = content.rect.height;
        var group = (RectTransform)inner.transform.parent;
        CardFaceLayoutPresentation.Place(group, 0, y, Width, height + 58);
        CardFaceLayoutPresentation.Place((RectTransform)inner.transform, 4, 46, 876, height);
        CardFaceLayoutPresentation.Place(inner.viewport, 0, 0, 876, height);
        CardFaceLayoutPresentation.Place(content, 0, 0, 876, height);
        var mask = inner.viewport.GetComponent<RectMask2D>();
        if (mask != null) mask.enabled = false;
        var stencil = inner.viewport.GetComponent<Mask>();
        if (stencil != null) stencil.enabled = false;
        return y + height + 58 + Gap;
    }
}
