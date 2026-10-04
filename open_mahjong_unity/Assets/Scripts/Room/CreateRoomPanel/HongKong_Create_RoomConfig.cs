using System.Collections.Generic;

/// <summary>香港馆规：规则书默认值与房间覆盖项随牌谱一起保存。</summary>
public sealed class HongKong_Create_RoomConfig : Qingque_Create_RoomConfig {
    public const string RemixDescription="香港新派清章（恋绘色魔改版）是恋绘色以香港麻将（新派清章）为原型灵感，深度魔改而来的个人规则，在保留了三三制（混一色和对对和三番、清一色七番）骨架及番数与分数非线性对应关系的基础上大量增加国标麻将、立直麻将和中庸麻将的部分番种/役种/和种，以期达到更丰富的造牌体验，并避免了番数增加后分数过于膨胀等问题，总之是融合了香港麻将、立直麻将、国标麻将和中庸麻将的大杂烩。";
    public Dictionary<string,object> DetailedConfig=new Dictionary<string,object>();

    public Dictionary<string,object> NormalizedConfig() {
        var result=new Dictionary<string,object>();
        foreach (var option in Definition(SubRule,DetailedConfig).Options)
            result[option.Key]=DetailedConfig.TryGetValue(option.Key,out var value) && value?.ToString()!="default"
                ? value : option.DefaultValue;
        bool lianhuise=IsLianhuise(SubRule,DetailedConfig);
        result["new13_version"]=lianhuise ? "lianhuise" : "gametower";
        result["flowers"]=SubRule==HongKongGameState.New16 || SubRule==HongKongGameState.QingzhangRemix ||
            ((SubRule==HongKongGameState.Qingzhang || lianhuise) &&
             (result["flowers"] is bool on ? on : lianhuise));
        return result;
    }

    public static bool IsLianhuise(string subRule, IDictionary<string,object> config) =>
        subRule==HongKongGameState.New13Lianhuise ||
        (subRule==HongKongGameState.New13 && config!=null && config.TryGetValue("new13_version",out var version) && version?.ToString()=="lianhuise");

    public static bool IsGametower(string subRule,IDictionary<string,object> config) =>
        subRule==HongKongGameState.New13Gametower || (subRule==HongKongGameState.New13 && !IsLianhuise(subRule,config));

    public static string RulebookPath(string subRule,IDictionary<string,object> config) {
        if (IsLianhuise(subRule,config)) subRule=HongKongGameState.New13Lianhuise;
        else if (IsGametower(subRule,config)) subRule=HongKongGameState.New13Gametower;
        else if (subRule!=HongKongGameState.QingzhangRemix && subRule!=HongKongGameState.New16)
            subRule=HongKongGameState.Qingzhang;
        return "/rulebook/hongkong?sub_rule="+System.Uri.EscapeDataString(subRule);
    }

    public static bool OptionVisible(string key,string subRule,IDictionary<string,object> config) {
        bool lianhuise=IsLianhuise(subRule,config);
        switch (key) {
            case "flowers": return subRule==HongKongGameState.Qingzhang || lianhuise;
            case "new13_full_shoot": return IsGametower(subRule,config);
            case "liability_twelve": return subRule!=HongKongGameState.New16;
            case "liability_dragons": return subRule==HongKongGameState.Qingzhang || lianhuise;
            case "liability_kong": return lianhuise;
            case "liability_limit": return subRule==HongKongGameState.QingzhangRemix;
            default: return true;
        }
    }

    public static string SourceDescription(string subRule,IDictionary<string,object> config) {
        if (IsLianhuise(subRule,config)) return "恋绘色版本：采用《香港新章规则书》。默认144张、三番起和、十三番封顶；全铳制、庄和或流局连庄，不设报听。馆规可在下方调整。";
        if (IsGametower(subRule,config)) return "Wiki版本：采用 Mahjong Wiki 的 IGS 番表。136张、线性计分，支持七对子与报听；默认每局轮庄。馆规可在下方调整。";
        if (subRule==HongKongGameState.QingzhangRemix) return RemixDescription;
        if (subRule==HongKongGameState.Qingzhang) return "香港麻雀协会清章十三张：默认无花、三番起和、十番封顶、每局轮庄。花牌和馆规可在下方调整。";
        return "香港麻雀协会新章十六张详述版：固定144张，叮牌、连庄、拉踢。馆规可在下方调整。";
    }

    internal static DetailedConfigDefinition Definition(string subRule=HongKongGameState.Qingzhang,IDictionary<string,object> config=null) {
        bool sixteen=subRule==HongKongGameState.New16;
        bool remix=subRule==HongKongGameState.QingzhangRemix;
        bool lianhuise=IsLianhuise(subRule,config);
        // The remix's legacy wire value "default" means win or tenpai draw;
        // keep that distinct from repeating after every draw.
        var dealerChoices=remix ? new[] {"每局轮庄","庄和或流局连庄","庄和或流局听牌连庄"} : new[] {"每局轮庄","庄和或流局连庄"};
        var dealerValues=remix ? new object[] {"rotate","win_or_draw","default"} : new object[] {"rotate","win_or_draw"};
        return new DetailedConfigDefinition("hongkong",
        new DetailedConfigPresentation("香港麻将 · 房间设置","预设","自定义","修改项为本房间馆规；规则书番表保持不变。","规则版本","详见规则书"),
        new[] {
            new DetailedConfigOption("香港麻将","flowers","花牌",new[] {"无花（136张）","有花（144张）"},new object[] {false,true},sixteen || lianhuise || remix),
            new DetailedConfigOption("香港麻将","self_draw_only","和牌方式",new[] {"可点和与自摸","只可自摸"},new object[] {false,true},false),
            new DetailedConfigOption("香港麻将","win_claim","多人和同一张牌",new[] {"截胡","一炮多响"},new object[] {"head_bump","multiple"},sixteen ? "multiple" : "head_bump"),
            new DetailedConfigOption("香港麻将","dealer_mode","连庄方式",dealerChoices,dealerValues,remix ? "default" : sixteen || lianhuise ? "win_or_draw" : "rotate"),
            new DetailedConfigOption("香港麻将","new13_full_shoot","放铳付款",new[] {"全冲（放铳者付四份）","非全冲（2+1+1）"},new object[] {true,false},true),
            new DetailedConfigOption("香港麻将","liability_twelve","包十二张",new[] {"开启","关闭"},new object[] {true,false},true),
            new DetailedConfigOption("香港麻将","liability_dragons","包大三元",new[] {"开启","关闭"},new object[] {true,false},true),
            new DetailedConfigOption("香港麻将","liability_kong","生章明杠包自摸",new[] {"开启","关闭"},new object[] {true,false},true),
            new DetailedConfigOption("香港麻将","liability_limit","包满贯",new[] {"开启","关闭"},new object[] {true,false},true),
        },
        new[] {new DetailedConfigPreset("默认配置","使用所选版本的默认馆规。",new Dictionary<string,object>())});
    }
}
