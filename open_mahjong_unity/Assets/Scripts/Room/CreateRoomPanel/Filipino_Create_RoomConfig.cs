using System.Collections.Generic;
public sealed class Filipino_Create_RoomConfig : Qingque_Create_RoomConfig {
    public const string Version="classic16-ging2010-v1";
    public Dictionary<string,object> DetailedConfig=new Dictionary<string,object>();
    public Dictionary<string,object> NormalizedConfig() {
        var result=new Dictionary<string,object>();
        foreach(var option in Definition().Options)
            result[option.Key]=DetailedConfig!=null && DetailedConfig.TryGetValue(option.Key,out var value) && option.FindValueIndex(value)>=0 ? value : option.DefaultValue;
        result["rule_version"]=Version; return result;
    }
    internal static DetailedConfigDefinition Definition()=>new DetailedConfigDefinition("filipino",
        new DetailedConfigPresentation("菲律宾麻将 · 经典十六张","预设","自定义","选择本房间使用的规则。","规则来源","Ging 2010"),
        new[]{new DetailedConfigOption("经典十六张","base_points","基准分",new[]{"4点","8点","16点"},new object[]{4,8,16},4),
new DetailedConfigOption("经典十六张","self_draw_multiplier","自摸倍数",new[]{"一倍","双倍"},new object[]{1,2},2),
new DetailedConfigOption("经典十六张","discard_payment","点和付款",new[]{"三家各付","仅放铳者付"},new object[]{"all","discarder"},"all"),
new DetailedConfigOption("经典十六张","seven_pairs","七对加一组",new[]{"开启","关闭"},new object[]{true,false},true),
new DetailedConfigOption("经典十六张","pair_quads_as_two","四同牌算两对",new[]{"关闭","开启"},new object[]{false,true},false),
new DetailedConfigOption("经典十六张","ambitions","附加奖励",new[]{"开启","关闭"},new object[]{true,false},true),
new DetailedConfigOption("经典十六张","opening_no_flowers","起手无花奖励",new[]{"开启","关闭"},new object[]{true,false},true),
new DetailedConfigOption("经典十六张","opening_one_flower","起手一花奖励",new[]{"关闭","开启"},new object[]{false,true},false),
new DetailedConfigOption("经典十六张","extra_flower_thresholds","花牌门槛",new[]{"13花","13／16／19花"},new object[]{false,true},false),
new DetailedConfigOption("经典十六张","secret_points","暗杠奖励",new[]{"0点","1点","2点"},new object[]{0,1,2},1),
new DetailedConfigOption("经典十六张","kong_points","明杠奖励",new[]{"0点","1点","2点"},new object[]{0,1,2},2),
new DetailedConfigOption("经典十六张","sagasa_points","加杠奖励",new[]{"0点","1点","2点"},new object[]{0,1,2},2),
new DetailedConfigOption("经典十六张","dice_double","双骰加倍",new[]{"关闭","开启"},new object[]{false,true},false),
new DetailedConfigOption("经典十六张","dealer_mode","庄家",new[]{"和者掷骰定庄","每局轮庄","和者坐庄"},new object[]{"winner_roll","rotate","winner"},"winner_roll")},
        new[]{new DetailedConfigPreset("classic16 经典","依据 Ging 2010；未规定的基础支付统一为4游戏点。",new Dictionary<string,object>()),
              new DetailedConfigPreset("Manila 支付选项","保留经典牌形，改用 Manila 速查表的付款及杠奖励。",new Dictionary<string,object>{{"self_draw_multiplier",1},{"discard_payment","discarder"},{"secret_points",2},{"sagasa_points",1},{"extra_flower_thresholds",true}})});
}
