using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RecordInfoPopup : MonoBehaviour {
    [SerializeField] private RectTransform availableArea;
    [SerializeField] private RectTransform panel;
    [SerializeField] private TMP_Text text;
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private Vector2 padding = new Vector2(40f, 20f);
    private bool refreshing, status;
    private Vector2 lastAvailableSize;
    private TMP_Text statusText, heading;

    public void ResetDocument() {
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = 1f;
    }

    public void PresentGame(string value) {
        status = false;
        if (heading) heading.gameObject.SetActive(false);
        if (statusText) statusText.gameObject.SetActive(false);
        text.gameObject.SetActive(true);
        scroll.content = text.rectTransform;
        TableText.Set(text, value);
        RefreshLayout();
        ResetDocument();
    }

    public void PresentStatus(string value) {
        bool reset = !status || !gameObject.activeSelf;
        status = true;
        if (!heading) heading = Label(panel, "InfoHeading", "阅览状态", 28);
        if (!statusText) {
            statusText = Instantiate(text, scroll.viewport, false);
            statusText.name = "ReplayStatusContent";
            statusText.fontStyle = FontStyles.Normal;
            statusText.fontWeight = FontWeight.Regular;
            statusText.richText = false;
            statusText.fontSize = 25;
            statusText.enableAutoSizing = false;
            statusText.textWrappingMode = TextWrappingModes.Normal;
            statusText.overflowMode = TextOverflowModes.Overflow;
        }
        text.gameObject.SetActive(false);
        heading.gameObject.SetActive(true);
        statusText.gameObject.SetActive(true);
        scroll.content = statusText.rectTransform;
        TableText.Set(statusText, value);
        RefreshLayout();
        if (reset) ResetDocument();
    }

    private void OnEnable() { RefreshLayout(); ResetDocument(); }
    private void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) RefreshLayout(); }
    private void LateUpdate() { if (availableArea && availableArea.rect.size != lastAvailableSize) RefreshLayout(); }
    private void RefreshLayout() {
        if (refreshing || !availableArea || !panel || !text || !scroll) return;
        refreshing = true;
        try {
            lastAvailableSize = availableArea.rect.size;
            if (!status) {
                scroll.viewport.offsetMin = padding * .5f;
                scroll.viewport.offsetMax = -padding * .5f;
                float maxWidth = Mathf.Max(padding.x + 1f, availableArea.rect.width);
                float maxHeight = Mathf.Max(padding.y + 1f, availableArea.rect.height);
                Vector2 preferred = text.GetPreferredValues(float.PositiveInfinity, float.PositiveInfinity);
                float width = Mathf.Min(maxWidth, Mathf.Max(300f, preferred.x + padding.x));
                float textHeight = text.GetPreferredValues(width - padding.x, float.PositiveInfinity).y;
                panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Min(maxHeight, textHeight + padding.y));
                text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight);
            } else if (statusText && heading) {
                float width = Mathf.Min(620, Mathf.Max(240, availableArea.rect.width - 24));
                float limit = Mathf.Min(420, Mathf.Max(140, availableArea.rect.height * .82f));
                panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                scroll.viewport.offsetMin = new Vector2(20, 16);
                scroll.viewport.offsetMax = new Vector2(-24, -62);
                Place(heading.rectTransform, 20, 10, width - 40, 42);
                float height = statusText.GetPreferredValues(width - 44, float.PositiveInfinity).y + 4;
                statusText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Min(limit, height + 82));
            }
        } finally { refreshing = false; }
    }
    private TMP_Text Label(Transform parent, string name, string value, float size) {
        var label = DynamicTextRow.SingleLabel(parent, name);
        var go = label.gameObject;
        label.font = text.font; label.color = text.color; label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular;
        label.fontSize = size; label.richText = false; label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap; label.alignment = TextAlignmentOptions.MidlineLeft;
        TableText.Set(label, value ?? "");
        var font = go.AddComponent<PlatformFontBinding>();
        font.authoredFont = text.GetComponent<PlatformFontBinding>()?.authoredFont ?? text.font;
        font.authoredMaterial = font.authoredFont.material;
        font.authoredStyle = FontStyles.Normal; font.authoredWeight = FontWeight.Regular;
        font.styleCaptured = true; font.preserveAuthoredTypeface = true;
        font.Apply();
        return label;
    }
    private static void Place(RectTransform rect, float x, float y, float width, float height) {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }
}
