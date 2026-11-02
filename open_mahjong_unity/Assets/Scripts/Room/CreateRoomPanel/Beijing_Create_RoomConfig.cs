using System;
using System.Collections.Generic;
using System.Linq;

public sealed class Beijing_Create_RoomConfig : Qingque_Create_RoomConfig {
    public Dictionary<string,object> DetailedConfig=new Dictionary<string,object>();
    public Dictionary<string,object> NormalizedConfig() {
        var result=new Dictionary<string,object>{{"rule_version",BeijingGameState.RuleVersion}};
        foreach(var option in Definition().Options)
            result[option.Key]=DetailedConfig!=null&&DetailedConfig.TryGetValue(option.Key,out var value)&&option.Values.Contains(value)?value:option.DefaultValue;
        return result;
    }
    internal static DetailedConfigDefinition Definition()=>new DetailedConfigDefinition("beijing",
        new DetailedConfigPresentation("北京麻将 · 吃碰提", "规则预设", "自定义", "基础0翻起和；混儿减半启用后最低保留1翻。", "规则来源", "北京麻将（吃碰提）规则书"),
        new[] {
            new DetailedConfigOption("混儿", "hun_mode", "定混儿", new[]{"上滚混儿","下滚混儿","本混儿"},new object[]{"up","down","self"},"up"),
            Switch("行牌", "three_hun_self_draw", "三混儿提"),Switch("行牌", "single_call", "吃碰一口提"),
            Switch("行牌", "dingzhuang", "盯庄"),Switch("行牌", "four_hun_win", "混儿杠"),Switch("行牌", "multi_ron", "一炮多响"),
            Switch("番种", "fan_zimo", "自摸加1翻"),Switch("番种", "fan_dealer", "庄加1翻"),Switch("番种", "fan_repeat", "连庄加翻"),
            Switch("番种", "fan_all_pungs", "碰碰和"),Switch("番种", "fan_all_waits", "混儿悠"),Switch("番种", "fan_kong", "每杠加1翻"),
            Switch("结算", "ron_all_pay", "放炮一家包"),Switch("结算", "half_hun", "混儿减半（保底1翻）"),
            Switch("结算", "pao_kong", "包杠"),Switch("结算", "pao_four_calls", "包四口")
        },new[]{new DetailedConfigPreset("正文规则","上滚混儿，所有可选变体关闭。",new Dictionary<string,object>())});
    private static DetailedConfigOption Switch(string section,string key,string label)=>new DetailedConfigOption(section,key,label,new[]{"关闭","开启"},new object[]{false,true},false);
}
