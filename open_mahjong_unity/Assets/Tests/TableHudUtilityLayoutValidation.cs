#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public static class TableHudUtilityLayoutValidation {
    public static object Run() {
        var root = new GameObject("TableUtilityValidation", typeof(RectTransform));
        root.SetActive(false);
        var panel = (RectTransform)root.transform;
        panel.sizeDelta = new Vector2(1920, 1080);
        var layout = root.AddComponent<TableHudUtilityLayout>();
        layout.utility = Child(panel, "Utility");
        layout.exits = Child(panel, "Exits");
        foreach (string name in new[] { "SwitchToMeun", "OpenRuleBook", "OpenHistoryPanel" })
            Child(layout.utility, name).gameObject.AddComponent<Button>();
        var quit = Child(layout.exits, "QuitRecord").gameObject.AddComponent<Button>();
        var vote = Child(layout.exits, "VoteEnd").gameObject.AddComponent<Button>();
        int checks = 0;
        try {
            {
                root.SetActive(true);
                layout.RefreshLayout();
                for (int cycle = 0; cycle < 4; cycle++) {
                    vote.gameObject.SetActive(false);
                    quit.gameObject.SetActive(true);
                    layout.SendMessage("LateUpdate");
                    CheckGeometry(layout, panel); checks++;
                    // Teardown hides exits while the layout is disabled; LoadRecord restores them after OnEnable.
                    root.SetActive(false);
                    quit.gameObject.SetActive(false);
                    root.SetActive(true);
                    quit.gameObject.SetActive(true);
                    layout.SendMessage("LateUpdate");
                    CheckGeometry(layout, panel); checks++;
                    vote.gameObject.SetActive(true);
                    layout.SendMessage("LateUpdate");
                    CheckGeometry(layout, panel); checks++;
                }
                panel.sizeDelta = new Vector2(2560, 1080);
                layout.SendMessage("LateUpdate");
                CheckGeometry(layout, panel); checks++;
                panel.sizeDelta = new Vector2(1920, 1080);
            }
            return new { passed = true, checks };
        } finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void CheckGeometry(TableHudUtilityLayout layout, RectTransform panel) {
        var rects = new List<Rect>();
        foreach (var button in layout.utility.GetComponentsInChildren<Button>()
                     .Concat(layout.exits.GetComponentsInChildren<Button>())) {
            var corners = new Vector3[4];
            ((RectTransform)button.transform).GetWorldCorners(corners);
            var min = panel.InverseTransformPoint(corners[0]);
            var max = panel.InverseTransformPoint(corners[2]);
            var rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            if (rect.xMax > panel.rect.xMax || rect.yMax > panel.rect.yMax)
                throw new InvalidOperationException("Table button outside panel: " + button.name);
            if (rects.Any(previous => previous.Overlaps(rect)))
                throw new InvalidOperationException("Table buttons overlap after reentry: " + button.name);
            rects.Add(rect);
        }
    }

    static RectTransform Child(RectTransform parent, string name) {
        var child = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        child.SetParent(parent, false);
        return child;
    }
}
#endif
