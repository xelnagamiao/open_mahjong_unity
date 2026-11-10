using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ActionChoicePanel : MonoBehaviour {
    public enum Palette { BlueSlate, Graphite, Jade, WarmGrey }
    [SerializeField] private Palette palette = Palette.Graphite;
    [SerializeField] private UnityEngine.UI.Image background;
    [SerializeField] private UnityEngine.UI.Outline border;
    [SerializeField] private UnityEngine.UI.ScrollRect scroll;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RectTransform content;
    [SerializeField] private UnityEngine.UI.Scrollbar scrollbar;
    [SerializeField] private UnityEngine.UI.Image scrollbarTrack;
    [SerializeField] private UnityEngine.UI.Image scrollbarHandle;
    [SerializeField] private ActionChoiceRow rowTemplate;
    const float Padding = 14, Gap = 10, ScrollWidth = 24, ScrollGap = 12, Margin = 20, TopMargin = 32, ButtonGap = 16;
    readonly List<ActionChoiceRow> rows = new List<ActionChoiceRow>();
    readonly Dictionary<int, ActionChoiceRow> visible = new Dictionary<int, ActionChoiceRow>();
    IReadOnlyList<ActionCandidate> choices = Array.Empty<ActionCandidate>();
    GameObject cardPrefab;
    RectTransform rect, buttonAnchor;
    readonly Vector3[] anchorCorners = new Vector3[4];
    Vector2 origin;
    float rowWidth, rowHeight, detailsHeight, tileHeight;
    bool initialized, rebuilding;
    int viewportSignature;
    AppLanguage language;
    public int ChoiceCount => choices.Count;
    public int CreatedRowCount => rows.Count;
    public int CreatedCardCount => rows.Sum(row => row.CreatedCardCount);
    public int VisibleRowCount => visible.Count;
    public bool HasScrollbar => scrollbar.gameObject.activeSelf;
    public float ScrollPosition => scroll.verticalNormalizedPosition;
    public Palette SelectedPalette => palette;
    public ActionChoiceRow[] VisibleRows => visible.OrderBy(item => item.Key).Select(item => item.Value).ToArray();
    public bool HasReferences => background && border && scroll && viewport && content && scrollbar && scrollbarTrack && scrollbarHandle && rowTemplate && rowTemplate.HasReferences;

    public void Show(IReadOnlyList<ActionCandidate> offered, GameObject tilePrefab, RectTransform anchor = null) {
        if (!HasReferences) throw new InvalidOperationException("ActionChoicePanel requires its baked scene references");
        Initialize(); ClearRows(); buttonAnchor = anchor;
        choices = offered ?? Array.Empty<ActionCandidate>(); cardPrefab = tilePrefab;
        if (choices.Count == 0) { Hide(); return; }
        gameObject.SetActive(true); transform.SetAsLastSibling();
        rowTemplate.gameObject.SetActive(false);
        scroll.StopMovement(); content.anchoredPosition = Vector2.zero;
        language = ConfigManager.CurrentLanguage;
        Measure(); RefreshLayout();
        scroll.verticalNormalizedPosition = 1;
        RefreshVisible(); ApplyPalette();
    }

    public void Hide() {
        if (scroll != null) scroll.StopMovement();
        ClearRows(); choices = Array.Empty<ActionCandidate>(); buttonAnchor = null;
        gameObject.SetActive(false);
    }

    public void SetPalette(Palette value) { palette = value; ApplyPalette(); }
    public void ScrollTo(float normalized) {
        scroll.StopMovement(); scroll.verticalNormalizedPosition = Mathf.Clamp01(normalized); RefreshVisible();
    }
    public void RevealChoice(int index) {
        if (index < 0 || index >= choices.Count) throw new ArgumentOutOfRangeException(nameof(index));
        float distance = Mathf.Max(0, content.rect.height - viewport.rect.height);
        ScrollTo(distance > 0 ? 1 - Mathf.Clamp(index * (rowHeight + Gap), 0, distance) / distance : 1);
    }

    void Initialize() {
        if (initialized) return;
        rect = (RectTransform)transform; origin = rect.anchoredPosition;
        scroll.onValueChanged.AddListener(OnScroll);
        initialized = true;
    }
    void OnScroll(Vector2 value) { if (!rebuilding) RefreshVisible(); }
    void OnDestroy() { if (scroll != null) scroll.onValueChanged.RemoveListener(OnScroll); }
    void OnValidate() { if (HasReferences) ApplyPalette(); }
    void LateUpdate() {
        if (!initialized || choices.Count == 0) return;
        if (language != ConfigManager.CurrentLanguage) {
            language = ConfigManager.CurrentLanguage;
            float position = ScrollPosition;
            Measure(); ClearRows(); RefreshLayout(); ScrollTo(position);
        } else if (viewportSignature != Signature()) RefreshLayout();
    }

    void Measure() {
        rowWidth = 480; tileHeight = 0; detailsHeight = 0;
        var heights = new Dictionary<int, float>();
        foreach (var choice in choices) {
            int[][] groups = choice.TileGroups ?? new[] { choice.TileIds };
            float width = groups.Sum(group => Math.Max(0, group.Length * (ActionChoiceRow.TileWidth + ActionChoiceRow.TileSpacing) - ActionChoiceRow.TileSpacing))
                + Math.Max(0, groups.Length - 1) * ActionChoiceRow.GroupGap + ActionChoiceRow.SidePadding * 2;
            rowWidth = Mathf.Max(rowWidth, width);
            foreach (int tile in choice.TileIds) {
                if (!heights.TryGetValue(tile, out float h)) { h = TileFaceFit.HandSizeFor(tile, ActionChoiceRow.TileWidth).y; heights.Add(tile, h); }
                tileHeight = Mathf.Max(tileHeight, h);
            }
        }
        var measured = new HashSet<string>();
        ActionChoiceRow.ConfigureText(rowTemplate.Details, 18);
        foreach (var choice in choices) if (!string.IsNullOrEmpty(choice.Details) && measured.Add(choice.Details)) {
            TableText.Set(rowTemplate.Details, choice.Details, choice.TextContext);
            detailsHeight = Mathf.Max(detailsHeight, rowTemplate.Details.GetPreferredValues(rowTemplate.Details.text,
                rowWidth - ActionChoiceRow.SidePadding * 2, Mathf.Infinity).y + 6);
        }
        rowHeight = Mathf.Ceil(58 + detailsHeight + tileHeight);
    }

    public void RefreshLayout() {
        if (!initialized || choices.Count == 0) return;
        rebuilding = true;
        float position = scroll.verticalNormalizedPosition;
        Rect bounds = AvailableBounds();
        Vector2 placement = PlacementAboveButton();
        float bottom = Mathf.Max(bounds.yMin, placement.y);
        float contentHeight = choices.Count * (rowHeight + Gap) - Gap;
        int fullRows = Math.Min(3, choices.Count);
        float preferredHeight = fullRows * (rowHeight + Gap) - Gap;
        // Reserve the scrollbar column when fitting width, so enabling overflow
        // cannot change card/text scale or feed back into the height calculation.
        float maximumWidth = rowWidth + Padding * 2 + ScrollWidth + ScrollGap;
        float scale = Mathf.Min(1, bounds.width / maximumWidth);
        float availableHeight = Mathf.Max(0, bounds.yMax - bottom) / scale;
        float verticalPadding = Mathf.Min(Padding, availableHeight * .5f);
        float viewportHeight = Mathf.Min(preferredHeight, Mathf.Max(0, availableHeight - verticalPadding * 2));
        bool overflow = contentHeight > viewportHeight + .01f;
        float width = rowWidth + Padding * 2 + (overflow ? ScrollWidth + ScrollGap : 0);
        float height = viewportHeight + verticalPadding * 2;
        rect.pivot = new Vector2(.5f, 0); rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one * scale;
        float centerX = buttonAnchor != null ? placement.x - width * scale / 2 : placement.x;
        rect.anchoredPosition = new Vector2(Mathf.Clamp(centerX, bounds.xMin + width * scale / 2, bounds.xMax - width * scale / 2), bottom);
        SetRect(viewport, Padding, -verticalPadding, rowWidth, viewportHeight);
        // Virtual content includes every logical row, not only instantiated children.
        SetRect(content, 0, content.anchoredPosition.y, rowWidth, contentHeight);
        scrollbar.gameObject.SetActive(overflow && viewportHeight > 0);
        SetRect((RectTransform)scrollbar.transform, width - Padding - ScrollWidth, -verticalPadding, ScrollWidth, viewportHeight);
        scroll.vertical = overflow;
        scroll.verticalNormalizedPosition = overflow ? position : 1;
        rebuilding = false; viewportSignature = Signature(); RefreshVisible();
    }

    void RefreshVisible() {
        if (rebuilding || choices.Count == 0) return;
        if (viewport.rect.height <= 0) { ClearRows(); return; }
        float top = Mathf.Clamp(content.anchoredPosition.y, 0, Mathf.Max(0, content.rect.height - viewport.rect.height));
        int first = Mathf.Clamp(Mathf.FloorToInt(top / (rowHeight + Gap)), 0, choices.Count - 1);
        int last = Mathf.Clamp(Mathf.CeilToInt((top + viewport.rect.height) / (rowHeight + Gap)) - 1, first, choices.Count - 1);
        foreach (int index in visible.Keys.Where(index => index < first || index > last).ToArray()) {
            visible[index].Release(); visible.Remove(index);
        }
        for (int index = first; index <= last; index++) {
            if (!visible.TryGetValue(index, out var row)) {
                row = rows.FirstOrDefault(item => item.Index < 0);
                if (row == null) { row = Instantiate(rowTemplate, content); rows.Add(row); }
                row.gameObject.SetActive(true);
                row.Bind(index, choices[index], cardPrefab, rowWidth, rowHeight, detailsHeight);
                visible.Add(index, row); ApplyRowPalette(row);
            }
            var rowRect = (RectTransform)row.transform;
            rowRect.anchoredPosition = new Vector2(0, -index * (rowHeight + Gap));
        }
    }

    void ClearRows() { foreach (var row in rows) row.Release(); visible.Clear(); }
    int Signature() {
        var canvas = GetComponentInParent<Canvas>()?.rootCanvas;
        return Screen.width ^ Screen.height * 397 ^ MobileViewportLayout.SafeArea.GetHashCode()
            ^ (canvas == null ? 0 : ((RectTransform)canvas.transform).rect.GetHashCode())
            ^ (buttonAnchor == null ? 0 : buttonAnchor.localToWorldMatrix.GetHashCode() ^ buttonAnchor.rect.GetHashCode());
    }
    Vector2 PlacementAboveButton() {
        if (buttonAnchor == null) return origin;
        var parent = (RectTransform)transform.parent;
        var sourceCanvas = buttonAnchor.GetComponentInParent<Canvas>()?.rootCanvas;
        var targetCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        Camera sourceCamera = sourceCanvas == null || sourceCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : sourceCanvas.worldCamera;
        Camera targetCamera = targetCanvas == null || targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCanvas.worldCamera;
        buttonAnchor.GetWorldCorners(anchorCorners);
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, top = float.NegativeInfinity;
        foreach (var corner in anchorCorners) {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(sourceCamera, corner);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, targetCamera, out var local);
            minX = Mathf.Min(minX, local.x); maxX = Mathf.Max(maxX, local.x); top = Mathf.Max(top, local.y);
        }
        return new Vector2(Mathf.Lerp(minX, maxX, .25f), top + ButtonGap) - AnchorReference(parent);
    }
    Vector2 AnchorReference(RectTransform parent) => parent.rect.min
        + Vector2.Scale(parent.rect.size, Vector2.Lerp(rect.anchorMin, rect.anchorMax, .5f));
    Rect AvailableBounds() {
        var parent = (RectTransform)transform.parent;
        var canvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (canvas == null) return new Rect(-960, -540, 1920, 1080);
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Rect pixels = canvas.pixelRect;
        Rect safe = camera != null && camera.targetTexture != null ? pixels : MobileViewportLayout.SafeArea;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Vector2.Max(safe.min, pixels.min), camera, out var low);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Vector2.Min(safe.max, pixels.max), camera, out var high);
        Vector2 anchor = AnchorReference(parent);
        return Rect.MinMaxRect(low.x - anchor.x + Margin, low.y - anchor.y + Margin, high.x - anchor.x - Margin, high.y - anchor.y - TopMargin);
    }
    static void SetRect(RectTransform value, float x, float y, float width, float height) {
        value.anchorMin = value.anchorMax = value.pivot = new Vector2(0, 1);
        value.anchoredPosition = new Vector2(x, y); value.sizeDelta = new Vector2(width, height); value.localScale = Vector3.one;
    }

    struct Colors { public Color Outer, Row, Hover, Text, Detail, Edge, Thumb; }
    static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out var color); return color; }
    Colors CurrentColors() {
        string[] values;
        switch (palette) {
            case Palette.Graphite: values = new[] { "#191E28F7", "#2A313D", "#374250", "#F1F3F6", "#C4CDD8", "#566476", "#B3C2D3" }; break;
            case Palette.Jade: values = new[] { "#172D2DF7", "#28443F", "#34594F", "#F1F6F0", "#C3D7CC", "#50766B", "#AACDBB" }; break;
            case Palette.WarmGrey: values = new[] { "#302B27F7", "#474039", "#5A5147", "#FAF4E8", "#DED1BF", "#7B6C58", "#D1BFA2" }; break;
            default: values = new[] { "#182B38F7", "#294555", "#365A6C", "#F0F6F8", "#C2D5DF", "#52798F", "#A9CADA" }; break;
        }
        return new Colors { Outer = Hex(values[0]), Row = Hex(values[1]), Hover = Hex(values[2]), Text = Hex(values[3]), Detail = Hex(values[4]), Edge = Hex(values[5]), Thumb = Hex(values[6]) };
    }
    public void ApplyPalette() {
        if (!HasReferences) return;
        var colors = CurrentColors(); background.color = colors.Outer; border.effectColor = colors.Edge;
        scrollbarTrack.color = new Color(colors.Edge.r, colors.Edge.g, colors.Edge.b, .55f);
        scrollbarHandle.color = colors.Thumb;
        rowTemplate.ApplyColors(colors.Row, colors.Hover, colors.Text, colors.Detail);
        foreach (var row in rows) row.ApplyColors(colors.Row, colors.Hover, colors.Text, colors.Detail);
    }
    void ApplyRowPalette(ActionChoiceRow row) {
        var colors = CurrentColors(); row.ApplyColors(colors.Row, colors.Hover, colors.Text, colors.Detail);
    }
}
