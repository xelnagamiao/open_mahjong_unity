using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>阅读用番表的规则、分类与说明书地址；不参与对局计番。</summary>
public static class MahjongFanCatalog {
    public static readonly string[] RuleIds = { "guobiao", "riichi", "qingque", "hongque", "sichuan" };
    public static readonly string[] RuleNames = { "国标麻将", "立直麻将", "青雀", "虹雀", "川麻血战" };
    private static readonly string[] ResourceNames = { "GuobiaoFanCatalog", "RiichiFanCatalog", "QingqueFanCatalog", "HongqueFanCatalog", "SichuanFanCatalog" };
    private static readonly Dictionary<string, Document> Documents = new Dictionary<string, Document>();

    [Serializable] public sealed class Category {
        public string key;
        public string label;
    }
    [Serializable] public sealed class Document {
        public string id;
        public string displayName;
        public string rulebookPath;
        public string source;
        public Category[] categories;
        public GuobiaoFanCatalog.Entry[] entries;
    }

    public static string ForRoom(string roomRule, string subRule) {
        // 血流与双打有自己的规则说明，不能用血战到底番表替代。
        if (roomRule == "sichuan" && !string.IsNullOrEmpty(subRule) && subRule != "sichuan/standard") return null;
        return Array.IndexOf(RuleIds, roomRule ?? "") >= 0 ? roomRule : null;
    }

    public static Document Get(string ruleId) {
        if (Documents.TryGetValue(ruleId, out var document)) return document;
        int index = Array.IndexOf(RuleIds, ruleId);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(ruleId));
        var asset = Resources.Load<TextAsset>(ResourceNames[index]);
        if (!asset) throw new InvalidOperationException("Missing " + ResourceNames[index] + ".json");
        document = JsonUtility.FromJson<Document>(asset.text);
        document.id = ruleId;
        document.displayName = RuleNames[index];
        document.rulebookPath = "/rulebook/" + ruleId;
        if (document.categories == null || document.categories.Length == 0) {
            document.categories = new Category[GuobiaoFanCatalog.FanValues.Length];
            for (int i = 0; i < document.categories.Length; i++) {
                string value = GuobiaoFanCatalog.FanValues[i].ToString();
                document.categories[i] = new Category { key = value, label = value + "番" };
            }
        }
        Documents.Add(ruleId, document);
        return document;
    }
}
