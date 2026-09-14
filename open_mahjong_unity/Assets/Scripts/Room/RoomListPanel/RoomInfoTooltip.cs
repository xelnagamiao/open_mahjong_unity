using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>可进入、滚动，移开自动收起的详情浮层。挂在根 Canvas 下，避开列表 Mask。</summary>
public sealed class RoomInfoTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerClickHandler, ISubmitHandler {
    private static RoomInfoTooltip active;
    private string heading, body;
    private TMP_FontAsset font;
    private RectTransform popup, canvasRect;
    private Canvas rootCanvas;
    private TextMeshProUGUI contentLabel;
    private ScrollRect scroll;
    private bool hovering;
    private float showAt, leaveAt;
    private const float Margin = 12f;
    private readonly Vector3[] anchorCorners = new Vector3[4];

    public void SetContent(string title, string text, TMP_FontAsset textFont) {
        if (heading == title && body == text && font == textFont) return;
        heading = title; body = text; font = textFont;
        if (popup != null) {
            contentLabel.text = body;
            ResizeContent();
        }
    }
    public void OnPointerEnter(PointerEventData eventData) { hovering = true; showAt = Time.unscaledTime + .2f; }
    public void OnPointerExit(PointerEventData eventData) { hovering = false; }
    public void OnSubmit(BaseEventData eventData) { Show(); }
    public void OnPointerClick(PointerEventData eventData) {
        if (eventData.button == PointerEventData.InputButton.Left) Show();
    }
    private void Update() {
        if (popup == null) {
            if (hovering && Time.unscaledTime >= showAt) Show();
            return;
        }
        Camera camera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        Vector2 pointer = Input.mousePosition;
        bool overPopup = RectTransformUtility.RectangleContainsScreenPoint(popup, pointer, camera);
        bool overAnchor = RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, pointer, camera);
        if (overPopup || overAnchor) leaveAt = Time.unscaledTime + .25f;
        else if (Time.unscaledTime > leaveAt) Hide();
    }
    private void LateUpdate() { if (popup != null) PositionPopup(); }
    private void OnDisable() { hovering = false; Hide(); }
    private void OnDestroy() { Hide(); }

    public void Show() {
        if (popup != null || string.IsNullOrEmpty(body)) return;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        if (active != null && active != this) active.Hide();
        active = this;
        rootCanvas = canvas.rootCanvas;
        canvasRect = (RectTransform)rootCanvas.transform;
        popup = Rect("RoomDetailsPopup", canvasRect);
        popup.anchorMin = popup.anchorMax = new Vector2(.5f, .5f);
        popup.pivot = new Vector2(0, 1);
        var layer = popup.gameObject.AddComponent<Canvas>();
        layer.overrideSorting = true;
        layer.sortingOrder = rootCanvas.sortingOrder + 100;
        popup.gameObject.AddComponent<GraphicRaycaster>();
        var background = popup.gameObject.AddComponent<Image>();
        background.color = new Color(.12f, .12f, .12f, 1f);
        var outline = popup.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(.45f, .45f, .45f, 1f);
        outline.effectDistance = new Vector2(1, -1);

        var title = Label("Heading", popup, 23);
        title.text = heading;
        title.rectTransform.anchorMin = new Vector2(0, 1);
        title.rectTransform.anchorMax = Vector2.one;
        title.rectTransform.pivot = new Vector2(.5f, 1);
        title.rectTransform.sizeDelta = new Vector2(-32, 36);
        title.rectTransform.anchoredPosition = new Vector2(0, -14);

        var viewport = Rect("Viewport", popup);
        Stretch(viewport, 16, 58, 16, 16);
        viewport.gameObject.AddComponent<RectMask2D>();
        var hitArea = viewport.gameObject.AddComponent<Image>();
        hitArea.color = new Color(0, 0, 0, 0);
        contentLabel = Label("Configuration", viewport, 20);
        contentLabel.text = body;
        contentLabel.rectTransform.anchorMin = new Vector2(0, 1);
        contentLabel.rectTransform.anchorMax = new Vector2(1, 1);
        contentLabel.rectTransform.pivot = new Vector2(.5f, 1);
        contentLabel.rectTransform.anchoredPosition = Vector2.zero;
        scroll = popup.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = contentLabel.rectTransform;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30;
        ResizeContent();
        scroll.verticalNormalizedPosition = 1;
        leaveAt = Time.unscaledTime + .25f;
        PositionPopup();
    }
    private void ResizeContent() {
        float width = Mathf.Min(520, canvasRect.rect.width - Margin * 2);
        float height = contentLabel.GetPreferredValues(body, width - 32, Mathf.Infinity).y + 8;
        popup.sizeDelta = new Vector2(width, Mathf.Min(height + 74, canvasRect.rect.height - Margin * 2, 680));
        contentLabel.rectTransform.sizeDelta = new Vector2(0, height);
    }
    private void PositionPopup() {
        ((RectTransform)transform).GetWorldCorners(anchorCorners);
        Vector2 leftBottom = canvasRect.InverseTransformPoint(anchorCorners[0]);
        Vector2 rightTop = canvasRect.InverseTransformPoint(anchorCorners[2]);
        Rect bounds = canvasRect.rect;
        float width = popup.rect.width, height = popup.rect.height;
        float x = rightTop.x + 8;
        if (x + width > bounds.xMax - Margin) x = leftBottom.x - width - 8;
        x = Mathf.Clamp(x, bounds.xMin + Margin, bounds.xMax - width - Margin);
        float y = Mathf.Clamp(rightTop.y, bounds.yMin + height + Margin, bounds.yMax - Margin);
        popup.localPosition = new Vector3(x, y, 0);
    }
    public void Hide() {
        if (popup != null) {
            popup.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(popup.gameObject);
            else DestroyImmediate(popup.gameObject);
        }
        popup = null;
        if (active == this) active = null;
    }
    private TextMeshProUGUI Label(string name, RectTransform parent, float size) {
        var label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.fontSize = size;
        label.richText = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.raycastTarget = false;
        label.color = new Color(.96f, .96f, .96f);
        return label;
    }
    private static RectTransform Rect(string name, Transform parent) {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.gameObject.layer = parent.gameObject.layer;
        rect.SetParent(parent, false);
        return rect;
    }
    private static void Stretch(RectTransform rect, float left, float top, float right, float bottom) {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top);
    }
}
