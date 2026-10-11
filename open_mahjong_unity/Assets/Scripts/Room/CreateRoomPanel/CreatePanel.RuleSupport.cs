using System;
using System.Linq;
using TMPro;
using UnityEngine;

public partial class CreatePanel {
    [SerializeField, HideInInspector] private UnityEngine.UI.Button ruleSupportButton;
    [SerializeField, HideInInspector] private GameObject ruleSupportPanel;
    [SerializeField, HideInInspector] private UnityEngine.UI.Button ruleSupportCloseButton;
    [SerializeField, HideInInspector] private UnityEngine.UI.Button ruleSupportBackdrop;
    [SerializeField, HideInInspector] private UnityEngine.UI.ScrollRect ruleSupportScroll;
    [SerializeField, HideInInspector] private RectTransform ruleSupportHeader;
    [SerializeField, HideInInspector] private string ruleSupportSourceHash;

    private void BindRuleSupportTable() {
        ruleSupportButton.onClick.AddListener(ShowRuleSupportTable);
        ruleSupportCloseButton.onClick.AddListener(CloseRuleSupportTable);
        ruleSupportBackdrop.onClick.AddListener(CloseRuleSupportTable);
    }

    public void ShowRuleSupportTable() {
        ReflowRuleSupportTable();
        ShowSettingsDialog(ruleSupportPanel);
        ruleSupportScroll.StopMovement();
        ruleSupportScroll.normalizedPosition = new Vector2(0, 1);
        ruleSupportPanel.GetComponent<RuleSupportTableLayout>().RefreshLayout(true);
        ruleSupportCloseButton.Select();
    }

    public void CloseRuleSupportTable() {
        HideSettingsDialog(ruleSupportPanel);
        ruleSupportButton.Select();
    }

    private void ReflowRuleSupportTable() {
        if (!ruleSupportButton || !ruleSupportPanel) return;
        var button = (RectTransform)ruleSupportButton.transform;
        button.anchorMin = button.anchorMax = button.pivot = Vector2.one;
        button.anchoredPosition = new Vector2(-462, -16);
        button.sizeDelta = new Vector2(250, 92);
        var dialog = (RectTransform)ruleSupportPanel.transform.Find("Dialog");
        RoomFill(dialog, 28, 24, 28, 24);
        ruleSupportPanel.GetComponent<RuleSupportTableLayout>()?.RefreshLayout();
    }

#if UNITY_EDITOR
    public void BakeRuleSupportTableUi(string[] headers, string[][] rows, bool[] standard, string sourceHash, TMP_FontAsset tableFont) {
        if (headers.Length != 11 || rows.Length != standard.Length || rows.Any(row => row.Length != headers.Length))
            throw new InvalidOperationException("麻将规则支持表列数或行数不一致。");
        var entry = RoomRect("RuleSupportButton", addRuleButton.transform.parent);
        ruleSupportButton = RoomGet<UnityEngine.UI.Button>(entry.gameObject);
        ruleSupportButton.targetGraphic = RoomImage(entry, Color.white);
        ruleSupportButton.targetGraphic.raycastTarget = true;
        var entryText = RoomLabel("Label", entry, "麻将规则支持表", 28);
        entryText.alignment = TextAlignmentOptions.Center;
        RoomFill(entryText.rectTransform, 10, 0, 10, 0);
        entry.SetSiblingIndex(addRuleButton.transform.GetSiblingIndex());

        var overlay = RoomRect("RuleSupportTablePanel", transform);
        ruleSupportPanel = overlay.gameObject;
        RoomFill(overlay);
        RoomGet<CanvasGroup>(overlay.gameObject);
        var oldMotion = overlay.GetComponent<CreateRoomPopupMotion>();
        if (oldMotion) DestroyImmediate(oldMotion);
        var tableCanvas = RoomGet<Canvas>(overlay.gameObject);
        tableCanvas.overridePixelPerfect = true;
        tableCanvas.pixelPerfect = true;
        RoomGet<UnityEngine.UI.GraphicRaycaster>(overlay.gameObject);
        var backdrop = RoomRect("Backdrop", overlay);
        RoomFill(backdrop);
        ruleSupportBackdrop = RoomGet<UnityEngine.UI.Button>(backdrop.gameObject);
        ruleSupportBackdrop.targetGraphic = RoomImage(backdrop, new Color(0, 0, 0, .58f));
        ruleSupportBackdrop.targetGraphic.raycastTarget = true;
        ruleSupportBackdrop.transition = UnityEngine.UI.Selectable.Transition.None;
        var dialog = RoomRect("Dialog", overlay);
        dialog.localScale = Vector3.one;
        RoomImage(dialog, Color.white).raycastTarget = true;
        var title = RoomLabel("Title", dialog, "麻将规则支持表", 34);
        title.font = tableFont; title.fontSharedMaterial = tableFont.material;
        title.color = RoomNavy;
        RoomBox(title.rectTransform, 24, 12, 620, 62);
        var close = RoomRect("Close", dialog);
        close.anchorMin = close.anchorMax = close.pivot = Vector2.one;
        close.anchoredPosition = new Vector2(-20, -16);
        close.sizeDelta = new Vector2(112, 52);
        ruleSupportCloseButton = RoomGet<UnityEngine.UI.Button>(close.gameObject);
        ruleSupportCloseButton.targetGraphic = RoomImage(close, RoomNavy);
        ruleSupportCloseButton.targetGraphic.raycastTarget = true;
        var closeText = RoomLabel("Label", close, "关闭", 24);
        closeText.font = tableFont; closeText.fontSharedMaterial = tableFont.material;
        closeText.color = Color.white;
        closeText.alignment = TextAlignmentOptions.Center;
        RoomFill(closeText.rectTransform);

        float[] widths = { 110, 140, 144, 208, 224, 320, 176, 112, 112, 372, 154 };
        float tableWidth = widths.Sum();
        var headerViewport = RoomRect("HeaderViewport", dialog);
        RoomFill(headerViewport, 20, 84, 40, 0);
        headerViewport.anchorMin = new Vector2(0, 1);
        headerViewport.offsetMin = new Vector2(20, -140);
        RoomGet<UnityEngine.UI.RectMask2D>(headerViewport.gameObject);
        ruleSupportHeader = RoomRect("Header", headerViewport);
        RoomBox(ruleSupportHeader, 0, 0, tableWidth, 56);
        var scrollRoot = RoomRect("TableScroll", dialog);
        RoomFill(scrollRoot, 20, 140, 20, 20);
        ruleSupportScroll = RoomGet<UnityEngine.UI.ScrollRect>(scrollRoot.gameObject);
        ruleSupportScroll.horizontal = false;
        ruleSupportScroll.vertical = true;
        ruleSupportScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        ruleSupportScroll.scrollSensitivity = 70;
        var viewport = RoomRect("Viewport", scrollRoot);
        RoomFill(viewport, 0, 0, 20, 0);
        RoomImage(viewport, Color.white).raycastTarget = true;
        RoomGet<UnityEngine.UI.RectMask2D>(viewport.gameObject);
        var content = RoomRect("Content", viewport);
        ruleSupportScroll.viewport = viewport;
        ruleSupportScroll.content = content;
        // Reuse the existing scroll-bar assets and their authored hit areas.
        UnityEngine.UI.Scrollbar Bar(string name) {
            var existing = scrollRoot.Find(name);
            var bar = existing ? existing.GetComponent<UnityEngine.UI.Scrollbar>()
                : Instantiate(RoomContent.GetComponentInParent<UnityEngine.UI.ScrollRect>(true).verticalScrollbar, scrollRoot);
            bar.name = name;
            bar.SetDirection(UnityEngine.UI.Scrollbar.Direction.BottomToTop, true);
            var rect = (RectTransform)bar.transform;
            RoomFill(rect);
            rect.anchorMin = new Vector2(1, 0);
            rect.offsetMin = new Vector2(-16, 0);
            return bar;
        }
        ruleSupportScroll.horizontalScrollbar = null;
        var oldHorizontalBar = scrollRoot.Find("HorizontalScrollbar");
        if (oldHorizontalBar) DestroyImmediate(oldHorizontalBar.gameObject);
        ruleSupportScroll.verticalScrollbar = Bar("VerticalScrollbar");
        ruleSupportScroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.Permanent;
        for (int row = content.childCount - 1; row >= rows.Length; row--) DestroyImmediate(content.GetChild(row).gameObject);
        float x = 0;
        for (int col = 0; col < headers.Length; col++) {
            string heading = col == 0 ? "规则\n序号" : col == 2 ? "首页\n显示名"
                : col == 6 ? "是否\n提供\n匹配" : col == 7 ? "基础\n统计" : col == 8 ? "番种\n统计" : headers[col];
            SupportCell(ruleSupportHeader, col, heading, x, 0, widths[col], 56, RoomNavy, Color.white, tableFont, 22);
            x += widths[col];
        }
        float y = 0;
        int group = -1;
        string mainRule = null;
        for (int row = 0; row < rows.Length; row++) {
            if (rows[row][1] != mainRule) { group++; mainRule = rows[row][1]; }
            var line = RoomRect("Rule_" + rows[row][0], content);
            Color background = group % 2 == 0 ? Color.white : new Color32(244, 247, 250, 255);
            float height = 62;
            x = 0;
            for (int col = 0; col < headers.Length; col++) {
                Color fill = background, ink = new Color32(40, 59, 76, 255);
                if (standard[row] && (col == 1 || col == 3 || col == 4 || col == 5)) {
                    fill = new Color32(255, 240, 194, 255); ink = new Color32(113, 79, 10, 255);
                }
                if (col == 2 && rows[row][col].Length > 0) {
                    fill = new Color32(220, 238, 255, 255); ink = new Color32(20, 93, 160, 255);
                }
                if (col >= 6 && col <= 8) {
                    if (rows[row][col] == (col == 6 ? "是" : "有")) {
                        fill = col == 6 ? new Color32(218, 241, 241, 255) : col == 7 ? new Color32(222, 240, 221, 255) : new Color32(235, 223, 250, 255);
                        ink = col == 6 ? new Color32(24, 105, 106, 255) : col == 7 ? new Color32(39, 99, 56, 255) : new Color32(104, 61, 149, 255);
                    } else ink = new Color32(100, 111, 122, 255);
                }
                var label = SupportCell(line, col, rows[row][col], x, 0, widths[col], 62, fill, ink, tableFont, 22);
                height = Mathf.Max(height, label.GetPreferredValues(rows[row][col], widths[col] - 16, 0).y + 22);
                x += widths[col];
            }
            RoomBox(line, 0, y, tableWidth, height);
            foreach (RectTransform cell in line) cell.sizeDelta = new Vector2(cell.sizeDelta.x, height);
            y += height;
        }
        RoomBox(content, 0, 0, tableWidth, y);
        var layout = RoomGet<RuleSupportTableLayout>(overlay.gameObject);
        layout.Configure(ruleSupportScroll, ruleSupportHeader, dialog);
        ruleSupportSourceHash = sourceHash;
        ReflowRoomPresentation();
        ReflowRuleSupportTable();
        layout.RefreshLayout(true);
        overlay.gameObject.SetActive(false);
        UnityEditor.EditorUtility.SetDirty(this);
    }

    private TMP_Text SupportCell(Transform parent, int col, string value, float x, float y, float width, float height,
        Color fill, Color ink, TMP_FontAsset font, float fontSize) {
        var rect = RoomRect("Column_" + col, parent);
        RoomBox(rect, x, y, width, height);
        RoomImage(rect, fill);
        var label = RoomLabel("Text", rect, value, fontSize);
        label.font = font; label.fontSharedMaterial = font.material;
        label.color = ink;
        label.fontStyle = FontStyles.Normal;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        label.richText = false;
        // Inactive UI has not received OnEnable yet; measure in canvas units when baking.
        label.isOrthographic = true;
        RoomFill(label.rectTransform, 8, 8, 8, 8);
        return label;
    }

    public void ValidateRuleSupportTableUi(string[] headers, string[][] rows, string sourceHash) {
        if (!ruleSupportButton || !ruleSupportPanel || !ruleSupportCloseButton || !ruleSupportBackdrop || !ruleSupportScroll || !ruleSupportHeader)
            throw new InvalidOperationException(name + " 麻将规则支持表引用缺失。");
        if (ruleSupportSourceHash != sourceHash || ruleSupportPanel.activeSelf || ruleSupportScroll.content.childCount != rows.Length)
            throw new InvalidOperationException(name + " 麻将规则支持表未同步或初始显示状态错误。");
        if (ruleSupportScroll.horizontal || ruleSupportScroll.horizontalScrollbar || !ruleSupportPanel.GetComponent<RuleSupportTableLayout>())
            throw new InvalidOperationException(name + " 支持表必须自动适配宽度，仅允许纵向滚动。");
        for (int col = 0; col < headers.Length; col++)
            if (ruleSupportHeader.GetChild(col).GetComponentInChildren<TMP_Text>().text.Replace("\n", "") != headers[col])
                throw new InvalidOperationException(name + " 支持表表头不一致。");
        for (int row = 0; row < rows.Length; row++)
            for (int col = 0; col < headers.Length; col++)
                if (ruleSupportScroll.content.GetChild(row).GetChild(col).GetComponentInChildren<TMP_Text>().text != rows[row][col])
                    throw new InvalidOperationException(name + " 支持表内容不一致：" + row + "/" + col);
    }
#endif
}
