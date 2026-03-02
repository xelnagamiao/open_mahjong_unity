using System.Collections.Generic;

/// <summary>
/// 规则/子规则显示名映射，供记录列表、牌谱等 UI 使用。
/// 根据 sub_rule 或 mainRule 查找对应的展示名称。
/// </summary>
public static class RuleNameDictionary {
    /// <summary>
    /// sub_rule -> 显示名 映射。可按需增删键值。
    /// </summary>
    public static readonly Dictionary<string, string> SubRuleToDisplayName = new Dictionary<string, string> {
        { "qingque/standard", "青雀" },
        { "guobiao/standard", "国标麻将(标准)" },
        { "guobiao/xiaolin", "国标麻将(小林改)" },
    };

    /// <summary>
    /// main rule（如 guobiao、qingque、riichi）-> 显示名 映射。sub_rule 未命中时使用。
    /// </summary>
    public static readonly Dictionary<string, string> MainRuleToDisplayName = new Dictionary<string, string> {
        { "guobiao", "国标" },
        { "qingque", "青雀" },
        { "riichi", "立直" },
    };

    /// <summary>
    /// 根据 sub_rule 或 mainRule 获取显示名。未命中时返回原始 subRule 或 mainRule。
    /// </summary>
    public static string GetDisplayName(string subRule, string mainRule) {
        if (!string.IsNullOrEmpty(subRule) && SubRuleToDisplayName.TryGetValue(subRule, out string subName))
            return subName;
        if (!string.IsNullOrEmpty(mainRule) && MainRuleToDisplayName.TryGetValue(mainRule, out string mainName))
            return mainName;
        return string.IsNullOrEmpty(subRule) ? (mainRule ?? "") : subRule;
    }
}
