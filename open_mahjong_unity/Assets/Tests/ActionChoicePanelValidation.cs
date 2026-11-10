#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public static class ActionChoicePanelValidation {
    public static object Run() {
        var source = Resources.FindObjectsOfTypeAll<GameCanvas>().First(item => item.gameObject.scene.IsValid() && item.ChoicePanel != null);
        var cardPrefab = (GameObject)typeof(GameCanvas).GetField("StaticCardPrefab", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
        int[] tiles = new[] { "AX1", "AY1", "BX1", "BY1", "CX1", "CY1", "DX1", "DY1", "EX1", "EY1", "FX1", "FY1", "GX1", "GY1" }
            .Select(HongqueTileVisual.FromCode).ToArray();
        var choices = Enumerable.Range(0, 443).Select(i => new ActionCandidate("choice-" + i, tiles,
            caption: "17番  153分", tileGroups: new[] { tiles.Take(7).ToArray(), tiles.Skip(7).ToArray() },
            details: "二数  12番 · 二金  3番 · 清刻×2  2番", textContext: "hongque")).ToArray();
        var checks = new List<string>();
        int maxRows = 0, maxCards = 0;
        var root = new GameObject("ActionChoicePanelValidation", typeof(RectTransform), typeof(Canvas));
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        try {
            foreach (float width in new[] { 720f, 1280f, 1920f }) {
                canvas.scaleFactor = Screen.width / width;
                var panel = UnityEngine.Object.Instantiate(source.ChoicePanel, root.transform);
                panel.gameObject.SetActive(false);
                var panelRect = (RectTransform)panel.transform;
                panelRect.anchorMin = panelRect.anchorMax = new Vector2(.5f, .5f);
                panelRect.anchoredPosition = new Vector2(0, -100);
                Canvas.ForceUpdateCanvases();
                var buttonObject = new GameObject("ActualChoiceButton", typeof(RectTransform));
                var anchor = (RectTransform)buttonObject.transform;
                anchor.SetParent(root.transform, false);
                anchor.anchorMin = anchor.anchorMax = anchor.pivot = new Vector2(.5f, .5f);
                anchor.sizeDelta = new Vector2(112, 48);
                Vector2 lowButton = new Vector2(0, ((RectTransform)root.transform).rect.yMin + 40);
                foreach (int count in new[] { 1, 3, 4, 443 }) {
                    anchor.anchoredPosition = lowButton;
                    panel.Show(choices.Take(count).ToArray(), cardPrefab, anchor);
                    var scroll = panel.GetComponent<UnityEngine.UI.ScrollRect>();
                    CheckPlacement(panelRect, anchor, canvas);
                    if (panel.ChoiceCount != count || panel.HasScrollbar != (scroll.content.rect.height > scroll.viewport.rect.height + .01f))
                        throw new InvalidOperationException("Clipped choices need a scrollbar regardless of their count");
                    if (panel.VisibleRows.Length == 0 || panel.VisibleRows.Length > Math.Min(3, count))
                        throw new InvalidOperationException("The initial viewport must show at most three rows, allowing a clipped row");
                    foreach (float position in new[] { 1f, .517f, 0f }) {
                        panel.ScrollTo(position);
                        var shown = panel.VisibleRows;
                        if (shown.Length > 4 || panel.CreatedRowCount > 4 || panel.CreatedCardCount > 56)
                            throw new InvalidOperationException("Scrolling may instantiate at most four rows / 56 real cards");
                        maxRows = Math.Max(maxRows, panel.CreatedRowCount); maxCards = Math.Max(maxCards, panel.CreatedCardCount);
                        for (int i = 0; i < shown.Length; i++) {
                            if (shown[i].ActionType != "choice-" + shown[i].Index)
                                throw new InvalidOperationException("Recycled row lost its candidate identity");
                            if (i > 0 && (shown[i].Index != shown[i - 1].Index + 1
                                || ((RectTransform)shown[i].transform).anchoredPosition.y >= ((RectTransform)shown[i - 1].transform).anchoredPosition.y))
                                throw new InvalidOperationException("Visible choices must retain top-to-bottom server order");
                        }
                        if (position == 0 && shown.Last().Index != count - 1)
                            throw new InvalidOperationException("The final low-score candidate must remain reachable");
                        var corners = new Vector3[4]; panelRect.GetWorldCorners(corners);
                        if (corners.Any(point => point.x < -.1f || point.y < -.1f || point.x > Screen.width + .1f || point.y > Screen.height + .1f))
                            throw new InvalidOperationException("Choice panel exceeds viewport bounds");
                    }
                    if (panel.HasScrollbar) {
                        var bar = scroll.verticalScrollbar;
                        Canvas.ForceUpdateCanvases();
                        var thumb = bar.handleRect.GetComponent<UnityEngine.UI.Image>();
                        if (!bar.gameObject.activeInHierarchy || !thumb.isActiveAndEnabled || thumb.color.a < .9f
                            || bar.handleRect.rect.width < 14 || bar.handleRect.rect.height <= 0)
                            throw new InvalidOperationException("The scrollbar needs a clearly visible, nonzero thumb at least 14 logical pixels wide");
                        bar.value = 1; Canvas.ForceUpdateCanvases();
                        float topThumb = bar.handleRect.position.y;
                        bar.value = 0; Canvas.ForceUpdateCanvases();
                        if (bar.handleRect.position.y >= topThumb - .1f)
                            throw new InvalidOperationException("The visible scrollbar thumb must move between both ends");
                        panel.ScrollTo(1);
                        var pointer = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -1) };
                        scroll.OnScroll(pointer); panel.RefreshLayout();
                        if (panel.ScrollPosition >= 1) throw new InvalidOperationException("Mouse wheel must move the real ScrollRect");
                        panel.ScrollTo(1);
                        var corners = new Vector3[4]; scroll.viewport.GetWorldCorners(corners);
                        pointer.button = PointerEventData.InputButton.Left;
                        pointer.position = (corners[0] + corners[2]) * .5f;
                        scroll.OnInitializePotentialDrag(pointer); scroll.OnBeginDrag(pointer);
                        pointer.position += new Vector2(0, 100); pointer.delta = new Vector2(0, 100);
                        scroll.OnDrag(pointer); scroll.OnEndDrag(pointer); panel.RefreshLayout();
                        if (panel.ScrollPosition >= 1) throw new InvalidOperationException("Pointer drag must move the real ScrollRect");
                        panel.RevealChoice(count - 1);
                        if (!panel.VisibleRows.Any(row => row.Index == count - 1))
                            throw new InvalidOperationException("Explicit candidate reveal must reach the final row");
                    }
                    foreach (ActionChoicePanel.Palette palette in Enum.GetValues(typeof(ActionChoicePanel.Palette))) {
                        panel.SetPalette(palette);
                        foreach (var row in panel.VisibleRows) {
                            row.Summary.GetComponent<LocalizedPlatformLabel>()?.Refresh();
                            row.Details.GetComponent<LocalizedPlatformLabel>()?.Refresh();
                            if (row.Summary.fontStyle != FontStyles.Normal || row.Details.fontStyle != FontStyles.Normal
                                || row.Summary.fontWeight != FontWeight.Regular || row.Details.fontWeight != FontWeight.Regular)
                                throw new InvalidOperationException("Palette/localization refresh must retain regular Chinese weight");
                        }
                    }
                    // Move the same real anchor upward: height clips rows rather than shrinking cards.
                    float scale = panelRect.localScale.x;
                    float oneRow = ((RectTransform)panel.VisibleRows[0].transform).rect.height;
                    float room = (oneRow * (count == 1 ? .65f : 1.5f) + 28) * scale;
                    float safeTop = ((RectTransform)root.transform).rect.yMax - 32;
                    anchor.anchoredPosition = new Vector2(0, safeTop - room - 16 - anchor.rect.height * .5f);
                    panel.SendMessage("LateUpdate");
                    CheckPlacement(panelRect, anchor, canvas);
                    if (!Mathf.Approximately(panelRect.localScale.x, scale) || !panel.HasScrollbar)
                        throw new InvalidOperationException("Short space must preserve scale and enable scrolling, even for one or three choices");
                    panel.ScrollTo(0);
                    var lastRow = panel.VisibleRows.Last();
                    if (lastRow.Index != count - 1) throw new InvalidOperationException("Clipped viewport lost the last candidate");
                    var rowCorners = new Vector3[4]; var viewCorners = new Vector3[4];
                    ((RectTransform)lastRow.transform).GetWorldCorners(rowCorners); scroll.viewport.GetWorldCorners(viewCorners);
                    if (Mathf.Abs(rowCorners[0].y - viewCorners[0].y) > .2f
                        || count > 1 && rowCorners[1].y > viewCorners[1].y + .2f)
                        throw new InvalidOperationException("The final row must reach the viewport bottom and fit when space permits");
                    checks.Add(width + "px / " + count + " choices: actual button gap, clipped height and last row");
                    panel.Hide();
                    if (panel.gameObject.activeSelf || panel.VisibleRowCount != 0)
                        throw new InvalidOperationException("Closing must hide all actionable choices");
                    checks.Add(width + "px / " + count + " choices: bounds, identity, scroll inputs, pool, palettes");
                }
                // A different initiating button must replace the old anchor, and resizing must follow it.
                var secondObject = new GameObject("SecondChoiceButton", typeof(RectTransform));
                var second = (RectTransform)secondObject.transform;
                second.SetParent(root.transform, false); second.sizeDelta = anchor.sizeDelta;
                var smallTiles = tiles.Take(3).ToArray();
                var small = new[] { new ActionCandidate("small", smallTiles, caption: "17番  153分", tileGroups: new[] { smallTiles }) };
                anchor.anchoredPosition = new Vector2(-width * .1f, lowButton.y);
                panel.Show(small, cardPrefab, anchor);
                float firstX = panelRect.position.x;
                second.anchoredPosition = new Vector2(width * .1f, lowButton.y + 20);
                panel.Show(small, cardPrefab, second);
                CheckPlacement(panelRect, second, canvas);
                if (panelRect.position.x <= firstX) throw new InvalidOperationException("Panel did not follow the different initiating button");
                canvas.scaleFactor *= .9f; Canvas.ForceUpdateCanvases(); panel.SendMessage("LateUpdate");
                CheckPlacement(panelRect, second, canvas);
                checks.Add(width + "px: new initiating button and resized viewport");
                UnityEngine.Object.DestroyImmediate(secondObject);
                UnityEngine.Object.DestroyImmediate(buttonObject);
                UnityEngine.Object.DestroyImmediate(panel.gameObject);
            }
            return new { count = checks.Count, maxRows, maxCards, eagerCardCount = 6202, checks };
        } finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void CheckPlacement(RectTransform panel, RectTransform anchor, Canvas canvas) {
        var panelCorners = new Vector3[4]; var buttonCorners = new Vector3[4];
        panel.GetWorldCorners(panelCorners); anchor.GetWorldCorners(buttonCorners);
        float gap = panelCorners[0].y - buttonCorners[1].y;
        if (Mathf.Abs(gap - 16 * canvas.scaleFactor) > .2f)
            throw new InvalidOperationException("Panel bottom must remain 16 logical pixels above the actual button");
        if (panelCorners[1].y > Screen.height - 32 * canvas.scaleFactor + .2f)
            throw new InvalidOperationException("Panel must retain its top safe margin without vertical downscaling");
    }
}
#endif
