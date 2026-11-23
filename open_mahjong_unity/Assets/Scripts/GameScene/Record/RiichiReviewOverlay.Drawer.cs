using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class RiichiReviewOverlay {
    private sealed class DrawerButton {
        public RectTransform Rect;
        public TableHudSurface Surface;
        public TextMeshProUGUI Text;
        public Button Button;
        public void Paint(bool active) {
            Surface.color = Surface.bottomColor = active ? Mint : Slate;
            Surface.edgeColor = active ? Mint : Edge;
            Surface.SetVerticesDirty();
            Text.color = active ? Ink : Paper;
        }
    }

    private ScrollRect scroll;
    private RectTransform content, modes, comparison, footer;
    private DrawerButton buttonB, buttonC, allButton, phaseButton, actualButton, recommendedButton;
    private DrawerButton previous, next, recovery;
    private TextMeshProUGUI displayTitle, pressHint, phaseTitle, detailTitle, detailSummary, tiles, expectation, choicesTitle, notice;
    private readonly List<DrawerButton> choiceButtons = new List<DrawerButton>();
    private float lastWidth;

    private static DrawerButton MakeButton(Transform parent, string name, string caption, Action click, float size = 19) {
        var rect = RectChild(parent, name);
        var surface = Surface(rect, Slate, Edge, true);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = surface;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => click());
        var label = Label(rect, "Label", caption, size, Paper, TextAlignmentOptions.Center);
        Stretch(label.rectTransform, 3);
        return new DrawerButton { Rect = rect, Surface = surface, Text = label, Button = button };
    }

    private void BuildHandle() {
        var tab = MakeButton(root, "OpenReview", "‹  复盘", () => SetDrawer(true), 22);
        handle = tab.Rect;
        handle.anchorMin = handle.anchorMax = new Vector2(1, .48f);
        handle.pivot = new Vector2(1, .5f);
        handle.sizeDelta = new Vector2(90, 47);
        handle.anchoredPosition = new Vector2(-6, 0);
        tab.Surface.edgeColor = Mint;
    }

    private void BuildDrawer() {
        panel = RectChild(root, "ReviewDrawer");
        panel.pivot = new Vector2(1, .5f);
        panel.anchorMin = new Vector2(1, .328f);
        panel.anchorMax = new Vector2(1, .904f);
        Surface(panel, Deep, Edge, true, 12);
        var header = RectChild(panel, "Header");
        header.anchorMin = new Vector2(0, 1); header.anchorMax = Vector2.one;
        header.offsetMin = new Vector2(16, -60); header.offsetMax = new Vector2(-14, -8);
        var headerText = Label(header, "Title", "逐手复盘", 28, Paper);
        headerText.rectTransform.offsetMax = new Vector2(-90, 0);
        var close = MakeButton(header, "Close", "收起  ›", () => SetDrawer(false), 18);
        close.Rect.anchorMin = new Vector2(1, .1f); close.Rect.anchorMax = new Vector2(1, .9f);
        close.Rect.offsetMin = new Vector2(-82, 0); close.Rect.offsetMax = Vector2.zero;

        var viewport = RectChild(panel, "Viewport");
        Stretch(viewport);
        viewport.offsetMin = new Vector2(14, 66); viewport.offsetMax = new Vector2(-14, -68);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 36;
        content = RectChild(viewport, "Content");
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
        scroll.content = content;

        displayTitle = Label(content, "Display", "牌面显示", 17, Muted);
        modes = RectChild(content, "Modes");
        buttonB = MakeButton(modes, "ModeB", "B   突出前三", () => SetMode(DisplayMode.B));
        buttonC = MakeButton(modes, "ModeC", "C   按牌查看", () => SetMode(DisplayMode.C));
        Pair(buttonB.Rect, buttonC.Rect);
        allButton = MakeButton(content, "ShowAll", "", () => {
            showAll = !showAll;
            PlayerPrefs.SetInt(AllKey, showAll ? 1 : 0);
            PlayerPrefs.Save();
            RefreshDrawer(false);
        });
        pressHint = Label(content, "PressHint", "按住手牌查看推荐度，松开收起", 17, Muted);
        phaseTitle = Label(content, "Decision", "", 20, Muted);
        phaseButton = MakeButton(content, "ChangePhase", "", () => {
            phaseIndex = (phaseIndex + 1) % atNode.Count;
            SetDecision(atNode[phaseIndex]);
        });
        comparison = RectChild(content, "Comparison");
        actualButton = MakeButton(comparison, "Actual", "", () => SelectChoice(decision.Actual), 18);
        recommendedButton = MakeButton(comparison, "Recommended", "", () => SelectChoice(decision.Recommended), 18);
        Pair(actualButton.Rect, recommendedButton.Rect);
        detailTitle = Label(content, "UkeireTitle", "", 22, Paper);
        detailSummary = Label(content, "UkeireSummary", "", 20, Mint);
        tiles = Label(content, "EffectiveTiles", "", 18, Paper);
        tiles.textWrappingMode = TextWrappingModes.Normal;
        expectation = Label(content, "Expectation", "", 16, Muted);
        choicesTitle = Label(content, "Candidates", "候选动作", 18, Muted);
        notice = Label(content, "Status", "", 19, Paper);
        notice.textWrappingMode = TextWrappingModes.Normal;
        recovery = MakeButton(content, "StatusAction", "", () => {
            if (report != null) owner.SwitchRecordPerspectiveToOriginalPlayerIndex(report.Perspective);
            else if (request == null) { status = "正在读取复盘…"; needsLoad = true; RefreshDrawer(false); }
        });
        footer = RectChild(panel, "DecisionNavigation");
        footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0);
        footer.offsetMin = new Vector2(14, 12); footer.offsetMax = new Vector2(-14, 56);
        previous = MakeButton(footer, "Previous", "‹  上个决策", () => SeekDecision(-1));
        next = MakeButton(footer, "Next", "下个决策  ›", () => SeekDecision(1));
        Pair(previous.Rect, next.Rect);
    }

    private static void Pair(RectTransform left, RectTransform right) {
        left.anchorMin = Vector2.zero; left.anchorMax = new Vector2(.5f, 1);
        left.offsetMin = Vector2.zero; left.offsetMax = new Vector2(-3, 0);
        right.anchorMin = new Vector2(.5f, 0); right.anchorMax = Vector2.one;
        right.offsetMin = new Vector2(3, 0); right.offsetMax = Vector2.zero;
    }

    private void ResizeDrawer() {
        float width = Mathf.Min(Mathf.Clamp(root.rect.width * .235f, 336, 400), root.rect.width - 22);
        if (Mathf.Abs(lastWidth - width) < .5f) return;
        lastWidth = width;
        panel.offsetMin = new Vector2(-width - 8, 0);
        panel.offsetMax = new Vector2(-8, 0);
        RefreshDrawer(false);
    }

    private void SelectChoice(string key) {
        var choice = Array.Find(decision.Choices, c => c.Key == key);
        if (choice == null) return;
        selected = choice;
        RefreshDrawer(false);
        // Bring the selected candidate's tile breakdown into view, including when picked at the bottom of a long list.
        Canvas.ForceUpdateCanvases();
        float scrollable = content.rect.height - scroll.viewport.rect.height;
        if (scrollable > 0) scroll.verticalNormalizedPosition =
            1 - Mathf.Clamp01((-detailTitle.rectTransform.offsetMax.y - 3) / scrollable);
    }

    private static void Place(RectTransform rect, ref float y, float height, float gap = 6) {
        rect.gameObject.SetActive(height > 0);
        if (height <= 0) return;
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(2, -y - height); rect.offsetMax = new Vector2(-2, -y);
        y += height + gap;
    }

    private void RefreshDrawer(bool resetScroll) {
        if (!panel || !drawerOpen) return;
        float y = 0;
        Place(displayTitle.rectTransform, ref y, 23, 4);
        Place(modes, ref y, 40);
        buttonB.Paint(mode == DisplayMode.B); buttonC.Paint(mode == DisplayMode.C);
        allButton.Text.text = "全手牌常显百分比     " + (showAll ? "开" : "关");
        allButton.Paint(mode == DisplayMode.B && showAll);
        Place(allButton.Rect, ref y, mode == DisplayMode.B ? 36 : 0, 14);
        Place(pressHint.rectTransform, ref y, mode == DisplayMode.C ? 32 : 0, 14);

        bool has = decision != null;
        bool otherSeat = report != null && seat != report.Perspective;
        phaseTitle.text = has ? decision.PhaseLabel + "  ·  节点 " + node : "当前决策";
        Place(phaseTitle.rectTransform, ref y, 27);
        bool multi = has && atNode.Count > 1;
        if (multi) phaseButton.Text.text = $"决策 {phaseIndex + 1}/{atNode.Count}    {decision.PhaseLabel}    ›";
        Place(phaseButton.Rect, ref y, multi ? 38 : 0);
        if (has) {
            actualButton.Text.text = "实际选择\n" + decision.ChoiceText(decision.Actual);
            actualButton.Text.color = Gold;
            recommendedButton.Text.text = "AI 推荐\n" + decision.ChoiceText(decision.Recommended);
            recommendedButton.Text.color = Mint;
        }
        Place(comparison, ref y, has ? 68 : 0, 13);
        notice.text = has ? "" : report == null ? status : otherSeat ? "当前视角暂无分析" : "本步没有该玩家的决策";
        Place(notice.rectTransform, ref y, has ? 0 : 55);
        bool canRetry = report == null && status == "读取分析失败";
        recovery.Text.text = otherSeat ? "查看开局" + "东南西北"[report.Perspective] + "家的分析" : "重新读取";
        Place(recovery.Rect, ref y, otherSeat || canRetry ? 40 : 0);

        RefreshUkeire(ref y);
        Place(choicesTitle.rectTransform, ref y, has ? 24 : 0);
        int count = has ? decision.Choices.Length : 0;
        for (int i = choiceButtons.Count; i < count; i++) {
            int index = i;
            choiceButtons.Add(MakeButton(content, "Candidate" + i, "", () => SelectChoice(decision.Choices[index].Key)));
        }
        for (int i = 0; i < choiceButtons.Count; i++) {
            var button = choiceButtons[i];
            if (i < count) {
                var c = decision.Choices[i];
                button.Text.text = RiichiReviewReport.ActionLabel(c.Key) + "     " + c.Exact;
                button.Paint(c == selected);
            }
            Place(button.Rect, ref y, i < count ? 36 : 0, 4);
        }
        content.sizeDelta = new Vector2(0, y);
        bool canSeek = report != null && !otherSeat;
        footer.gameObject.SetActive(canSeek);
        scroll.viewport.offsetMin = new Vector2(14, canSeek ? 66 : 12);
        if (canSeek) {
            var first = report.Decisions[0]; var last = report.Decisions[report.Decisions.Count - 1];
            previous.Button.interactable = round > first.Round || (round == first.Round && node > first.Node);
            next.Button.interactable = round < last.Round || (round == last.Round && node < last.Node);
        }
        if (resetScroll) { scroll.StopMovement(); content.anchoredPosition = Vector2.zero; }
    }

    private void RefreshUkeire(ref float y) {
        var row = selected?.Ukeire;
        detailTitle.text = selected == null ? "" : "进张  ·  " + RiichiReviewReport.ActionLabel(selected.Key);
        Place(detailTitle.rectTransform, ref y, selected != null ? 28 : 0, 2);
        detailSummary.text = selected == null ? "" : selected.Key == "agari" ? "和牌"
            : row == null ? "此候选暂无进张数据" : $"{row["shanten"]} 向听    {row["effective_count"]} 枚进张";
        Place(detailSummary.rectTransform, ref y, selected != null ? 28 : 0, 3);
        var effective = row?["effective_tiles"] as JArray;
        tiles.text = effective == null ? "" : string.Join("   ", effective.Select(t =>
            RiichiReviewReport.TileLabel((string)t["tile"]) + "×" + t["count"]));
        float height = tiles.text.Length == 0 ? 0 : tiles.GetPreferredValues(tiles.text, Mathf.Max(40, lastWidth - 36), 1000).y + 6;
        Place(tiles.rectTransform, ref y, height);
        double secondary = (double?)row?["second_ukeire_ev"] ?? -1, ev = (double?)row?["ev"] ?? -1;
        var numbers = new List<string>(2);
        if (secondary >= 0) numbers.Add("二次进张期望 " + secondary.ToString("0.##", CultureInfo.InvariantCulture));
        if (ev >= 0) numbers.Add("EV " + ev.ToString("0.##", CultureInfo.InvariantCulture));
        expectation.text = string.Join("    ", numbers);
        Place(expectation.rectTransform, ref y, numbers.Count > 0 ? 24 : 0, 12);
    }
}
