#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

/// <summary>所有规则共用的按钮顺序回归；从 Editor bridge 显式调用 Run。</summary>
public static class ActionButtonOrderValidation {
    public static readonly Dictionary<string, string[]> RuleActions = new Dictionary<string, string[]> {
        { "guobiao", new[] { "force_pass", "hu_first", "gang", "peng", "chi_left", "pass" } },
        { "qingque", new[] { "hu_first", "gang", "peng", "chi_right", "force_pass", "pass" } },
        { "sichuan", new[] { "hu", "peng", "gang", "force_pass", "pass" } },
        { "changsha", new[] { "hu_first", "gang", "peng", "chi_mid", "pass", "force_pass" } },
        { "taiwan", new[] { "hu_first", "peng", "gang", "chi_left", "pass" } },
        { "zhongyong", new[] { "hu_self", "angang", "jiagang", "cut" } },
        { "nanque", new[] { "hu_self", "angang", "jiagang", "cut" } },
        { "jiandan", new[] { "hu_self", "angang", "jiagang", "cut" } },
        { "hongzhong", new[] { "hu_self", "angang", "jiagang", "cut" } },
        { "guangdong", new[] { "hu_first", "gang", "peng", "chi_left", "pass" } },
        { "changchun", new[] { "hu_first", "gang", "peng", "chi_right", "pass" } },
        { "hangzhou", new[] { "peng", "gang", "chi_left", "pass" } },
        { "classical", new[] { "hu_first", "gang", "peng", "chi_mid", "pass" } },
        { "riichi", new[] { "hu_self", "jiuzhongjiupai", "riichi_cut", "buhua", "angang", "jiagang", "pass" } },
        { "guizhou", new[] { "guizhou_ready_cancel", "hu_self", "baoting_initial", "angang" } },
        { "hongkong", new[] { "riichi_cancel", "hu_self", "riichi_cut", "angang" } },
        { "shanxi", new[] { "hu_self", "buhua", "angang", "jiagang", "pass" } },
        { "shanghai", new[] { "hu_self", "angang", "jiagang", "pass" } },
        { "wenzhou", new[] { "hu_self", "angang", "jiagang", "riichi_cut", "pass" } },
        { "yixing", new[] { "hu_self", "buhua", "angang", "jiagang", "pass" } },
        { "hongque", new[] { HongqueActionWords.ForcePass, HongqueActionWords.Pass,
            HongqueActionWords.GroupPrefix + "win", HongqueActionWords.GroupPrefix + "rainbow",
            HongqueActionWords.GroupPrefix + "triplet", HongqueActionWords.GroupPrefix + "sequence",
            HongqueActionWords.KongPrefix + "candidate" } },
    };

    public static string Run() {
        RegisterRuleWords();
        int checks = 0;
        void Check(bool value, string label) {
            if (!value) throw new InvalidOperationException(label);
            checks++;
        }
        var expectedRanks = new Dictionary<string, int> {
            { "chi_left", 10 }, { "chi_mid", 10 }, { "chi_right", 10 }, { "peng", 20 },
            { "gang", 30 }, { "angang", 30 }, { "jiagang", 30 }, { "buzhang", 30 },
            { "riichi_cut", 40 }, { "jiuzhongjiupai", 40 }, { "buhua", 40 },
            { "hu", 50 }, { "hu_first", 50 }, { "hu_second", 50 }, { "hu_third", 50 },
            { "hu_self", 50 }, { "hu_flower", 50 }, { "initial_hu", 50 },
            { "pass", 60 }, { "riichi_cut_cancel", 60 }, { "guizhou_ready_cancel", 60 }, { "riichi_cancel", 60 },
            { "force_pass", 70 }, { HongqueActionWords.Pass, 60 }, { HongqueActionWords.ForcePass, 70 },
            { HongqueActionWords.GroupPrefix + "sequence", 10 }, { HongqueActionWords.GroupPrefix + "triplet", 20 },
            { HongqueActionWords.KongPrefix + "candidate", 30 }, { HongqueActionWords.GroupPrefix + "rainbow", 40 },
            { HongqueActionWords.Win, 50 }, { HongqueActionWords.GroupPrefix + "win", 50 },
        };
        foreach (var item in expectedRanks) Check(ActionWords.DisplayPriorityOf(item.Key) == item.Value, "priority: " + item.Key);
        foreach (var manifest in RuleRegistry.All.Where(manifest => manifest.RuleId != "free"))
            Check(RuleActions.ContainsKey(manifest.RuleId), "registered rule has a regression row: " + manifest.RuleId);
        foreach (var row in RuleActions) {
            var input = row.Value.ToList();
            var ordered = ActionWords.OrderForDisplay(input);
            Check(input.SequenceEqual(row.Value), "source unchanged: " + row.Key);
            Check(!ReferenceEquals(input, ordered), "display owns a copy: " + row.Key);
            Check(ordered.Select(ActionWords.DisplayPriorityOf).SequenceEqual(
                ordered.Select(ActionWords.DisplayPriorityOf).OrderBy(rank => rank)), "ascending: " + row.Key);
            Check(ActionWords.OrderForDisplay(ordered).SequenceEqual(ordered), "idempotent: " + row.Key);
        }
        var canonical = new[] { "chi_left", "peng", "gang", "riichi_cut", "hu_self", "pass", "force_pass" };
        int permutations = 0;
        foreach (var input in Permutations((string[])canonical.Clone(), 0)) {
            Check(ActionWords.OrderForDisplay(input).SequenceEqual(canonical), "permuted seven-action row");
            permutations++;
        }
        var grouped = new[] { "hu_third", "chi_right", "angang", "hu_first", "chi_left", "angang", "jiagang", "pass" };
        Check(ActionWords.OrderForDisplay(grouped).SequenceEqual(new[] {
            "chi_right", "chi_left", "angang", "angang", "jiagang", "hu_third", "hu_first", "pass"
        }), "same-rank candidates and duplicate kong targets remain stable");
        foreach (string word in new[] { "angang", "jiagang", "buzhang", "hu_flower", "initial_hu" })
            Check(ActionWords.KindOf(word) == ActionWordKind.Other, "display priority preserves automatic-action semantics: " + word);
        Check(ActionWords.KindOf("force_pass") == ActionWordKind.Pass, "give up remains a decline");
        Check(ActionWords.LabelOf(HongqueActionWords.ForcePass) == "放弃", "Hongque give-up caption");
        Check(ActionWords.OrderForDisplay(null).Count == 0, "null row");
        Check(ActionWords.OrderForDisplay(Array.Empty<string>()).Count == 0, "empty row");
        return JsonConvert.SerializeObject(new { passed = checks, rule_families = RuleActions.Count, permutations });
    }

    private static void RegisterRuleWords() {
        HongqueActionWords.RegisterAll();
        foreach (Type type in typeof(ActionWords).Assembly.GetTypes().Where(type => type.Name.EndsWith("RuleBootstrap"))) {
            MethodInfo register = type.GetMethod("Register", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (register != null && register.GetParameters().Length == 0) register.Invoke(null, null);
        }
    }

    /// <summary>在空闲牌桌的 Play 模式验证真实按钮层级和布局方向；结束后恢复空闲状态。</summary>
    public static string RunCanvas() {
        if (!Application.isPlaying || GameCanvas.Instance == null || RuleRegistry.ActiveGameState != null)
            throw new InvalidOperationException("RunCanvas requires Play mode with an idle GameCanvas");
        RegisterRuleWords();
        GameCanvas canvas = GameCanvas.Instance;
        NormalGameStateManager manager = NormalGameStateManager.Instance;
        string savedRule = manager.roomRule, savedSubRule = manager.subRule;
        var rows = new List<object>();
        var savedActive = new Dictionary<GameObject, bool>();
        void EnableHost(Transform host) {
            for (Transform parent = host; parent != null; parent = parent.parent) {
                if (!savedActive.ContainsKey(parent.gameObject)) savedActive[parent.gameObject] = parent.gameObject.activeSelf;
                parent.gameObject.SetActive(true);
            }
        }
        try {
            foreach (var row in RuleActions) {
                RuleRegistry.SetCurrent(row.Key);
                manager.roomRule = row.Key;
                manager.subRule = RuleRegistry.Current?.DefaultSubRule;
                Transform host = canvas.ActionButtonHost;
                EnableHost(host);
                var source = row.Value.ToList();
                canvas.SetActionButton(source);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)host);
                var buttons = Enumerable.Range(0, host.childCount).Select(index => host.GetChild(index))
                    .Where(child => child.gameObject.activeSelf).Select(child => child.GetComponent<ActionButton>())
                    .Where(button => button != null).ToArray();
                var expected = ActionWords.OrderForDisplay(source).Where(word => word != "cut"
                    && ActionWords.KindOf(word) != ActionWordKind.Persistent).ToArray();
                if (!buttons.Select(button => button.actionTypeList[0]).SequenceEqual(expected))
                    throw new InvalidOperationException("Generated buttons differ from expected row: " + row.Key);
                if (!source.SequenceEqual(row.Value)) throw new InvalidOperationException("Canvas mutated source: " + row.Key);
                var layout = host.GetComponent<HorizontalLayoutGroup>();
                if (layout != null && layout.reverseArrangement) throw new InvalidOperationException("Reversed layout: " + row.Key);
                var positions = buttons.Select(button => button.transform.position.x).ToArray();
                if (!positions.SequenceEqual(positions.OrderBy(value => value)))
                    throw new InvalidOperationException("Buttons are visually reversed: " + row.Key);
                rows.Add(new { rule = row.Key, actions = expected, labels = buttons.Select(button => button.TextObject.text).ToArray(), positions });
            }
            RuleRegistry.SetCurrent("free");
            var freeActions = new List<string> { FreeActionWords.Stand, FreeActionWords.Draw, FreeActionWords.Push };
            canvas.SetActionButton(freeActions);
            var freeButtons = Enumerable.Range(0, canvas.ActionButtonHost.childCount)
                .Select(index => canvas.ActionButtonHost.GetChild(index)).Where(child => child.gameObject.activeSelf)
                .Select(child => child.GetComponent<ActionButton>()).Where(button => button != null).ToArray();
            if (!freeButtons.Select(button => button.actionTypeList[0]).SequenceEqual(freeActions))
                throw new InvalidOperationException("Free-mode toolbar order changed");
            return JsonConvert.SerializeObject(new { passed = rows.Count + 1, rows, free_toolbar_preserved = true });
        } finally {
            RuleRegistry.ClearCurrent();
            manager.roomRule = savedRule;
            manager.subRule = savedSubRule;
            canvas.ClearActionButton();
            foreach (var saved in savedActive) saved.Key.SetActive(saved.Value);
        }
    }

    private static IEnumerable<string[]> Permutations(string[] words, int start) {
        if (start == words.Length) { yield return (string[])words.Clone(); yield break; }
        for (int index = start; index < words.Length; index++) {
            string original = words[start]; words[start] = words[index]; words[index] = original;
            foreach (var result in Permutations(words, start + 1)) yield return result;
            original = words[start]; words[start] = words[index]; words[index] = original;
        }
    }
}
#endif
