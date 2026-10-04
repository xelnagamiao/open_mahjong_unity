using System.Collections.Generic;

/// <summary>
/// 规则/子规则显示名映射，供记录列表、牌谱等 UI 使用。直接使用 sub_rule 作为 key 查找。
/// </summary>
public static class RuleNameDictionary {
    /// <summary>sub_rule（或 main rule）-> 长显示名</summary>
    public static readonly Dictionary<string, string> WholeName = new Dictionary<string, string> {
        // 从sub_rule获得
        { "qingque/standard", "青雀" },
        { "guobiao/blood_battle", "国标麻将(血战到底)" },
        { "guobiao/standard", "国标麻将(标准)" },
        { "guobiao/xiaolin", "国标麻将(小林改)" },
        { "guobiao/kshen", "K神麻将" },
        { "guobiao/lanshi", "国标麻将(蓝十改)" },
        { "classical/standard", "古典麻雀" },
        { "sichuan/standard", "四川麻将(血战到底)" },
        { "sichuan/xueliu", "血流成河·弃三张" },
        { "sichuan/xueliu_exchange", "血流成河·换三张" },
        { "changsha", "长沙麻将" },
        { "changsha/classic_double_bird", "长沙麻将(经典双鸟)" },
        { "jiandan/standard", "南雀" },
        { "guangdong", "广东麻将" },
        { "guangdong/tuidao_mil2024", "推倒和（MIL 2024）" },
        { "hongkong", "香港麻将" },
        { "hongkong/qingzhang", "香港清章十三张" },
        { "hongkong/new13", "香港新章十三张" },
        { "hongkong/new13_gametower", "新章十三（Wiki）" },
        { "hongkong/new13_lianhuise", "新章十三（恋绘色）" },
        { "hongkong/qingzhang_lianhuise", "新章十三（恋绘色魔改）" },
        { "hongkong/new16", "港式新章十六张" },
        { "zhongyong", "中庸麻将" },
        { "zhongyong/standard", "标准中庸" },
        { "zhongyong/nanque", "南雀" },
        { "hongque/v1.6", "虹雀" },
        { "taiwan/standard", "台湾麻将" },
        { "shanxi", "山西麻将" },
        { "shanxi/mil2023", "MIL山西（2023）" },
        { "shanghai", "上海麻将" },
        { "shanghai/qiaoma", "上海敲麻" },
        { "shanghai/qinghunpeng", "清混碰" },
        { "riichi/standard", "立直麻将(标准)" },
        { "riichi/langyong", "浪涌麻将(日麻)" },
        { "free", "自由模式" },
        { "free/standard", "自由模式" },
    };

    /// <summary>sub_rule -> 短显示名（预留，可按需补全）</summary>
    public static readonly Dictionary<string, string> ShortName = new Dictionary<string, string> {
        { "guobiao/blood_battle", "国标血战" },
        { "guobiao/standard", "国标" },
        { "guobiao/xiaolin", "小林" },
        { "guobiao/kshen", "K神" },
        { "guobiao/lanshi", "蓝十" },
        { "qingque/standard", "青雀" },
        { "classical/standard", "古典" },
        { "sichuan/standard", "四川" },
        { "sichuan/xueliu", "血流弃三张" },
        { "sichuan/xueliu_exchange", "血流换三张" },
        { "changsha", "长沙" },
        { "changsha/classic_double_bird", "长沙" },
        { "jiandan/standard", "南雀" },
        { "guangdong", "广东麻将" },
        { "guangdong/tuidao_mil2024", "推倒和（MIL 2024）" },
        { "hongkong", "香港麻将" },
        { "hongkong/qingzhang", "香港清章十三张" },
        { "hongkong/new13", "香港新章十三张" },
        { "hongkong/new13_gametower", "新章十三（Wiki）" },
        { "hongkong/new13_lianhuise", "新章十三（恋绘色）" },
        { "hongkong/qingzhang_lianhuise", "新章十三（恋绘色魔改）" },
        { "hongkong/new16", "港式新章十六张" },
        { "zhongyong", "中庸麻将" },
        { "zhongyong/standard", "标准中庸" },
        { "zhongyong/nanque", "南雀" },
        { "hongque/v1.6", "虹雀" },
        { "taiwan/standard", "台湾" },
        { "shanxi", "山西麻将" },
        { "shanxi/mil2023", "MIL山西（2023）" },
        { "shanghai", "上海" },
        { "shanghai/qiaoma", "敲麻" },
        { "shanghai/qinghunpeng", "清混碰" },
        { "riichi/standard", "立直" },
        { "riichi/langyong", "浪涌" },
        { "free", "自由" },
        { "free/standard", "自由" },
    };

    public static string GetWholeName(string subRule) {
        return subRule != null && WholeName.TryGetValue(subRule, out string name) ? name : subRule ?? "";
    }

    public static string GetShortName(string subRule) {
        return subRule != null && ShortName.TryGetValue(subRule, out string name) ? name : subRule ?? "";
    }
}
