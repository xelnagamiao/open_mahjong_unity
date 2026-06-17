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
        { "guobiao/kshen", "K神麻将" },
        { "guobiao/lanshi", "国标麻将(蓝十改)" },
        { "classical/standard", "古典麻雀" },
        { "sichuan/standard", "四川麻将(血战到底)" },
        { "riichi/standard", "立直麻将(标准)" },
        { "riichi/langyong", "浪涌麻将(日麻)" },
    };

    /// <summary>sub_rule -> 短显示名（预留，可按需补全）</summary>
    public static readonly Dictionary<string, string> ShortName = new Dictionary<string, string> {
        { "guobiao/standard", "国标" },
        { "guobiao/xiaolin", "小林" },
        { "guobiao/kshen", "K神" },
        { "guobiao/lanshi", "蓝十" },
        { "qingque/standard", "青雀" },
        { "classical/standard", "古典" },
        { "sichuan/standard", "四川" },
        { "riichi/standard", "立直" },
        { "riichi/langyong", "浪涌" },
    };

    public static string GetWholeName(string subRule) {
        return subRule != null && WholeName.TryGetValue(subRule, out string name) ? name : subRule ?? "";
    }

    public static string GetShortName(string subRule) {
        return subRule != null && ShortName.TryGetValue(subRule, out string name) ? name : subRule ?? "";
    }
}
