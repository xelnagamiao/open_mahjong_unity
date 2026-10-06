using System;
using System.Collections.Generic;

/// <summary>资料统计入口；同族的三人场和子规则保留独立的统计标识。</summary>
public static class PlayerInfoRuleCatalog {
    public const string GuobiaoSanma = "guobiao_sanma";
    public const string GuangdongTuidao = "guangdong_tuidao";
    public const string ShanghaiQinghunpeng = "shanghai_qinghunpeng";
    public const string Nanque = "nanque";
    public static readonly string[] PrimaryRules = { "guobiao", "riichi", "qingque", "sichuan" };
    private static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string> {
        { "changsha", "长沙麻将" }, { "taiwan", "台湾麻将" }, { "hongkong", "香港麻将" },
        { "shanxi", "山西麻将" }, { "changchun", "长春麻将" }, { "guizhou", "贵州麻将" },
        { "yixing", "宜兴麻将" }, { "wenzhou", "温州麻将" }, { "hangzhou", "杭州麻将" },
        { "hongzhong", "红中麻将" }, { "hongque", "虹雀" },
        { "free", "自由模式" }, { "classical", "古典麻将" },
        { "zhongyong", "中庸麻将" },
    };

    public static List<KeyValuePair<string, string>> OtherRules(bool ranked) {
        var rules = new List<KeyValuePair<string, string>>();
        void Add(string id, string name) => rules.Add(new KeyValuePair<string, string>(id, name));
        if (!ranked) Add(GuobiaoSanma, Name(GuobiaoSanma));
        Add(RiichiSanmaRankConfig.Rule, Name(RiichiSanmaRankConfig.Rule));
        Add(RankedRules.XueliuExchangeRule, "川麻血流换三张");
        if (ranked) return rules;
        foreach (var manifest in RuleRegistry.Ordered) {
            if (manifest.HideFromLobby || Array.IndexOf(PrimaryRules, manifest.RuleId) >= 0) continue;
            Add(manifest.RuleId, Name(manifest.RuleId) ?? manifest.LobbyName ?? manifest.DisplayName);
            if (manifest.RuleId == "guangdong") {
                Add(GuangdongTuidao, "广东麻将（推倒和）");
            } else if (manifest.RuleId == "shanghai") {
                Add(ShanghaiQinghunpeng, "上海麻将（清混碰）");
            }
        }
        Add(Nanque, "南雀");
        return rules;
    }

    public static string SourceRule(string rule) => rule == GuobiaoSanma ? "guobiao"
        : rule == RiichiSanmaRankConfig.Rule ? "riichi"
        : rule == RankedRules.XueliuExchangeRule ? "sichuan"
        : rule == GuangdongTuidao ? "guangdong"
        : rule == ShanghaiQinghunpeng ? "shanghai"
        : rule == Nanque || rule == "jiandan" ? "zhongyong" : rule;

    public static string SubRule(string rule) => rule == GuobiaoSanma ? "guobiao/sanma"
        : rule == RiichiSanmaRankConfig.Rule ? "riichi/sanma"
        : rule == RankedRules.XueliuExchangeRule ? "sichuan/xueliu_exchange"
        : rule == GuangdongTuidao ? "guangdong/tuidao_mil2024"
        : rule == "guangdong" ? "guangdong/mil2023"
        : rule == "shanghai" ? "shanghai/qiaoma"
        : rule == ShanghaiQinghunpeng ? "shanghai/qinghunpeng"
        : rule == Nanque ? "zhongyong/nanque" : null;

    public static int PlayerCount(string rule) => rule == GuobiaoSanma || rule == RiichiSanmaRankConfig.Rule ? 3 : 4;
    public static bool IsRiichi(string rule) => rule == "riichi" || rule == RiichiSanmaRankConfig.Rule;
    public static bool Supports(string rule) => Array.IndexOf(PrimaryRules, rule) >= 0
        || rule == GuobiaoSanma || rule == GuangdongTuidao || rule == ShanghaiQinghunpeng || rule == Nanque
        || RankedRules.Supports(rule) || RuleRegistry.Resolve(rule) != null;
    public static string Name(string rule) => rule == GuobiaoSanma ? "国标(三人)"
        : rule == RiichiSanmaRankConfig.Rule ? "立直(三人)"
        : rule == GuangdongTuidao ? "广东麻将（推倒和）"
        : rule == "shanghai" ? "上海麻将（敲麻）"
        : rule == ShanghaiQinghunpeng ? "上海麻将（清混碰）"
        : rule == Nanque || rule == "jiandan" ? "南雀"
        : rule == "guangdong" ? "广东麻将"
        : rule != null && DisplayNames.TryGetValue(rule, out string name) ? name : null;
}
