using System.Collections.Generic;

public sealed class NewChinese_Create_RoomConfig : Qingque_Create_RoomConfig {
    public Dictionary<string,object> DetailedConfig;

    public Dictionary<string,object> NormalizedConfig() {
        bool exchange=DetailedConfig!=null && DetailedConfig.TryGetValue("exchange_three",out var raw) && raw is bool value && value;
        return new Dictionary<string,object> {{"rule_version","new-chinese-2026-11"},{"exchange_three",exchange}};
    }

    internal static DetailedConfigDefinition Definition() => new DetailedConfigDefinition("new_chinese",
        new DetailedConfigPresentation("新中式麻将", "规则预设", "自定义", "房主选择的行牌设置", "规则来源", "新中式麻将规则说明书"),
        new[]{new DetailedConfigOption("行牌","exchange_three","换三张",new[]{"关闭","开启"},new object[]{false,true},false)},
        new[]{new DetailedConfigPreset("标准规则","不开启换三张",new Dictionary<string,object>())});
}
