#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Geometry contracts for shared choices and paired waiting-tile hints.</summary>
public static class UpwardPopupLayoutValidation {
    public static object RunHints() => Run(true);
    public static object RunActions() => Run(false);

    public static object RunBalancedRows() {
        var checks = new List<string>();
        var root = new GameObject("BalancedPopupValidation", typeof(RectTransform), typeof(Canvas));
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        try {
            foreach (float viewport in new[] { 720f, 960f, 1920f }) foreach (bool paired in new[] { true, false }) {
                canvas.scaleFactor = Screen.width / viewport;
                foreach (int count in new[] { 7, 13, 25, 35, 126 }) {
                    var rect = Child(root.transform, "Popup", Vector2.zero);
                    rect.anchoredPosition = new Vector2(0, -100);
                    var layout = rect.gameObject.AddComponent<UpwardPopupLayout>();
                    var tiles = paired ? Child(rect, "Tiles", Vector2.zero) : rect;
                    var hints = paired ? Child(rect, "Hints", Vector2.zero) : null;
                    for (int i = 0; i < count; i++) {
                        var cell = Child(tiles, "Cell" + i, new Vector2(paired ? 84 : 180, 138));
                        if (paired) Child(hints, "Hint" + i, new Vector2(84, 77));
                        else cell.gameObject.AddComponent<ActionBlock>();
                    }
                    Canvas.ForceUpdateCanvases();
                    if (paired) layout.SetHintRows(tiles, hints); else layout.Refresh();
                    var rowCounts = new List<int>();
                    for (int page = 0; page < layout.PageCount; page++) {
                        layout.SetPage(page);
                        rowCounts.AddRange(tiles.Cast<Transform>().OfType<RectTransform>()
                            .Where(t => t.gameObject.activeSelf && (paired || t.GetComponent<ActionBlock>() != null))
                            .GroupBy(t => t.anchoredPosition.y).Select(row => row.Count()));
                    }
                    if (rowCounts.Sum() != count || rowCounts.Max() - rowCounts.Min() > 1)
                        throw new InvalidOperationException($"Unbalanced {paired}/{viewport}/{count}: {string.Join(",", rowCounts)}");
                    checks.Add($"balanced {paired}/{viewport}/{count}: {string.Join(",", rowCounts)}");
                    UnityEngine.Object.DestroyImmediate(rect.gameObject);
                }
            }
            return new { count = checks.Count, checks };
        } finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static object Run(bool paired) {
        var checks = new List<string>();
        void Check(bool value, string label) {
            if (!value) throw new InvalidOperationException(label);
            checks.Add(label);
        }
        var root = new GameObject("PopupLayoutValidation", typeof(RectTransform), typeof(Canvas));
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        try {
            foreach (int count in new[] { 0, 1, 4, 12, 35, 126 }) {
                var popup = new GameObject("Popup", typeof(RectTransform)); popup.transform.SetParent(root.transform, false);
                var rect = (RectTransform)popup.transform; rect.anchoredPosition = new Vector2(225, -100);
                var layout = popup.AddComponent<UpwardPopupLayout>();
                RectTransform tiles = rect, hints = null;
                if (paired) {
                    tiles = Child(rect, "Tiles", Vector2.zero); hints = Child(rect, "Hints", Vector2.zero);
                }
                for (int i = 0; i < count; i++) {
                    var cell = Child(tiles, "Tile_" + i, new Vector2(paired ? 84 : 292 + (i % 3) * 88, 138));
                    if (paired) Child(hints, "Hint_" + i, new Vector2(84, i % 3 == 0 ? 121 : 77));
                    else { var block = cell.gameObject.AddComponent<ActionBlock>(); block.actionType = "choice_" + i; block.targetTile = i; block.chiComboIndex = i; }
                }
                Canvas.ForceUpdateCanvases();
                if (paired) layout.SetHintRows(tiles, hints); else layout.Refresh();
                var seen = new HashSet<string>();
                var rowWidths = new List<float>();
                float baseline = rect.anchoredPosition.y;
                for (int page = 0; page < layout.PageCount; page++) {
                    layout.SetPage(page); Canvas.ForceUpdateCanvases();
                    Check(Mathf.Abs(rect.anchoredPosition.y - baseline) < .1f, "paging preserves bottom " + count + "/" + page);
                    var visible = tiles.Cast<Transform>().OfType<RectTransform>()
                        .Where(t => t.gameObject.activeSelf && (paired || t.GetComponent<ActionBlock>() != null)).ToArray();
                    rowWidths.AddRange(visible.GroupBy(t => t.anchoredPosition.y)
                        .Select(group => group.Max(t => t.anchoredPosition.x + t.rect.width * t.localScale.x) - group.Min(t => t.anchoredPosition.x)));
                    foreach (var tile in visible) {
                        Check(seen.Add(tile.name), "candidate appears once " + count + "/" + tile.name);
                        var corners = new Vector3[4]; tile.GetWorldCorners(corners);
                        Check(corners.All(p => p.x >= -.1f && p.x <= Screen.width + .1f && p.y >= -.1f && p.y <= Screen.height + .1f), "visible candidate stays on screen " + count + "/" + tile.name);
                        if (paired) {
                            var hint = (RectTransform)hints.GetChild(tile.GetSiblingIndex());
                            Check(hint.gameObject.activeSelf && Mathf.Abs(hint.anchoredPosition.x - tile.anchoredPosition.x) < .1f, "tile and hint remain paired " + tile.name);
                            var hintCorners = new Vector3[4]; hint.GetWorldCorners(hintCorners);
                            Check(hintCorners[1].y < corners[0].y, "hint stays below its own tile " + tile.name);
                        } else {
                            var block = tile.GetComponent<ActionBlock>();
                            Check(block.actionType == "choice_" + block.targetTile && block.chiComboIndex == block.targetTile, "choice identity is preserved " + tile.name);
                        }
                    }
                    for (int a = 0; a < visible.Length; a++) for (int b = a + 1; b < visible.Length; b++) {
                        var ac = new Vector3[4]; var bc = new Vector3[4]; visible[a].GetWorldCorners(ac); visible[b].GetWorldCorners(bc);
                        Check(!Rect.MinMaxRect(ac[0].x, ac[0].y, ac[2].x, ac[2].y).Overlaps(Rect.MinMaxRect(bc[0].x, bc[0].y, bc[2].x, bc[2].y)), "choices do not overlap " + count + "/" + a + "/" + b);
                    }
                }
                Check(seen.Count == count, "every choice remains reachable " + count);
                if (!paired && count == 4 && Screen.width >= 1280)
                    Check(rowWidths.Max() - rowWidths.Min() <= 484, "unequal-width action cards do not leave an orphan row");
                layout.SetPage(999); Check(layout.PageIndex <= Mathf.Max(0, layout.PageCount - 1), "page index clamped " + count);
                UnityEngine.Object.DestroyImmediate(popup);
            }
            return new { count = checks.Count, checks };
        } finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static RectTransform Child(Transform parent, string name, Vector2 size) {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.sizeDelta = size; return rect;
    }
}
#endif
