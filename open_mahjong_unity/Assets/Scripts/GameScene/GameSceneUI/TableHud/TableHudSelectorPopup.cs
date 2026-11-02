using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Anchored, bounded selector over the real round/node list.</summary>
[RequireComponent(typeof(ScrollRect))]
public sealed class TableHudSelectorPopup : MonoBehaviour {
    public bool roundSelector;
    public Button trigger;
    public TMP_Text heading;
    public TMP_Text emptyLabel;
    private ScrollRect scroll;
    private int lastCount = -1, lastNode = -1, lastRound = -1;
    private bool needsReveal;
    private readonly Vector3[] corners = new Vector3[4];
    private ScrollRect Scroll => scroll ? scroll : scroll = GetComponent<ScrollRect>();
    private void OnEnable() {
        needsReveal = true;
        GetComponentInParent<TableHudPopupGroup>()?.Opened(this);
        Refresh();
    }
    private void OnDisable() => GetComponentInParent<TableHudPopupGroup>()?.Closed();
    private void LateUpdate() {
        var manager = GameRecordManager.Instance;
        if (Scroll.content.childCount != lastCount) { needsReveal = true; Refresh(); }
        else if (manager && (manager.currentNode != lastNode || manager.currentRoundIndex != lastRound)) RefreshPalette();
        if (needsReveal) { needsReveal = false; RevealSelection(); }
    }
    public void Refresh() {
        if (!Scroll.content) return;
        lastCount = Scroll.content.childCount;
        float rowHeight = roundSelector ? 52 : 44;
        float total = lastCount > 0 ? lastCount * rowHeight + (lastCount - 1) * 4 + 16 : 64;
        float height = Mathf.Min(roundSelector ? 400 : 360, 40 + total);
        ((RectTransform)transform).sizeDelta = new Vector2(roundSelector ? 276 : 248, height);
        if (heading) heading.text = roundSelector ? "选择小局" : "选择巡目";
        if (emptyLabel) { emptyLabel.gameObject.SetActive(lastCount == 0); emptyLabel.text = roundSelector ? "暂无可选小局" : "暂无可选巡目"; }
        if (Scroll.verticalScrollbar) Scroll.verticalScrollbar.gameObject.SetActive(total > height - 40);
        LayoutRebuilder.ForceRebuildLayoutImmediate(Scroll.content);
        RefreshPalette(); Reposition();
    }
    public void RefreshPalette() {
        var theme = GetComponentInParent<TableHudTheme>();
        var palette = TableHudTheme.GetPalette(theme ? theme.CurrentStyle : TableHudTheme.Style.MistBlue);
        if (heading) { heading.color = palette.text; TableHudTypography.Apply(heading); }
        if (emptyLabel) { emptyLabel.color = palette.muted; TableHudTypography.Apply(emptyLabel); }
        if (Scroll.verticalScrollbar && Scroll.verticalScrollbar.targetGraphic) Scroll.verticalScrollbar.targetGraphic.color = palette.accent;
        var manager = GameRecordManager.Instance;
        if (manager) { lastRound = manager.currentRoundIndex; lastNode = manager.currentNode; }
        int selected = SelectedIndex();
        for (int i = 0; i < Scroll.content.childCount; i++) {
            var row = Scroll.content.GetChild(i);
            var background = row.GetComponent<Image>();
            if (background) background.color = i == selected ? Color.Lerp(palette.panel, palette.accent, .19f) : palette.button;
            foreach (var label in row.GetComponentsInChildren<TMP_Text>(true)) { label.color = palette.text; TableHudTypography.Apply(label); }
            var marker = row.Find("CurrentMarker")?.GetComponent<Image>();
            if (marker) { marker.color = palette.accent; marker.gameObject.SetActive(i == selected); }
            var button = row.GetComponent<Button>();
            if (button) {
                var colors = button.colors; colors.normalColor = colors.selectedColor = Color.white;
                colors.highlightedColor = new Color(.92f,.96f,1f); colors.pressedColor = new Color(.78f,.85f,.92f);
                colors.disabledColor = new Color(.65f,.65f,.65f,.55f); colors.colorMultiplier = 1; colors.fadeDuration = .08f; button.colors = colors;
            }
        }
    }
    private int SelectedIndex() {
        var manager = GameRecordManager.Instance; if (!manager) return -1;
        int selected = -1;
        for (int i = 0; i < Scroll.content.childCount; i++) {
            var child = Scroll.content.GetChild(i);
            if (roundSelector) {
                var item = child.GetComponent<RecordRoundItem>();
                if (item && item.TargetRoundIndex == manager.currentRoundIndex) return i;
            } else {
                var item = child.GetComponent<RecordNodeItem>();
                if (item && item.TargetNodeIndex <= manager.currentNode) selected = i;
            }
        }
        return selected;
    }
    private void RevealSelection() {
        Canvas.ForceUpdateCanvases();
        int selected = SelectedIndex(); if (selected < 0) return;
        float overflow = Scroll.content.rect.height - Scroll.viewport.rect.height;
        float row = roundSelector ? 56 : 48;
        if (overflow > 0) Scroll.verticalNormalizedPosition = 1 - Mathf.Clamp01((selected * row + row * .5f - Scroll.viewport.rect.height * .5f) / overflow);
        else Scroll.verticalNormalizedPosition = 1;
        Scroll.velocity = Vector2.zero;
    }
    public void Reposition() {
        if (!trigger || transform is not RectTransform rect) return;
        var theme = GetComponentInParent<TableHudTheme>();
        if (!theme || theme.transform is not RectTransform bounds) return;
        ((RectTransform)trigger.transform).GetWorldCorners(corners);
        Vector3 top = bounds.InverseTransformPoint(corners[1]);
        Vector3 bottom = bounds.InverseTransformPoint(corners[0]);
        Vector3 right = bounds.InverseTransformPoint(corners[2]);
        float x = (top.x + right.x) * .5f;
        float y = top.y + 8;
        if (transform.parent is RectTransform controlBar) {
            controlBar.GetWorldCorners(corners);
            y = Mathf.Max(y, bounds.InverseTransformPoint(corners[1]).y + 8);
        }
        if (y + rect.rect.height > bounds.rect.yMax - 12) y = bottom.y - rect.rect.height - 8;
        x = Mathf.Clamp(x, bounds.rect.xMin + rect.rect.width * .5f + 12, bounds.rect.xMax - rect.rect.width * .5f - 12);
        y = Mathf.Clamp(y, bounds.rect.yMin + 12, bounds.rect.yMax - rect.rect.height - 12);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f,.5f); rect.pivot = new Vector2(.5f,0);
        rect.position = bounds.TransformPoint(new Vector3(x,y,0));
        if (gameObject.activeSelf) rect.SetAsLastSibling();
    }
    public void CloseAfterSelection() => GetComponentInParent<TableHudPopupGroup>()?.CloseSelectors();
}
