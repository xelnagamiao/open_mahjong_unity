using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One continuous transport geometry shared by every artwork palette.</summary>
public static class TableHudReplayLayout {
    private static readonly string[] ControlNames = { "TileList", "GameRound", "LaterXunmu", "Xunmu", "NextXunmu", "LaterStep", "NextStep" };
    public static void Reflow(RectTransform panel) {
        var buttons = new Button[7];
        for (int i = 0; i < buttons.Length; i++) buttons[i] = panel.Find(ControlNames[i])?.GetComponent<Button>();
        var auto = panel.Find("AutoPlay")?.GetComponent<Button>();
        Arrange(panel, buttons, auto && auto.gameObject.activeSelf ? auto : null);
    }
    public static void Arrange(RectTransform panel, Button[] buttons, Button autoPlay) {
        if (!panel || buttons == null || buttons.Length != 7) return;
        var theme = panel.GetComponentInParent<TableHudTheme>();
        var palette = TableHudTheme.GetPalette(theme ? theme.CurrentStyle : TableHudTheme.Style.OriginalBlue);
        var ordered = new List<Button> { buttons[0], buttons[1], buttons[2], buttons[3], buttons[4], buttons[5] };
        var widths = new List<float> { 92, 212, 82, 130, 82, 82 };
        if (autoPlay) { ordered.Add(autoPlay); widths.Add(112); }
        ordered.Add(buttons[6]); widths.Add(82);
        var livePanel = panel.Find("SpectatingPanel") as RectTransform;
        var liveButton = livePanel ? livePanel.GetComponentInChildren<Button>(true) : null;
        bool showLive = livePanel && livePanel.gameObject.activeSelf && liveButton;
        if (showLive) { ordered.Add(liveButton); widths.Add(156); }
        float total = 8 * (ordered.Count - 1) + 24;
        foreach (float width in widths) total += width;
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(1040,total + 32));
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 88);
        panel.anchoredPosition = new Vector2(Mathf.Round(panel.anchoredPosition.x),Mathf.Round(panel.anchoredPosition.y));
        float x = -total * .5f;
        int divider = 0;
        for (int i = 0; i < ordered.Count; i++) {
            if (i == 2 || i == 5) { PlaceDivider(panel, ++divider, x + 2, palette); x += 12; }
            if (ordered[i]) {
                var rect = showLive && ordered[i] == liveButton ? livePanel : (RectTransform)ordered[i].transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f);
                rect.sizeDelta = new Vector2(widths[i],56);
                rect.anchoredPosition = new Vector2(x + widths[i] * .5f,0);
                if (showLive && ordered[i] == liveButton) ArrangeLiveButton(livePanel,liveButton,palette);
                else ArrangeButton(ordered[i],palette,theme ? theme.CurrentTurnBackground : TableHudTheme.TurnBackground.Glass);
            }
            x += widths[i] + 8;
        }
        if (theme) {
            var art = Artwork(panel,"ReferenceBackdrop");
            art.sprite = theme.TransportSprite; FitArtworkHeight(art);
            art.material = theme.ReferenceMaterial; art.color = Color.white;
            art.transform.SetAsFirstSibling();
        }
        foreach (var popup in panel.GetComponentsInChildren<TableHudSelectorPopup>(true)) popup.Reposition();
        panel.GetComponentInChildren<TableHudWallPopup>(true)?.Reposition();
    }
    private static void ArrangeLiveButton(RectTransform panel, Button button, TableHudTheme.Palette p) {
        var rect = (RectTransform)button.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var body = button.GetComponent<TableHudSurface>();
        if (body) {
            body.color = new Color(1,1,1,.11f); body.bottomColor = new Color(1,1,1,.17f);
            body.edgeColor = p.edge; body.edgeWidth = 1; body.corner = 4; body.SetVerticesDirty();
        }
        foreach (var text in panel.GetComponentsInChildren<TMP_Text>(true)) {
            bool state = text.name == "SpectatingStateText";
            TableHudTypography.Apply(text);
            text.fontSize = text.fontSizeMax = state ? 14 : 20; text.fontSizeMin = state ? 12 : 16;
            text.enableAutoSizing = true; text.color = state ? p.muted : p.text;
            text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.NoWrap;
            var r = text.rectTransform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f,.5f);
            r.sizeDelta = new Vector2(144,state ? 20 : 28); r.anchoredPosition = new Vector2(0,state ? 15 : -10);
        }
    }
    private static void ArrangeButton(Button button,TableHudTheme.Palette p,TableHudTheme.TurnBackground background) {
        bool value = button.name == "TileList" || button.name == "GameRound" || button.name == "Xunmu";
        bool turn = button.name == "LaterXunmu" || button.name == "NextXunmu";
        bool light = turn && (background == TableHudTheme.TurnBackground.Ice || background == TableHudTheme.TurnBackground.Porcelain);
        var label = button.GetComponentInChildren<TMP_Text>(true);
        var body = button.transform.Find("HudSurface")?.GetComponent<TableHudSurface>();
        if (body) {
            body.enabled = !value; body.edgeWidth = 0; body.corner = 4;
            body.color = new Color(1,1,1,.11f); body.bottomColor = new Color(1,1,1,.17f);
            var r = body.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            // The supplied value artwork has two transparent pixels on each edge.
            // Align the painted edges, not merely the 56 px button hit rectangles.
            float inset = 56f * 2f / 138f;
            r.offsetMin = new Vector2(0,inset); r.offsetMax = new Vector2(0,-inset);
            if (turn) {
                if (background == TableHudTheme.TurnBackground.Ice) {
                    body.color = new Color32(216,235,255,245); body.bottomColor = new Color32(151,190,231,245);
                    body.edgeColor = new Color32(224,241,255,255); body.edgeWidth = 1;
                } else if (background == TableHudTheme.TurnBackground.Porcelain) {
                    body.color = new Color32(250,252,255,255); body.bottomColor = new Color32(207,216,230,255);
                    body.edgeColor = new Color32(162,181,205,255); body.edgeWidth = 1;
                } else if (background == TableHudTheme.TurnBackground.Outline) {
                    body.color = body.bottomColor = new Color(1,1,1,.035f);
                    body.edgeColor = new Color32(184,214,250,190); body.edgeWidth = 1;
                }
            }
            body.SetVerticesDirty(); if (!value) button.targetGraphic = body;
        }
        if (value) {
            var art = Artwork(button.transform,"ReferenceValue");
            art.sprite = Resources.Load<Sprite>("image/UI/TableHud/Source/ValuePlate");
            FitArtworkHeight(art);
            art.color = p.valueTint; art.material = null;
            art.transform.SetAsFirstSibling(); button.targetGraphic = art;
        }
        if (label) {
            label.color = value ? p.valueText : light ? new Color32(24,49,80,255) : p.text; label.fontSize = value ? 26 : 16;
            TableHudTypography.Apply(label,value);
            label.fontSizeMax = label.fontSize; label.fontSizeMin = value ? 18 : 12;
            label.enableAutoSizing = true; label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.Center;
            var r = label.rectTransform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f,.5f);
            float width = ((RectTransform)button.transform).rect.width;
            r.sizeDelta = new Vector2(width - (value ? 16 : 6),value ? 44 : 24);
            r.anchoredPosition = new Vector2(0,value ? 0 : -15);
        }
        for (int n = 0; n < 2; n++) {
            var arrow = button.transform.Find(n == 0 ? "SourceArrow" : "SourceArrowSecond") as RectTransform;
            if (!arrow) continue;
            bool doubled = button.name == "LaterXunmu" || button.name == "NextXunmu";
            arrow.gameObject.SetActive(n == 0 || doubled);
            arrow.sizeDelta = new Vector2(doubled ? 20 : 28,28);
            arrow.anchoredPosition = new Vector2(doubled ? (n == 0 ? -7 : 7) : 0,11);
            var image = arrow.GetComponent<Image>(); if (image) image.color = light ? new Color32(28,57,92,255) : Color.white;
        }
        var underline = button.transform.Find("ReadingMark"); if (underline) underline.gameObject.SetActive(false);
        button.GetComponent<TableHudPlaybackArtwork>()?.RefreshLayout();
    }
    private static Image Artwork(Transform parent,string name) {
        var found = parent.Find(name);
        var image = found ? found.GetComponent<Image>() : new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<Image>();
        if (!found) image.transform.SetParent(parent,false);
        image.raycastTarget = false; image.enabled = true;
        var r = image.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
        return image;
    }
    private static void FitArtworkHeight(Image image) {
        if (!image.sprite) return;
        image.type = Image.Type.Sliced;
        // Unity's sliced Image uses sprite PPU / canvas reference PPU. Match that
        // conversion to the source height: corners scale equally in X and Y,
        // only the horizontal center stretches, and the gradient stays intact.
        float reference = image.canvas ? image.canvas.referencePixelsPerUnit : 100f;
        image.pixelsPerUnitMultiplier = image.sprite.rect.height * reference /
            (Mathf.Max(1,image.rectTransform.rect.height) * image.sprite.pixelsPerUnit);
    }
    private static void PlaceDivider(RectTransform panel,int index,float x,TableHudTheme.Palette p) {
        var s = panel.Find("Divider_" + index)?.GetComponent<TableHudSurface>(); if (!s) return;
        s.enabled = true; s.rectTransform.sizeDelta = new Vector2(1,52); s.rectTransform.anchoredPosition = new Vector2(x,0);
        var edge = p.accent; edge.a = .3f;
        s.color = s.bottomColor = edge; s.edgeWidth = 0; s.corner = 0; s.SetVerticesDirty();
    }
}
