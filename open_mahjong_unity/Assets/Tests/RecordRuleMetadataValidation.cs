#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

public static class RecordRuleMetadataValidation {
    public static string Run() {
        int checks = 0;
        void Equal(string actual, string expected, string label) {
            if (actual != expected) throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'");
            checks++;
        }
        foreach (var type in new[] { typeof(GuobiaoRuleBootstrap), typeof(RiichiRuleBootstrap) })
            type.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);

        const string blood = "血战到底·换三张", doubles = "广东麻将（推广）2V2 双打";
        Equal(RuleNameDictionary.GetRecordName("GB", null), "国标麻将", "legacy GB");
        Equal(RuleNameDictionary.GetRecordName(" riichi ", " "), "立直麻将", "family only");
        Equal(RuleNameDictionary.GetRecordName("JP", null), "立直麻将", "legacy JP");
        Equal(RuleNameDictionary.GetRecordName(null, "SICHUAN/XUEZHAN_EXCHANGE "), blood, "exchange record");
        Equal(RuleNameDictionary.GetRecordName("guangdong", "guangdong/mil2023-doubles"), doubles, "Guangdong doubles");
        Equal(RuleNameDictionary.GetRecordName("riichi", "riichi/Future"), "riichi/Future", "unrecognized variant");
        Equal(RuleNameDictionary.GetRecordName("NewFamily", null), "NewFamily", "unrecognized family");
        Equal(RuleNameDictionary.GetRecordName(" ", null), "未知规则", "empty metadata");

        JObject Table(string sub) => new JObject {
            ["payload"] = new JObject { ["game_title"] = new JObject { ["rule"] = "sichuan", ["sub_rule"] = sub } }
        };
        var archive = new JObject { ["tables"] = new JObject {
            ["open"] = Table("sichuan/doubles_duplicate"), ["closed"] = Table("sichuan/xueliu_doubles_duplicate")
        } };
        var detail = new RecordDetail { record = archive, selected_table_id = "closed" };
        Equal(RuleNameDictionary.RecordKeys(detail).subRule, "sichuan/xueliu_doubles_duplicate", "selected table");
        Equal(RuleNameDictionary.RecordKeys(null, null, archive).subRule, "sichuan/doubles_duplicate", "list fallback to open");
        ((JObject)archive["tables"])["main"] = Table("sichuan/standard");
        Equal(RuleNameDictionary.RecordKeys(null, null, archive).subRule, "sichuan/standard", "single-table archive");
        detail.record = new JObject {
            ["game_title"] = new JObject { ["rule"] = "guangdong", ["sub_rule"] = "guangdong/mil2023-doubles" }
        };
        Equal(RuleNameDictionary.RecordKeys(detail).subRule, "guangdong/mil2023-doubles", "legacy record title");
        detail.record = archive;
        archive["sub_rule"] = "sichuan/xuezhan_exchange";
        Equal(RuleNameDictionary.RecordKeys(detail).subRule, "sichuan/xuezhan_exchange", "V2 metadata");
        Equal(RuleNameDictionary.RecordKeys(null, "riichi/sanma", archive).subRule, "riichi/sanma", "explicit list metadata");
        detail.sub_rule = "guobiao/standard";
        Equal(RuleNameDictionary.RecordKeys(detail).subRule, "guobiao/standard", "explicit detail metadata");

        detail = new RecordDetail { record = new JObject {
            ["game_title"] = new JObject { ["rule"] = "sichuan", ["sub_rule"] = "sichuan/xuezhan_exchange" }
        } };
        var index = (RecordInfo)typeof(LocalRecordStore).GetMethod("ToInfo", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { detail, null });
        Equal(index.rule, "sichuan", "local index family");
        Equal(index.sub_rule, "sichuan/xuezhan_exchange", "local index subrule");
        Equal(detail.sub_rule, null, "source archive preserved");
        Equal(RuleNameDictionary.RecordKeys((RecordDetail)null).subRule, null, "missing archive");
        Equal(RuleNameDictionary.RecordKeys(null, null, new JObject { ["tables"] = new JObject { ["closed"] = Table("sichuan/xueliu") } }).subRule,
            "sichuan/xueliu", "closed-only partial archive");

        var localized = new[] {
            (AppLanguage.SimplifiedChinese, blood, doubles),
            (AppLanguage.TraditionalChinese, "血戰到底·換三張", "廣東麻將（推廣）2V2 雙打"),
            (AppLanguage.English, "Bloody Battle with Three-Tile Exchange", "Guangdong Mahjong · 2v2 Doubles"),
            (AppLanguage.French, "Bataille avec échange de trois tuiles", "Mahjong du Guangdong · double 2 contre 2"),
            (AppLanguage.German, "Blutiger Kampf mit Dreiertausch", "Guangdong-Mahjong · 2 gegen 2"),
            (AppLanguage.Russian, "Кровавая битва с обменом трёх", "Гуандунский маджонг · пары 2 на 2"),
            (AppLanguage.Japanese, "血戦到底・3枚交換", "広東麻雀・2対2ダブルス"),
            (AppLanguage.Spanish, "Batalla sangrienta con intercambio de tres fichas", "Mahjong de Guangdong · dobles 2 contra 2")
        };
        foreach (var (language, exchangeName, doublesName) in localized) {
            Equal(PlatformText.Get(blood, language), exchangeName, language + " exchange");
            Equal(PlatformText.Get(doubles, language), doublesName, language + " doubles");
        }
        return $"Record rule metadata: {checks} checks passed";
    }
}
#endif
