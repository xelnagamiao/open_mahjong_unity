using System.Collections.Generic;

/// <summary>
/// 规则/子规则显示名映射，供记录列表、牌谱等 UI 使用。直接使用 sub_rule 作为 key 查找。
/// </summary>
public static class RuleNameDictionary {
    /// <summary>sub_rule（或 main rule）-> 长显示名</summary>
    public static readonly Dictionary<string, string> WholeName = new Dictionary<string, string> {
        // 从sub_rule获得
        { "qingque/standard", "青雀" },
        { "guobiao/standard", "国标麻将(标准)" },
        { "guobiao/xiaolin", "国标麻将(小林改)" },
        { "guobiao/lanshi", "国标麻将(蓝十改)" },
        { "classical/standard", "古典麻雀" },
        // 从roomtype获得
        { "guobiao", "国标麻将" },
        { "qingque", "青雀" },
        { "classical", "古典麻雀" },
        { "riichi", "立直麻将" },
    };

    /// <summary>sub_rule -> 短显示名（预留，可按需补全）</summary>
    public static readonly Dictionary<string, string> ShortName = new Dictionary<string, string> {
        { "guobiao/standard", "国标" },
        { "guobiao/xiaolin", "小林" },
        { "guobiao/lanshi", "蓝十" },
        { "qingque/standard", "青雀" },
        { "classical/standard", "古典" },
    };

    public static string GetWholeName(string subRule) {
        return subRule != null && WholeName.TryGetValue(subRule, out string name) ? name : subRule ?? "";
    }

    public static string GetShortName(string subRule) {
        return subRule != null && ShortName.TryGetValue(subRule, out string name) ? name : subRule ?? "";
    }
}
