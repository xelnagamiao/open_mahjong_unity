using System.Collections.Generic;
using UnityEngine;

/// <summary>在原结算牌行范围内等比缩放，底边对齐，为加杠完整预留高度。</summary>
[DisallowMultipleComponent]
public class SettlementTileRowLayout : UnityEngine.EventSystems.UIBehaviour, UnityEngine.UI.ILayoutGroup {
    private readonly List<RectTransform> rectChildren = new List<RectTransform>();
    private DrivenRectTransformTracker tracker;
    private RectTransform Rect => (RectTransform)transform;

    public static void Ensure(GameObject container) {
        var existing = container.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        if (existing != null) existing.enabled = false;
        if (container.GetComponent<SettlementTileRowLayout>() == null) container.AddComponent<SettlementTileRowLayout>();
    }

    protected override void OnEnable() { base.OnEnable(); MarkDirty(); }
    protected override void OnDisable() { tracker.Clear(); MarkDirty(); base.OnDisable(); }
    protected override void OnRectTransformDimensionsChange() { base.OnRectTransformDimensionsChange(); MarkDirty(); }
    private void OnTransformChildrenChanged() { MarkDirty(); }
    private void MarkDirty() { UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(Rect); }
    public void SetLayoutHorizontal() { Arrange(); }
    public void SetLayoutVertical() { Arrange(); }

    private static Vector2 NaturalSize(RectTransform child) {
        var meld = child.GetComponent<SettlementMeldGroupView>();
        return meld != null ? new Vector2(meld.preferredWidth, meld.preferredHeight) : child.rect.size;
    }

    private void Measure(out float width, out float height) {
        width = height = 0f;
        for (int i = 0; i < rectChildren.Count; i++) {
            Vector2 size = NaturalSize(rectChildren[i]);
            width += size.x;
            height = Mathf.Max(height, size.y);
        }
    }

    private void Arrange() {
        tracker.Clear();
        rectChildren.Clear();
        foreach (Transform child in transform) {
            if (child.gameObject.activeInHierarchy && child is RectTransform rect) rectChildren.Add(rect);
        }
        Measure(out float width, out float height);
        if (width <= 0f || height <= 0f) return;
        float availableWidth = Mathf.Max(0f, Rect.rect.width);
        float availableHeight = Mathf.Max(0f, Rect.rect.height);
        float scale = Mathf.Min(1f, Mathf.Min(availableWidth / width, availableHeight / height));
        float x = 0f;
        float bottom = (availableHeight - height * scale) * .5f;
        for (int i = 0; i < rectChildren.Count; i++) {
            RectTransform child = rectChildren[i];
            Vector2 size = NaturalSize(child);
            tracker.Add(this, child, DrivenTransformProperties.Scale | DrivenTransformProperties.Anchors
                | DrivenTransformProperties.Pivot | DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.SizeDelta);
            child.anchorMin = child.anchorMax = Vector2.zero;
            child.pivot = Vector2.zero;
            child.localScale = Vector3.one * scale;
            child.sizeDelta = size;
            child.anchoredPosition = new Vector2(x, bottom);
            x += size.x * scale;
        }
    }
}
