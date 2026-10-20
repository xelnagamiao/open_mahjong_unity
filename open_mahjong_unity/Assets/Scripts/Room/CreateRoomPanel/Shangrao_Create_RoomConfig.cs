using System.Collections.Generic;

public sealed class Shangrao_Create_RoomConfig : Qingque_Create_RoomConfig {
    public Dictionary<string, object> DetailedConfig = new Dictionary<string, object>();
    public Dictionary<string, object> NormalizedConfig() {
        var result = new Dictionary<string, object> { {"rule_version", ShangraoGameState.RuleVersion} };
        foreach (var option in Definition().Options)
            result[option.Key] = DetailedConfig != null && DetailedConfig.TryGetValue(option.Key, out var value) && value is bool ? value : false;
        return result;
    }
    internal static DetailedConfigDefinition Definition() => new DetailedConfigDefinition("shangrao",
        new DetailedConfigPresentation("上饶麻将 · 规则设置", "规则预设", "自定义", "按房间约定选择附加规则。", "规则来源", "上饶规则书与补充说明"),
        new[] {
            Switch("qingyise", "清一色"), Switch("ziyise", "字一色"),
            Switch("allow_special_bao", "九幺允许宝牌"), Switch("four_bao_reward", "四宝奖励（局末2分）"),
            Switch("flying_seven_stars", "飞宝保留七星")
        }, new[] {new DetailedConfigPreset("原规则", "全部附加开关关闭。", new Dictionary<string, object>())});
    private static DetailedConfigOption Switch(string key, string label) =>
        new DetailedConfigOption("可选规则", key, label, new[] {"关闭", "开启"}, new object[] {false, true}, false);
}
