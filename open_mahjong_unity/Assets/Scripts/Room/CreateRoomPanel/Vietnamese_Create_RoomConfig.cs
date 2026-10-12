using System.Collections.Generic;
public sealed class Vietnamese_Create_RoomConfig : Qingque_Create_RoomConfig {
    public const string Version="young-revised-20260822-classic160-v1";
    public Dictionary<string,object> DetailedConfig=new Dictionary<string,object>();
    public Dictionary<string,object> NormalizedConfig() {
        var result=new Dictionary<string,object>();
        foreach(var option in Definition().Options)
            result[option.Key]=DetailedConfig!=null && DetailedConfig.TryGetValue(option.Key,out var value) && option.FindValueIndex(value)>=0?value:option.DefaultValue;
        result["rule_version"]=Version;return result;
    }
    internal static DetailedConfigDefinition Definition()=>new DetailedConfigDefinition("vietnamese",
        new DetailedConfigPresentation("越南麻将 · Classic160","预设","自定义","采用 Bridget Young 2026 修订扩展版的经典规则。","规则版本","Classic160"),
        new[]{new DetailedConfigOption("规则书可选项","seven_pairs","七对（只限自摸）",new[]{"关闭","开启"},new object[]{false,true},false),
new DetailedConfigOption("规则书可选项","same_number_bouquet","同数字花束",new[]{"关闭","开启"},new object[]{false,true},false),
new DetailedConfigOption("规则书可选项","eight_flowers","八花／十花奖励",new[]{"关闭","开启"},new object[]{false,true},false),
new DetailedConfigOption("规则书可选项","discarding_flowers","安全弃三花即和",new[]{"关闭","开启"},new object[]{false,true},false),
new DetailedConfigOption("规则书可选项","blessing_of_man","人和",new[]{"关闭","开启"},new object[]{false,true},false),
new DetailedConfigOption("规则书可选项","six_consecutive_wins","六连和",new[]{"关闭","开启"},new object[]{false,true},false),
new DetailedConfigOption("规则书可选项","orphans_phan","十三幺",new[]{"13 phán","2 mủn"},new object[]{13,12},13),
new DetailedConfigOption("规则书可选项","additional_phan","超过 mủn 的余 phán",new[]{"6点","2点","1点"},new object[]{6,2,1},6),
new DetailedConfigOption("规则书可选项","mun_bonus","每 mủn 额外奖励",new[]{"无","6点","10点","40点"},new object[]{0,6,10,40},0),
new DetailedConfigOption("规则书可选项","quad_bonus","每杠额外奖励",new[]{"无","5点","6点"},new object[]{0,5,6},0)},new[]{new DetailedConfigPreset("规则书默认","160张；1 mủn=6 phán，余 phán 每项6点。",new Dictionary<string,object>())});
}
