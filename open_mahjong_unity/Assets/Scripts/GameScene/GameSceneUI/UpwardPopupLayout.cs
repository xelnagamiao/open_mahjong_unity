using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// A bottom-anchored popup: keep each choice (or tile and its hint) together,
/// wrap to the available canvas width, and paginate only when height runs out.
/// Owns the outer layout; the individual cards retain their own sizing/layout.
/// </summary>
[DisallowMultipleComponent]
public sealed class UpwardPopupLayout : MonoBehaviour {
    const float Margin = 20f, Padding = 16f, Gap = 16f, HintGap = 13f, NavigationHeight = 42f;
    const float PreferredMaxWidth = 1280f;
    readonly List<Item> items = new List<Item>();
    readonly List<Row> rows = new List<Row>();
    readonly List<List<Row>> pages = new List<List<Row>>();
    RectTransform rect, tileRow, hintRow;
    Vector2 origin, originalPivot;
    TMP_Text labelTemplate, pageText;
    RectTransform navigation;
    UnityEngine.UI.Button previous, next;
    int signature, contentSignature;
    bool initialized;
    public int PageIndex { get; private set; }
    public int PageCount => pages.Count;
    public bool HasMultiplePages => PageCount > 1;

    sealed class Item {
        public RectTransform Main, Hint;
        public Vector2 Size;
        public float Scale;
        public bool Visible;
    }
    sealed class Row {
        public readonly List<Item> Items = new List<Item>();
        public float Width, Height;
    }

    public static UpwardPopupLayout ForActions(Transform parent, TMP_Text label) {
        var layout = parent.GetComponent<UpwardPopupLayout>() ?? parent.gameObject.AddComponent<UpwardPopupLayout>();
        layout.labelTemplate = label;
        layout.Refresh();
        return layout;
    }

    public void SetHintRows(RectTransform tiles, RectTransform hints) {
        tileRow = tiles; hintRow = hints;
        DisableLayout(tiles); DisableLayout(hints);
        labelTemplate = hints.GetComponentInChildren<TMP_Text>(true);
        Refresh();
    }

    static void DisableLayout(RectTransform target) {
        foreach (var layout in target.GetComponents<UnityEngine.UI.LayoutGroup>()) layout.enabled = false;
        foreach (var fitter in target.GetComponents<UnityEngine.UI.ContentSizeFitter>()) fitter.enabled = false;
    }

    void Initialize() {
        if (initialized) return;
        rect = (RectTransform)transform;
        origin = rect.anchoredPosition; originalPivot = rect.pivot;
        DisableLayout(rect);
        initialized = true;
    }

    void LateUpdate() {
        // Handles resizing, skin aspect changes and asynchronously resolved hint heights.
        int current = GetSignature();
        if (current != signature) Refresh();
    }

    int GetSignature() {
        unchecked {
            int hash = Screen.width * 397 ^ Screen.height ^ Screen.safeArea.GetHashCode();
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null) hash = hash * 397 ^ ((RectTransform)canvas.rootCanvas.transform).rect.GetHashCode();
            Transform source = tileRow != null ? tileRow : transform;
            foreach (RectTransform child in source) {
                if (tileRow == null && child.GetComponent<ActionBlock>() == null) continue;
                hash = (hash * 397 ^ child.GetInstanceID()) * 397 ^ child.rect.size.GetHashCode();
            }
            if (hintRow != null) foreach (RectTransform child in hintRow) hash = hash * 397 ^ child.rect.size.GetHashCode();
            return hash;
        }
    }

    public void SetPage(int page) {
        PageIndex = Mathf.Clamp(page, 0, Mathf.Max(0, PageCount - 1));
        Refresh();
    }

    public void Refresh() {
        Initialize();
        items.Clear(); rows.Clear(); pages.Clear();
        Transform source = tileRow != null ? tileRow : transform;
        int content = 0;
        foreach (RectTransform child in source) {
            if (tileRow == null && child.GetComponent<ActionBlock>() == null) continue;
            RectTransform hint = hintRow != null && child.GetSiblingIndex() < hintRow.childCount
                ? (RectTransform)hintRow.GetChild(child.GetSiblingIndex()) : null;
            // Child fitters measure candidate cards, never the popup's wrapping/position.
            if (tileRow == null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(child);
            Vector2 size = child.rect.size;
            if (hint != null) { size.x = Mathf.Max(size.x, hint.rect.width); size.y += HintGap + hint.rect.height; }
            items.Add(new Item { Main = child, Hint = hint, Size = size });
            unchecked { content = content * 397 ^ child.GetInstanceID(); }
        }
        if (content != contentSignature) { contentSignature = content; PageIndex = 0; }
        if (items.Count == 0) {
            if (navigation != null) navigation.gameObject.SetActive(false);
            rect.sizeDelta = Vector2.zero;
            signature = GetSignature();
            return;
        }

        Rect bounds = AvailableBounds();
        float maxWidth = Mathf.Max(1f, Mathf.Min(PreferredMaxWidth, bounds.width) - Padding * 2);
        float firstHeight = 0;
        foreach (var item in items) firstHeight = Mathf.Max(firstHeight, item.Size.y * Mathf.Min(1f, maxWidth / Mathf.Max(1f, item.Size.x)));
        // The former centred single row defines the bottom. Extra rows grow upward.
        float bottom = Mathf.Max(bounds.yMin, origin.y - firstHeight * originalPivot.y);
        float availableHeight = Mathf.Max(1f, bounds.yMax - bottom - Padding * 2);
        Row row = null;
        foreach (var item in items) {
            item.Scale = Mathf.Min(1f, maxWidth / Mathf.Max(1f, item.Size.x), Mathf.Max(1f, availableHeight - NavigationHeight) / Mathf.Max(1f, item.Size.y));
            Vector2 size = item.Size * item.Scale;
            if (row == null || row.Items.Count > 0 && row.Width + Gap + size.x > maxWidth + .01f) {
                row = new Row(); rows.Add(row);
            }
            row.Width += (row.Items.Count > 0 ? Gap : 0) + size.x;
            row.Height = Mathf.Max(row.Height, size.y);
            row.Items.Add(item);
        }
        float totalHeight = -Gap;
        foreach (var r in rows) totalHeight += r.Height + Gap;
        bool paged = totalHeight > availableHeight;
        float pageHeight = availableHeight - (paged ? NavigationHeight : 0);
        List<Row> pageRows = null;
        float usedHeight = 0;
        foreach (var r in rows) {
            if (pageRows == null || pageRows.Count > 0 && usedHeight + Gap + r.Height > pageHeight + .01f) {
                pageRows = new List<Row>(); pages.Add(pageRows); usedHeight = 0;
            }
            usedHeight += (pageRows.Count > 0 ? Gap : 0) + r.Height;
            pageRows.Add(r);
        }
        PageIndex = Mathf.Clamp(PageIndex, 0, pages.Count - 1);
        float width = 0, height = -Gap;
        foreach (var r in rows) width = Mathf.Max(width, r.Width);
        foreach (var r in pages[PageIndex]) height += r.Height + Gap;
        if (HasMultiplePages) { width = Mathf.Max(width, Mathf.Min(maxWidth, 302)); height += NavigationHeight; }
        width += Padding * 2; height += Padding * 2;
        rect.pivot = new Vector2(.5f, 0);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(Mathf.Clamp(origin.x, bounds.xMin + width * .5f, bounds.xMax - width * .5f), bottom);
        if (tileRow != null) { Stretch(tileRow); Stretch(hintRow); }
        foreach (var r in pages[PageIndex]) foreach (var item in r.Items) item.Visible = true;
        foreach (var item in items) {
            item.Main.gameObject.SetActive(item.Visible);
            if (item.Hint != null) item.Hint.gameObject.SetActive(item.Visible);
        }
        float y = Padding;
        foreach (var r in pages[PageIndex]) {
            float x = (width - r.Width) * .5f;
            foreach (var item in r.Items) {
                Place(item.Main, x, y, item.Scale);
                if (item.Hint != null) Place(item.Hint, x, y + (item.Main.rect.height + HintGap) * item.Scale, item.Scale);
                x += item.Size.x * item.Scale + Gap;
            }
            y += r.Height + Gap;
        }
        UpdateNavigation(width);
        signature = GetSignature();
    }

    static void Stretch(RectTransform row) {
        row.anchorMin = Vector2.zero; row.anchorMax = Vector2.one;
        row.offsetMin = row.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform child, float x, float y, float scale) {
        child.anchorMin = child.anchorMax = child.pivot = new Vector2(0, 1);
        child.anchoredPosition = new Vector2(x, -y);
        child.localScale = new Vector3(scale, scale, 1);
    }

    Rect AvailableBounds() {
        var parent = (RectTransform)transform.parent;
        var canvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (canvas == null) return new Rect(-960, -540, 1920, 1080);
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Rect pixels = canvas.pixelRect;
        // A canvas rendered into a texture has its own viewport, independent of the display safe area.
        Rect safe = camera != null && camera.targetTexture != null ? pixels : Screen.safeArea;
        Vector2 min = Vector2.Max(safe.min, pixels.min), max = Vector2.Min(safe.max, pixels.max);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, min, camera, out var low);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, max, camera, out var high);
        // anchoredPosition is relative to the anchor reference point, not the parent origin.
        Vector2 anchor = parent.rect.min + Vector2.Scale(parent.rect.size, Vector2.Lerp(rect.anchorMin, rect.anchorMax, .5f));
        return Rect.MinMaxRect(low.x - anchor.x + Margin, low.y - anchor.y + Margin,
            high.x - anchor.x - Margin, high.y - anchor.y - Margin);
    }

    void UpdateNavigation(float width) {
        if (navigation != null && navigation.parent != transform) navigation = null;
        if (!HasMultiplePages) { if (navigation != null) navigation.gameObject.SetActive(false); return; }
        if (navigation == null) {
            var go = new GameObject("PopupPages", typeof(RectTransform));
            go.layer = gameObject.layer; go.transform.SetParent(transform, false); navigation = (RectTransform)go.transform;
            previous = MakeButton("Previous", "上一页", -104, -1);
            next = MakeButton("Next", "下一页", 104, 1);
            pageText = MakeLabel(navigation, "Page", "");
            pageText.rectTransform.sizeDelta = new Vector2(90, 33);
        }
        navigation.gameObject.SetActive(true);
        navigation.anchorMin = navigation.anchorMax = navigation.pivot = new Vector2(.5f, 0);
        navigation.anchoredPosition = new Vector2(0, Padding);
        navigation.sizeDelta = new Vector2(Mathf.Min(width - Padding * 2, 302), 33);
        navigation.localScale = Vector3.one * Mathf.Min(1, (width - Padding * 2) / 302);
        pageText.text = $"{PageIndex + 1}/{PageCount}";
        previous.interactable = PageIndex > 0; next.interactable = PageIndex + 1 < PageCount;
    }

    UnityEngine.UI.Button MakeButton(string name, string caption, float x, int direction) {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        go.layer = gameObject.layer; go.transform.SetParent(navigation, false);
        var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = new Vector2(.5f, 0);
        r.sizeDelta = new Vector2(94, 33); r.anchoredPosition = new Vector2(x, 16.5f);
        var image = go.GetComponent<UnityEngine.UI.Image>(); image.color = new Color32(70, 86, 126, 255);
        var button = go.GetComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        button.onClick.AddListener(() => SetPage(PageIndex + direction));
        var label = MakeLabel(r, "Caption", caption); Stretch(label.rectTransform);
        return button;
    }

    TMP_Text MakeLabel(Transform parent, string name, string caption) {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = gameObject.layer; go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = labelTemplate != null ? labelTemplate.font : TMP_Settings.defaultFontAsset;
        text.text = caption; text.fontSize = 18; text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        return text;
    }
}
