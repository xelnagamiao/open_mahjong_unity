using TMPro;
using UnityEngine;

/// <summary>信息面板随内容调整大小，并将长文本限制在屏幕内滚动。</summary>
public sealed class RecordInfoPopup : MonoBehaviour {
    [SerializeField] private RectTransform availableArea;
    [SerializeField] private RectTransform panel;
    [SerializeField] private TMP_Text text;
    [SerializeField] private UnityEngine.UI.ScrollRect scroll;
    [SerializeField] private Vector2 padding = new Vector2(40f, 20f);
    private bool refreshing;
    private Vector2 lastAvailableSize;

    private void OnEnable() {
        RefreshLayout();
        scroll.StopMovement();
        text.rectTransform.anchoredPosition = Vector2.zero;
        scroll.verticalNormalizedPosition = 1f;
    }

    private void OnRectTransformDimensionsChange() {
        if (isActiveAndEnabled) RefreshLayout();
    }

    private void LateUpdate() {
        // 固定大小的子面板不一定收到父容器的尺寸回调；只在可用区域变化时重新布局。
        if (availableArea != null && availableArea.rect.size != lastAvailableSize) RefreshLayout();
    }

    private void RefreshLayout() {
        if (refreshing || availableArea == null || panel == null || text == null) return;
        refreshing = true;
        try {
            lastAvailableSize = availableArea.rect.size;
            float maxWidth = Mathf.Max(padding.x + 1f, availableArea.rect.width);
            float maxHeight = Mathf.Max(padding.y + 1f, availableArea.rect.height);
            Vector2 preferred = text.GetPreferredValues(float.PositiveInfinity, float.PositiveInfinity);
            float width = Mathf.Min(maxWidth, Mathf.Max(300f, preferred.x + padding.x));
            float textHeight = text.GetPreferredValues(width - padding.x, float.PositiveInfinity).y;
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Min(maxHeight, textHeight + padding.y));
            text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight);
        } finally {
            refreshing = false;
        }
    }
}
