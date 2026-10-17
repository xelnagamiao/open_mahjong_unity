#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>计分板实际局列表、原生互斥选择和牌谱/实时数据隔离回归。</summary>
public static class ScoreHistoryPresentationValidation {
    public static string Run() {
        var checks = new List<string>();
        void Check(bool condition, string label) {
            if (!condition) throw new Exception("ScoreHistory presentation: " + label);
            checks.Add(label);
        }
        GameRecord Record(string rule, string subRule, int maxRound) {
            var record = new GameRecord { gameTitle = new Dictionary<string, object> {
                ["rule"] = rule, ["sub_rule"] = subRule, ["max_round"] = maxRound,
            }};
            record.gameRound.rounds[7] = new Round { roundIndex = 7, currentRound = 1,
                seats = new List<int> {0,1,2,3}, actionTicks = new List<List<string>> {new() {"liuju"}} };
            record.gameRound.rounds[11] = new Round { roundIndex = 11, currentRound = 1,
                seats = new List<int> {1,2,3,0}, actionTicks = new List<List<string>>() };
            return record;
        }

        var hongque = Record("hongque", "hongque/v1.6", 16);
        var rows = ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(hongque, includeUnsettledRounds: true);
        Check(rows.Count == 2 && rows.Select(r => r.roundIndex).SequenceEqual(new[] {7,11}),
            "recorded round keys survive repeated labels and an unsettled tail");
        Check(rows[1].scoreChangesByOriginal == null && rows[1].snapshot == null,
            "unsettled round stays blank instead of becoming a zero-score settlement");
        Check(ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(hongque).Count == 1,
            "settlement-only consumers still omit unsettled rounds");
        Check(RoundTextDictionary.ToTotalHands("hongque/v1.6", 16) == 16, "Hongque uses hands without multiplying");
        Check(RoundTextDictionary.ToTotalHands("guobiao/sanma", 2) == 6
            && RoundTextDictionary.ToTotalHands("riichi/sanma", 2) == 6, "both sanma schedules use three hands per wind");

        // Inactive fixture: no singleton registration, scene changes, or real replay navigation.
        var root = new GameObject("ScoreHistoryValidation", typeof(RectTransform));
        root.SetActive(false);
        var panel = root.AddComponent<ScoreHistoryPanel>();
        GameObject Child(string name, Transform parent) {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child;
        }
        void Bind(string name, object value) => typeof(ScoreHistoryPanel)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(panel, value);
        var template = Child("CellTemplate", root.transform);
        template.AddComponent<TextMeshProUGUI>();
        Bind("Tmp_Text_Prefab", template);
        var roundColumn = Child("Rounds", root.transform);
        var fanColumn = Child("Fans", root.transform);
        Bind("RoundIndexContainer", roundColumn);
        Bind("MainFanContainer", fanColumn.transform);
        var names = Child("Names", root.transform);
        var scores = new List<Transform>();
        for (int i = 0; i < 4; i++) {
            Bind($"player{i}UserName", Child("Player" + i, names.transform).AddComponent<TextMeshProUGUI>());
            var delta = Child("Delta" + i, root.transform).transform;
            var total = Child("Total" + i, root.transform).transform;
            scores.Add(delta); scores.Add(total);
            Bind($"player{i}RoundScoreContainer", delta);
            Bind($"player{i}GameScoreContainer", total);
        }
        int ActiveChildren(Transform parent) => parent.Cast<Transform>().Count(child => child.gameObject.activeSelf);
        try {
            foreach (var manifest in RuleRegistry.All) {
                foreach (int scheduled in new[] {0, 4, 16, 64}) {
                    panel.InitializeScoreRecord(manifest.RuleId,
                        0, "A", 10, new List<string> {"10", ""},
                        1, "B", -10, new List<string> {"-10", ""},
                        2, "C", 0, new List<string> {"0", ""},
                        3, "D", 0, new List<string> {"0", ""},
                        new List<int> {1,1}, Array.Empty<RoundSettlementSnapshot>(), scheduled,
                        manifest.DefaultSubRule, new[] {7,11});
                    Check(ActiveChildren(roundColumn.transform) == 2 && ActiveChildren(fanColumn.transform) == 2
                        && scores.All(column => ActiveChildren(column) == 2),
                        manifest.RuleId + " replay has two actual rows regardless of max_round=" + scheduled);
                    Check(scores.All(column => column.Cast<Transform>().Last(child => child.gameObject.activeSelf)
                        .GetComponent<TMP_Text>().text == ""), manifest.RuleId + " unsettled scores stay blank");
                }
            }
            Check(ScoreHistorySettlementHelper.ResolveSettlementForRow(0, 1,
                Array.Empty<RoundSettlementSnapshot>()) == null, "explicitly empty replay history does not borrow live snapshots");
        } finally {
            UnityEngine.Object.DestroyImmediate(root);
        }

        var toggleRoot = new GameObject("ScoreHistoryToggleValidation", typeof(RectTransform), typeof(Canvas));
        try {
            var group = ScoreHistoryCellVisuals.EnsureToggleGroup(toggleRoot.transform, false);
            var first = ScoreHistoryCellVisuals.CreateToggle(Child("First", toggleRoot.transform).AddComponent<TextMeshProUGUI>(), group);
            var second = ScoreHistoryCellVisuals.CreateToggle(Child("Second", toggleRoot.transform).AddComponent<TextMeshProUGUI>(), group);
            int calls = 0;
            first.onValueChanged.AddListener(_ => calls++);
            second.onValueChanged.AddListener(_ => calls++);
            first.SetIsOnWithoutNotify(true);
            Check(first.isOn && !second.isOn && calls == 0, "external state sync selects without navigation callbacks");
            second.OnSubmit(new BaseEventData(EventSystem.current));
            Check(!first.isOn && second.isOn, "native submit switches mutually exclusive selection");
            second.OnSubmit(new BaseEventData(EventSystem.current));
            Check(second.isOn, "selected player or round cannot be deselected");
            Check(first.colors.pressedColor != first.colors.highlightedColor
                && first.colors.fadeDuration > 0f && first.toggleTransition == UnityEngine.UI.Toggle.ToggleTransition.Fade,
                "pressed color differs from hover and both native transitions are enabled");
        } finally {
            UnityEngine.Object.DestroyImmediate(toggleRoot);
        }
        return string.Join("\n", checks) + $"\nPASS: {checks.Count} checks";
    }
}
#endif
