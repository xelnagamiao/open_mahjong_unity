#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Check rendered action buttons against rule-specific captions, including shared action words.</summary>
public static class RuleActionCaptionValidation {
    public static object RunCanvas(bool assertSuccess = true) {
        if (!Application.isPlaying || GameCanvas.Instance == null || RuleRegistry.ActiveGameState != null)
            throw new InvalidOperationException("Caption validation requires Play mode with an idle GameCanvas");
        var canvas = GameCanvas.Instance;
        var manager = NormalGameStateManager.Instance;
        string savedRule = manager.roomRule, savedSubRule = manager.subRule;
        var savedActive = new Dictionary<GameObject, bool>();
        var failures = new List<string>();
        var rows = new List<object>();
        int checks = 0;
        void Check(bool ok, string label) { checks++; if (!ok) failures.Add(label); }
        void Activate(Transform host) {
            for (var parent = host; parent != null; parent = parent.parent) {
                if (!savedActive.ContainsKey(parent.gameObject)) savedActive[parent.gameObject] = parent.gameObject.activeSelf;
                parent.gameObject.SetActive(true);
            }
        }
        void Context(string rule, string sub = null) {
            RuleRegistry.SetCurrent(rule, sub);
            manager.roomRule = rule;
            manager.subRule = sub ?? RuleRegistry.Current.DefaultSubRule;
            Activate(canvas.ActionButtonHost);
        }
        ActionButton[] Buttons() => canvas.ActionButtonHost.GetComponentsInChildren<ActionButton>(true)
            .Where(b => b.gameObject.activeSelf && b.actionTypeList.Count > 0).ToArray();
        try {
            foreach (string sub in new[] { "riichi/sanma", "riichi/standard", "riichi/langyong", "riichi/washizu", "riichi/washizu_realtime", "classical/standard" }) {
                string rule = sub.Split('/')[0];
                string expected = rule == "riichi" ? "九种九牌" : "九老峰回";
                Context(rule, sub);
                canvas.SetActionButton(new List<string> { "jiuzhongjiupai", "pass" });
                var button = Buttons().Single(b => b.actionTypeList[0] == "jiuzhongjiupai");
                Check(button.TextObject.text == expected, sub + " abort button: " + button.TextObject.text);
                Check(SpecialLiujuCaptions.Resolve("jiuzhongjiupai", rule) == expected, sub + " settlement/replay title");
                rows.Add(new { rule, sub, word = "jiuzhongjiupai", actual = button.TextObject.text, expected });
            }
            foreach (var manifest in RuleRegistry.All.Where(m => m.RuleId != "free")) {
                var row = new KeyValuePair<string, string[]>(manifest.RuleId,
                    ActionButtonOrderValidation.RuleActions.TryGetValue(manifest.RuleId, out var actions)
                        ? actions : new[] { "hu_self", "riichi_cut", "buhua", "angang", "jiagang", "peng", "gang", "chi_mid", "pass" });
                Context(row.Key);
                canvas.SetActionButton(row.Value.ToList());
                foreach (var button in Buttons()) {
                    string word = button.actionTypeList[0];
                    string expected = word == "jiuzhongjiupai" ? "九种九牌"
                        : word == "pass" ? "取消" : word == "force_pass" ? "放弃"
                        : StandardActionCaptions.Resolve(RuleRegistry.Current, word);
                    Check(button.TextObject.text == expected, row.Key + "/" + word + ": " + button.TextObject.text + " != " + expected);
                    rows.Add(new { rule = row.Key, word, actual = button.TextObject.text, expected });
                }
                var cancelObject = new GameObject("CaptionCancelProbe", typeof(RectTransform));
                cancelObject.transform.SetParent(canvas.ActionButtonHost.parent, false);
                try {
                    var cancel = (Button)typeof(GameCanvas).GetMethod("CreateReadyCancelButton", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(canvas, new object[] { cancelObject.transform });
                    string actual = cancel.GetComponentInChildren<TMPro.TMP_Text>(true).text;
                    string expected = StandardActionCaptions.Resolve(RuleRegistry.Current, "riichi_cut_cancel");
                    Check(actual == expected, row.Key + "/riichi_cut_cancel: " + actual + " != " + expected);
                    rows.Add(new { rule = row.Key, word = "riichi_cut_cancel", actual, expected });
                } finally { UnityEngine.Object.Destroy(cancelObject); }
            }
            Context("changsha");
            canvas.SetActionButton(new List<string> { "sea_bottom", "pass" });
            Check(Buttons().Single(b => b.actionTypeList[0] == "pass").TextObject.text == "不要", "sea-bottom decline caption");
            foreach (string kind in new[] { "four_wind_abort", "four_kan_abort", "four_riichi_abort", "three_ron_abort" })
                Check(SpecialLiujuCaptions.Resolve(kind, "riichi") != "流局", "specific riichi abort title: " + kind);
        } finally {
            RuleRegistry.ClearCurrent();
            manager.roomRule = savedRule;
            manager.subRule = savedSubRule;
            canvas.ClearActionButton();
            foreach (var entry in savedActive) entry.Key.SetActive(entry.Value);
        }
        if (assertSuccess && failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
        return new { checks, failures, rows };
    }
}
#endif
