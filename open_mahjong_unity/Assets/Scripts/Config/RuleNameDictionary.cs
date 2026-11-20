using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// 规则/子规则显示名映射，供记录列表、牌谱等 UI 使用。直接使用 sub_rule 作为 key 查找。
/// </summary>
public static class RuleNameDictionary {
    /// <summary>sub_rule（或 main rule）-> 长显示名</summary>
    public static readonly Dictionary<string, string> WholeName = new Dictionary<string, string> {
        { "filipino", "菲律宾麻将" },
        { "filipino/classic16", "菲律宾麻将" },
        { "vietnamese", "越南麻将" },
        { "vietnamese/classic160", "越南麻将" },
        // 从sub_rule获得
        { "qingque/standard", "青雀" },
        { "guobiao/blood_battle", "国标麻将(血战到底)" },
        { "guobiao/standard", "国标麻将(标准)" },
        { "guobiao/sixteen", "国标麻将（十六张）" },
        { "guobiao/sanma", "三人国标麻将" },
        { "guobiao/transparent", "透明国标麻将" },
        { "guobiao/transparent_realtime", "国标透明（实时手牌）" },
        { "guobiao/huahun", "国标（花混癞子）" },
        { "guobiao/four_jokers", "国标麻将(四癞子)" },
        { "guobiao/xiaolin", "国标麻将(小林改)" },
        { "guobiao/kshen", "K神麻将" },
        { "guobiao/lanshi", "国标麻将(蓝十改)" },
        { "classical/standard", "古典麻雀" },
        { "sichuan/standard", "四川麻将(血战到底)" },
        { "sichuan/xuezhan_exchange", "血战到底·换三张" },
        { "sichuan/doubles", "血战双打·2V2" },
        { "sichuan/xueliu", "血流成河·弃三张" },
        { "sichuan/xueliu_exchange", "血流成河·换三张" },
        { "sichuan/xueliu_doubles", "血流双打·2V2" },
        { "sichuan/doubles_duplicate", "血战双打复式" },
        { "sichuan/xueliu_doubles_duplicate", "血流双打复式" },
        { "changsha", "长沙麻将" },
        { "changsha/classic_double_bird", "长沙麻将(经典双鸟)" },
        { "changsha/exchange_six", "长沙麻将(换六张)" },
        { "guangdong", "广东麻将" },
        { "guangdong/mil2023-doubles", "广东麻将（推广）2V2 双打" },
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
        { "shanghai/lajihe", "上海垃圾和" },
        { "riichi/standard", "立直麻将(标准)" },
        { "riichi/langyong", "浪涌麻将(日麻)" },
        { "riichi/sanma", "三人立直麻将" },
        { "riichi/washizu", "鹫巢麻将" },
        { "riichi/washizu_realtime", "鹫巢麻将（实时手牌）" },
        { "riichi/washizu_doubles", "鹫巢麻将（双打）" },
        { "free", "自由模式" },
        { "free/standard", "自由模式" },
    };

    /// <summary>sub_rule -> 短显示名（预留，可按需补全）</summary>
    public static readonly Dictionary<string, string> ShortName = new Dictionary<string, string> {
        { "filipino", "菲律宾麻将" },
        { "filipino/classic16", "菲律宾麻将" },
        { "vietnamese", "越南麻将" },
        { "vietnamese/classic160", "越南麻将" },
        { "guobiao/blood_battle", "国标血战" },
        { "guobiao/standard", "国标" },
        { "guobiao/sixteen", "国标麻将（十六张）" },
        { "guobiao/sanma", "三人国标" },
        { "guobiao/transparent", "透明国标" },
        { "guobiao/transparent_realtime", "国标透明（实时手牌）" },
        { "guobiao/huahun", "花混癞子" },
        { "guobiao/four_jokers", "四癞子" },
        { "guobiao/xiaolin", "小林" },
        { "guobiao/kshen", "K神" },
        { "guobiao/lanshi", "蓝十" },
        { "qingque/standard", "青雀" },
        { "classical/standard", "古典" },
        { "sichuan/standard", "四川" },
        { "sichuan/xuezhan_exchange", "血战到底·换三张" },
        { "sichuan/doubles", "血战双打" },
        { "sichuan/xueliu", "血流弃三张" },
        { "sichuan/xueliu_exchange", "血流换三张" },
        { "sichuan/xueliu_doubles", "血流双打" },
        { "sichuan/doubles_duplicate", "血战复式" },
        { "sichuan/xueliu_doubles_duplicate", "血流复式" },
        { "changsha", "长沙" },
        { "changsha/classic_double_bird", "长沙" },
        { "changsha/exchange_six", "换六张" },
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
        { "shanghai/lajihe", "垃圾和" },
        { "riichi/standard", "立直" },
        { "riichi/langyong", "浪涌" },
        { "riichi/sanma", "三麻" },
        { "riichi/washizu", "鹫巢" },
        { "riichi/washizu_realtime", "鹫巢实时手牌" },
        { "riichi/washizu_doubles", "鹫巢双打" },
        { "free", "自由" },
        { "free/standard", "自由" },
    };

    public static string GetWholeName(string subRule) {
        return subRule != null && WholeName.TryGetValue(subRule, out string name) ? name : subRule ?? "";
    }

    public static string GetRecordName(string rule, string subRule) {
        string key = string.IsNullOrWhiteSpace(subRule) ? rule?.Trim() : subRule.Trim();
        if (string.IsNullOrEmpty(key)) return "未知规则";
        string id = key.ToLowerInvariant();
        if (id == "gb") id = "guobiao";
        if (id == "jp") id = "riichi";
        if (WholeName.TryGetValue(id, out string name)) return name;
        var manifest = RuleRegistry.Resolve(id, id);
        if (manifest != null) {
            if (manifest.RuleId == id) return manifest.LobbyName ?? manifest.DisplayName ?? key;
            if (manifest.LobbySubRules != null) {
                foreach (var entry in manifest.LobbySubRules)
                    if (entry?.Key == id && !string.IsNullOrEmpty(entry.DisplayName)) return entry.DisplayName;
            }
        }
        return key;
    }

    public static (string rule, string subRule) RecordKeys(RecordDetail detail) {
        if (detail == null) return (null, null);
        return RecordKeys(detail.rule, detail.sub_rule, detail.record, detail.selected_table_id);
    }

    public static (string rule, string subRule) RecordKeys(string rule, string subRule, JObject match,
        string tableId = null) {
        void Read(JObject source) {
            if (source == null) return;
            if (string.IsNullOrWhiteSpace(rule)) rule = (string)(source["rule"] as JValue);
            if (string.IsNullOrWhiteSpace(subRule)) subRule = (string)(source["sub_rule"] as JValue);
        }
        bool Incomplete() => string.IsNullOrWhiteSpace(rule) || string.IsNullOrWhiteSpace(subRule);
        if (Incomplete()) Read(match);
        if (Incomplete()) Read(match?["game_title"] as JObject);
        if (Incomplete() && match?["tables"] is JObject tables) {
            void ReadTable(string id) {
                if (string.IsNullOrEmpty(id) || !(tables[id] is JObject table)) return;
                if (table["payload"] is JObject payload) Read(payload["game_title"] as JObject);
            }
            ReadTable(tableId);
            if (Incomplete()) ReadTable("main");
            if (Incomplete()) ReadTable("open");
            if (Incomplete()) ReadTable("closed");
        }
        return (rule?.Trim(), subRule?.Trim());
    }

    public static string GetShortName(string subRule) {
        return subRule != null && ShortName.TryGetValue(subRule, out string name) ? name : subRule ?? "";
    }
}
