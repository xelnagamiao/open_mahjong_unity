using TMPro;
using UnityEngine;

/// <summary>Shared, non-interactive status label with an optional tile and a fading background.</summary>
public sealed class SelfStatusBanner : MonoBehaviour {
    private const float Padding = 16f;
    private const float FadeWidth = 84f;
    private TMP_Text label;
    private RectTransform rect;
    private UnityEngine.UI.LayoutElement layout;
    private SelfStatusFade fade;
    private UnityEngine.UI.Image background;
    private GameObject tilePrefab;
    private StaticCard tile;
    private string defaultText;
    private string currentText;
    private int currentTile;
    private bool initialized;

    public int TileId => currentTile;
    public float ContentWidth { get; private set; }

    public void Initialize(TMP_Text text, GameObject cardPrefab, Color color) {
        if (initialized) return;
        label = text != null ? text : GetComponentInChildren<TMP_Text>(true);
        rect = (RectTransform)transform;
        tilePrefab = cardPrefab;
        defaultText = label.text;
        background = GetComponent<UnityEngine.UI.Image>();
        background.color = color;
        background.raycastTarget = false;
        background.sprite = null;
        background.type = UnityEngine.UI.Image.Type.Simple;
        fade = GetComponent<SelfStatusFade>();
        if (fade == null) fade = gameObject.AddComponent<SelfStatusFade>();
        layout = GetComponent<UnityEngine.UI.LayoutElement>();
        if (layout == null) layout = gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        // TMP can create fallback submeshes after text changes; block input for the whole subtree.
        var inputGroup = GetComponent<CanvasGroup>();
        if (inputGroup == null) inputGroup = gameObject.AddComponent<CanvasGroup>();
        inputGroup.blocksRaycasts = false;
        inputGroup.interactable = false;
        label.raycastTarget = false;
        label.color = Color.white;
        label.fontSize = 28f;
        label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.margin = Vector4.zero;
        var fitter = label.GetComponent<UnityEngine.UI.ContentSizeFitter>();
        if (fitter != null) fitter.enabled = false;
        initialized = true;
        SetContent(defaultText, 0);
    }

    public void SetColor(Color color) {
        background.color = color;
    }

    public void SetContent(string text, int tileId = 0) {
        text = text ?? defaultText;
        tileId = tileId > 0 ? tileId : 0;
        if (currentText == text && currentTile == tileId) return;
        currentText = text;
        currentTile = tileId;
        label.text = text;
        Vector2 textSize = label.GetPreferredValues(text, Mathf.Infinity, Mathf.Infinity);
        float textWidth = Mathf.Ceil(textSize.x);
        float tileWidth = tileId > 0 ? 32f : 0f;
        float tileHeight = tileId > 0 ? TileFaceFit.HandSizeFor(tileId, tileWidth).y : 0f;
        float height = Mathf.Max(44f, textSize.y + 4f, tileHeight + 8f);
        ContentWidth = Padding + textWidth + (tileId > 0 ? 12f + tileWidth : 0f) + Padding;
        float width = ContentWidth + FadeWidth;
        layout.minWidth = layout.preferredWidth = width;
        layout.minHeight = layout.preferredHeight = height;
        layout.flexibleWidth = layout.flexibleHeight = 0;
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        Place(label.rectTransform, Padding, textWidth, height);
        if (tileId > 0) {
            if (tile == null) {
                tile = Instantiate(tilePrefab, transform).GetComponent<StaticCard>();
                tile.name = "StatusTile";
                foreach (var graphic in tile.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
                var tileLayout = tile.GetComponent<UnityEngine.UI.LayoutElement>();
                if (tileLayout != null) tileLayout.ignoreLayout = true;
            }
            tile.gameObject.SetActive(true);
            Place((RectTransform)tile.transform, Padding + textWidth + 12f, tileWidth, tileHeight);
            tile.SetTileOnlyImage(tileId);
        } else if (tile != null) {
            tile.gameObject.SetActive(false);
        }
        fade.SetSolidWidth(ContentWidth);
    }

    public void Hide() {
        if (initialized) SetContent(defaultText, 0);
        gameObject.SetActive(false);
    }

    private static void Place(RectTransform target, float x, float width, float height) {
        target.anchorMin = target.anchorMax = new Vector2(0f, 0.5f);
        target.pivot = new Vector2(0f, 0.5f);
        target.anchoredPosition = new Vector2(x, 0f);
        target.localScale = Vector3.one;
        target.sizeDelta = new Vector2(width, height);
    }
}
