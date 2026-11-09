using System;
using TMPro;
using UnityEngine;

/// <summary>牌面设置的单列排版，沿用设置面板的滑条。</summary>
public sealed class CardFaceLayoutPresentation : MonoBehaviour
{
    [Serializable] public sealed class Row
    {
        public RectTransform root;
        public TMP_Text label, value;
        public UnityEngine.UI.Button reset, minus, plus;
        public UnityEngine.UI.Slider slider;
        public CardLayoutRoundedGraphic track, fill, handle;
        public RectTransform ticks;
    }
    public CardFaceScrollLayout layout;
    public RectTransform standard, stylePicker, backSection, faceSection;
    public RectTransform backgroundSection, handAdjustmentTitle, tableAdjustmentTitle;
    public RectTransform backgroundAnchorTitle;
    public RectTransform backgroundLibrary, backLibrary, faceLibrary;
    public UnityEngine.UI.Button[] styleButtons;
    public Row[] rows;
    public Row[] tableRows;
    public Row[] backgroundRows;
    public RectTransform[] sectionTitleLabels, sectionTitleBackdrops;
    public CardFaceUploadedPackPicker uploadedPacks;
    public float PreferredContentHeight { get; private set; } = 920;

    void OnEnable() => RefreshLayout();

    public void RefreshLayout()
    {
        if (rows == null || rows.Length == 0 || tableRows == null || tableRows.Length == 0 || backgroundSection == null
            || backgroundRows == null || backgroundRows.Length == 0 || backgroundAnchorTitle == null) return;
        if (stylePicker != null) stylePicker.gameObject.SetActive(false);
        float y = 0;
        float faceBase = ChoiceBottom(faceSection) + 16;
        float faceHeight = uploadedPacks != null ? faceBase + uploadedPacks.ContentHeight : faceBase + (faceLibrary.gameObject.activeSelf ? 40 : 0);
        if (uploadedPacks != null) Place((RectTransform)uploadedPacks.transform, 0, faceBase, 610, uploadedPacks.ContentHeight);
        Place(faceSection, 0, y, 610, faceHeight);
        y += faceHeight;
        Place(handAdjustmentTitle, 0, y, 610, 42); y += 42;
        foreach (var row in rows) {
            PlaceRow(row, y);
            y += RowHeight;
        }
        Place(tableAdjustmentTitle, 0, y, 610, 42); y += 42;
        foreach (var row in tableRows) {
            PlaceRow(row, y);
            y += RowHeight;
        }
        y += 18;
        float anchorY = ChoiceBottom(backgroundSection) + 16 + (backgroundLibrary.gameObject.activeSelf ? 40 : 0);
        Place(backgroundAnchorTitle, 0, anchorY, 610, 36); anchorY += 38;
        foreach (var row in backgroundRows) { PlaceRow(row, anchorY); anchorY += RowHeight; }
        Place(backgroundSection, 0, y, 610, anchorY + 8);
        y += anchorY + 26;
        float backHeight = ChoiceBottom(backSection) + 16 + (backLibrary.gameObject.activeSelf ? 40 : 0);
        Place(backSection, 0, y, 610, backHeight);
        y += backHeight;
        standard.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, y);
        PreferredContentHeight = 74 + y + 18;
        layout.RefreshSettingsHeight(PreferredContentHeight);
        if (sectionTitleLabels != null && sectionTitleBackdrops != null)
            for (int i = 0; i < sectionTitleLabels.Length && i < sectionTitleBackdrops.Length; i++) {
                var title = sectionTitleLabels[i];
                Place(sectionTitleBackdrops[i], title.anchoredPosition.x, -title.anchoredPosition.y, 610, title.rect.height);
            }
    }

    public const float RowHeight = 64;

    public static float ChoiceGridHeight(int choices, bool add)
    {
        float bottom = choices > 0 ? (choices - 1) / 3 * 86 + 80 : 0;
        if (add) bottom = Mathf.Max(bottom, choices / 3 * 86 + (choices % 3 == 0 ? 0 : 9) + 62);
        return bottom;
    }

    public static void PlaceChoice(RectTransform rect, int index, bool add)
        => Place(rect, index % 3 * 206, index / 3 * 86 + (add && index % 3 != 0 ? 9 : 0), add ? 62 : 198, add ? 62 : 80);

    static float ChoiceBottom(RectTransform section)
    {
        float bottom = 0;
        foreach (Transform child in section)
            if (child.gameObject.activeSelf && child.GetComponent<UnityEngine.UI.Button>() != null)
            {
                var rect = (RectTransform)child;
                bottom = Mathf.Max(bottom, -rect.anchoredPosition.y + rect.rect.height);
            }
        return bottom;
    }

    public static void PlaceRow(Row row, float y)
    {
        Place(row.root, 0, y, 610, RowHeight);
        Place(row.label.rectTransform, 12, 12, 250, 32);
        Place(row.value.rectTransform, 438, 12, 88, 32);
        Place((RectTransform)row.reset.transform, 534, 12, 76, 40);
        Place((RectTransform)row.slider.transform, 12, 43, 506, 18);
        row.label.margin = row.value.margin = Vector4.zero;
        row.label.alignment = TextAlignmentOptions.MidlineLeft;
        row.value.alignment = TextAlignmentOptions.MidlineRight;
        row.value.overflowMode = TextOverflowModes.Overflow;
        row.value.textWrappingMode = TextWrappingModes.NoWrap;
        var caption = row.reset.GetComponentInChildren<TMP_Text>(true);
        if (caption != null) {
            caption.alignment = TextAlignmentOptions.Center;
            caption.margin = Vector4.zero;
            caption.rectTransform.anchorMin = Vector2.zero;
            caption.rectTransform.anchorMax = Vector2.one;
            caption.rectTransform.offsetMin = caption.rectTransform.offsetMax = Vector2.zero;
        }
    }

    public static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); rect.localScale = Vector3.one;
    }
}
