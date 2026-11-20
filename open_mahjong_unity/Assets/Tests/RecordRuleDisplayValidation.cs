#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class RecordRuleDisplayValidation
{
    const string Folder = "Assets/Prefabs/Record/";
    static readonly string[] Cards = {
        "RecordItem (3)", "MatchRecordRows", "MatchRecordColumns", "MatchRecordSeats", "MatchRecordSpectatorStyle"
    };

    // Changing this field for the fixture avoids writing to the user's language preference.
    static readonly FieldInfo Language = typeof(ConfigManager).GetField("_languageMode", BindingFlags.NonPublic | BindingFlags.Static);

    public static void VerifyBatch()
    {
        foreach (var method in TypeCache.GetMethodsWithAttribute<RuntimeInitializeOnLoadMethodAttribute>())
            if (method.Name == "Register" && method.DeclaringType.Name.EndsWith("RuleBootstrap", StringComparison.Ordinal))
                method.Invoke(null, null);
        var result = Run();
        if (result.failures.Count > 0)
            throw new InvalidOperationException($"{result.failures.Count}/{result.cases} record rule cases failed:\n" +
                string.Join("\n", result.failures.Take(12)));
        Debug.Log($"{result.cases} record rule cases passed ({result.rules} rules). " + RecordRuleMetadataValidation.Run());
    }

    public static (int rules, int cases, List<string> failures) Run()
    {
        if (!RuleRegistry.All.Any()) throw new InvalidOperationException("Register the client rule modules before running this test.");
        var rules = new SortedSet<string>(RuleNameDictionary.WholeName.Keys, StringComparer.Ordinal);
        foreach (var manifest in RuleRegistry.All) {
            rules.Add(manifest.RuleId);
            foreach (var sub in manifest.LobbySubRules ?? Array.Empty<RuleLobbySubRule>()) rules.Add(sub.Key);
        }
        var catalog = JObject.Parse(File.ReadAllText("Assets/DataLab/Data/statisticsRules.json"));
        foreach (var entry in catalog["rules"]) rules.Add((string)entry["sub_rule"]);
        rules.RemoveWhere(string.IsNullOrWhiteSpace);

        var canvas = new GameObject("Record rule validation", typeof(RectTransform), typeof(Canvas));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920, 1080);
        var paired = new JObject { ["match_id"] = "paired", ["tables"] = new JObject {
            ["open"] = new JObject(), ["closed"] = new JObject()
        }};
        var originalLanguage = ConfigManager.CurrentLanguage;
        var failures = new List<string>();
        int cases = 0;
        try {
            foreach (var cardName in Cards) {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + cardName + ".prefab");
                var card = Object.Instantiate(prefab, canvas.transform, false);
                card.SetActive(true);
                if (card.GetComponent<RecordPrefab>())
                    ((RectTransform)card.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 1474);
                try {
                    var ordinary = card.GetComponent<RecordPrefab>();
                    var match = card.GetComponent<MatchRecordCard>();
                    var label = card.GetComponentsInChildren<TextMeshProUGUI>(true)
                        .Single(t => t.name == (cardName == "MatchRecordSpectatorStyle" ? "GameRule" : "Rule"));
                    var binding = label.GetComponent<LocalizedPlatformLabel>();
                    foreach (AppLanguage language in Enum.GetValues(typeof(AppLanguage))) {
                        Language.SetValue(null, language);
                        foreach (var localized in card.GetComponentsInChildren<LocalizedPlatformLabel>(true)) localized.Refresh();
                        foreach (string id in rules) {
                            var manifest = RuleRegistry.Resolve(id, id);
                            var name = RuleNameDictionary.WholeName.TryGetValue(id, out string known) ? known :
                                manifest?.RuleId == id ? manifest.LobbyName ?? manifest.DisplayName :
                                manifest?.LobbySubRules?.FirstOrDefault(s => s.Key == id)?.DisplayName;
                            var expected = PlatformText.GetContextual(name, binding ? binding.context : "", language);
                            foreach (bool pairedMode in ordinary ? new[] { false, true } : new[] { true }) {
                                cases++;
                                var summary = pairedMode ? paired : null;
                                if (ordinary) ordinary.InitializeRecordItem("test-match", id, "1/4", "2026-11-19",
                                    Array.Empty<PlayerRecordInfo>(), false, true, summary);
                                else match.Bind(new RecordDetail {sub_rule = id, record = summary}, true);
                                var reason = !binding ? "missing localization binding" : string.IsNullOrWhiteSpace(name) || name == id
                                    ? "missing rule display name" : Measure(card, label, expected);
                                if (reason != null) failures.Add(cardName + " / " + language + " / " + id + " / " +
                                    (pairedMode ? "paired" : "single") + " / " + reason);
                            }
                        }
                    }
                }
                finally { Object.DestroyImmediate(card); }
            }
        }
        finally { Language.SetValue(null, originalLanguage); Object.DestroyImmediate(canvas); }
        return (rules.Count, cases, failures);
    }

    static string Measure(GameObject card, TextMeshProUGUI label, string expected)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)card.transform);
        Canvas.ForceUpdateCanvases();
        label.ForceMeshUpdate(true, true);
        if (label.text != expected || string.IsNullOrWhiteSpace(expected)) return "expected '" + expected + "', got '" + label.text + "'";
        if (!label.isActiveAndEnabled || label.isTextTruncated || label.textInfo.lineCount != 1 ||
            label.textInfo.characterCount != expected.Length) return "incomplete rule text (" + label.textInfo.characterCount + "/" + expected.Length + "), slot " + label.rectTransform.rect + ", row " + ((RectTransform)label.transform.parent).rect;
        var policy = label.GetComponent<PlatformTextBaseline>();
        if (!policy || !policy.Applied || !policy.row || !policy.row.Anchor.gameObject.activeInHierarchy) return "missing active baseline anchor";

        var slot = label.rectTransform.rect;
        var info = label.textInfo;
        bool hasGlyphs = false;
        for (int i = 0; i < info.characterCount; i++) {
            var c = info.characterInfo[i];
            if (!c.isVisible) continue;
            hasGlyphs = true;
            if (c.textElement == null || c.textElement.unicode != c.character) return "missing glyph U+" + ((int)c.character).ToString("X4");
            // TMP includes signed-distance-field padding at the edges of every glyph quad.
            float pad = c.fontAsset.atlasPadding * c.scale + .5f;
            if (c.bottomLeft.y < slot.yMin - pad || c.topRight.y > slot.yMax + pad ||
                c.bottomLeft.x < slot.xMin - pad || c.topRight.x > slot.xMax + pad)
                return $"glyph outside rule slot ({c.bottomLeft.x:F1},{c.bottomLeft.y:F1})..({c.topRight.x:F1},{c.topRight.y:F1}), slot {slot}";
        }
        return hasGlyphs ? null : "no visible glyphs";
    }
}
#endif
