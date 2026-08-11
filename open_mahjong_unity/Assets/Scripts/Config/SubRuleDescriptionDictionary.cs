using System.Collections.Generic;

/// <summary>创建房间界面中的子规则名称与介绍。</summary>
public sealed class CreateRoomSubRuleTextConfig {
    public string Key { get; }
    public string DisplayName { get; }
    public string Description { get; }

    public CreateRoomSubRuleTextConfig(string key, string displayName, string description) {
        Key = key;
        DisplayName = displayName;
        Description = description;
    }
}

/// <summary>创建房间界面中的主规则名称及其子规则文案。</summary>
public sealed class CreateRoomRuleTextConfig {
    public string Rule { get; }
    public string DisplayName { get; }
    public IReadOnlyList<CreateRoomSubRuleTextConfig> SubRules { get; }

    public CreateRoomRuleTextConfig(
        string rule,
        string displayName,
        params CreateRoomSubRuleTextConfig[] subRules) {
        Rule = rule;
        DisplayName = displayName;
        SubRules = subRules;
    }

    public CreateRoomSubRuleTextConfig GetSubRule(int index) {
        if (SubRules.Count == 0) return null;
        return SubRules[index >= 0 && index < SubRules.Count ? index : 0];
    }
}

/// <summary>
/// 创建房间规则文案配置。数组顺序同时决定主规则下拉框顺序，
/// 子规则数组顺序决定对应的子规则下拉框顺序。
/// </summary>
public static class CreateRoomRuleTextConfigCatalog {
    public static readonly IReadOnlyList<CreateRoomRuleTextConfig> Rules =
        new CreateRoomRuleTextConfig[] {
            new CreateRoomRuleTextConfig("guobiao", "国标麻将",
                new CreateRoomSubRuleTextConfig("guobiao/standard", "标准规(新编MCR)", "国标麻将源于国家体育总局于1998年11月出台的《中国竞技麻将比赛规则(试行)》、是中国唯一由官方确立的竞技麻将规则；本平台参照Natsuki编著的新编MCR撰写运行逻辑，已通过所有牌例验证，如发现测试过程中出现了不符合国标麻将规则预期的行为，请向Q群906497522反馈。"),
                new CreateRoomSubRuleTextConfig("guobiao/xiaolin", "国标麻将(小林改)", "小林改版国标麻将，对国标麻将进行了番数平衡，还处于测试版，取消了8番起胡和底分，改为点和得分x2，自摸番三。非竞技规则，只为娱乐。"),
                new CreateRoomSubRuleTextConfig("guobiao/kshen", "K神麻将", "K神改版国标麻将，新增镜同、四连刻等番种，复合番100封顶，默认8番起和。小牌点炮无责：点和12分以下三家各付n；12分以上两家各付12，放铳者付3n-24。自摸三家各付n。可开启错和、可自定义起和番。出现计分bug可在群里向q975653345反馈"),
                new CreateRoomSubRuleTextConfig("guobiao/lanshi", "国标麻将(蓝十改)", "蓝十改版的国标麻将规则，对国标麻将的番种表进行了全面的修改，并根据番种的难度调整了评分，5分起和，授受制为半全铳半分付。如在测试中发现设计问题或有任何建议，可以联系规则制定人蓝十QQ1002094810。")),
            new CreateRoomRuleTextConfig("riichi", "立直麻将",
                new CreateRoomSubRuleTextConfig("riichi/standard", "立直麻将(标准)", "立直麻将参照天凤/雀魂规则进行设计，无双倍役满"),
                new CreateRoomSubRuleTextConfig("riichi/langyong", "浪涌麻将", "让每一局，都像海浪般汹涌滔滔｜一、每吃、碰、杠一次，自己的浪涌点数+1（初始为0）。｜二、每1点浪涌，结算时输赢倍数+1。｜三、当全场浪涌累计达到4点，进入“浪潮模式”，结算时倍数再+1。｜四、规则内置可食替｜规则提供：b站up大理石狐自恧")),
            new CreateRoomRuleTextConfig("qingque", "青雀",
                new CreateRoomSubRuleTextConfig("qingque/standard", "青雀", "青雀是由莫莫柴编写的一款麻雀规则，旨在寻求一种在传统麻将行牌规则框架内的做大、抢和、兜牌防守三者平衡的麻雀游戏，同时试图为各类和牌提供基于美感和难度评估的赋分参照；如在测试中发现设计问题或有任何建议，可以联系规则制定人莫莫柴Q1107574，提交bug可在群906497522提交")),
            new CreateRoomRuleTextConfig("sichuan", "四川麻将",
                new CreateRoomSubRuleTextConfig("sichuan/standard", "四川麻将(血战到底)", "四川麻将（血战到底）")),
            new CreateRoomRuleTextConfig("changsha", "长沙麻将",
                new CreateRoomSubRuleTextConfig("changsha/classic_double_bird", "长沙麻将(经典双鸟)", "长沙麻将经典双鸟规则：108张数牌，可吃上家牌，258将小胡，大胡可叠加，和牌后翻两只鸟并按座位中鸟加倍。")),
            new CreateRoomRuleTextConfig("taiwan", "台湾麻将",
                new CreateRoomSubRuleTextConfig("taiwan/standard", "台湾麻将", "台湾麻将：使用144张牌与16张手牌，按台计分，支持公开报听、食替限制与八仙过海等规则。具体流程与台表可在馆规设置中选择。")),
            new CreateRoomRuleTextConfig("classical", "古典麻将",
                new CreateRoomSubRuleTextConfig("classical/standard", "古典麻雀", "本规则为根据《绘图麻雀牌谱》《想定宁波规则》等书籍文献资料汇总而成的，试图还原1920年代左右或以前的早期麻将样貌的麻将规则。相比现代规则，古典麻雀有番种体系简单、重刻杠幺九、未和牌家计分等特点，具有独特风味。")),
            new CreateRoomRuleTextConfig("jiandan", "南雀",
                new CreateRoomSubRuleTextConfig("jiandan/standard", "南雀", "南雀规则由南瓜饼编写，是一个正在测试的规则，目标是在新手易上手与竞技策略深度之间取得平衡。无起和限制。当前版本固定采用一人和牌即止。标准规则将采用三人和牌（血战到底），正在开发中。")),
            new CreateRoomRuleTextConfig("hongque", "虹雀",
                new CreateRoomSubRuleTextConfig("hongque/v1.6", "虹雀", "虹雀是由Null设计的一款以彩虹为主题的拉密类桌游，使用十四种花色、九种数字各一张的麻将牌，最先将手牌全部组成顺子或刻子的玩家赢得一局。牌组的种类千变万化，各种起手都存在无限的可能。游戏尚在测试阶段，如对本规则感兴趣或有任何建议都可以添加虹雀官方Q群497685219一同交流。")),
        };

    private static readonly Dictionary<string, CreateRoomRuleTextConfig> RulesByKey = BuildRulesByKey();

    public static CreateRoomRuleTextConfig GetRule(string rule) {
        return rule != null && RulesByKey.TryGetValue(rule, out CreateRoomRuleTextConfig config)
            ? config
            : Rules[0];
    }

    public static CreateRoomRuleTextConfig GetRule(int index) {
        return Rules[index >= 0 && index < Rules.Count ? index : 0];
    }

    private static Dictionary<string, CreateRoomRuleTextConfig> BuildRulesByKey() {
        var result = new Dictionary<string, CreateRoomRuleTextConfig>();
        foreach (CreateRoomRuleTextConfig rule in Rules) result.Add(rule.Rule, rule);
        return result;
    }
}

/// <summary>各子规则说明文案。保留该入口供非建房界面复用。</summary>
public static class SubRuleDescriptionDictionary {
    public static readonly Dictionary<string, string> Descriptions = BuildDescriptions();

    public static string GetDescription(string subRule) {
        return subRule != null && Descriptions.TryGetValue(subRule, out string desc) ? desc : "";
    }

    private static Dictionary<string, string> BuildDescriptions() {
        var result = new Dictionary<string, string>();
        foreach (CreateRoomRuleTextConfig rule in CreateRoomRuleTextConfigCatalog.Rules) {
            foreach (CreateRoomSubRuleTextConfig subRule in rule.SubRules) {
                result.Add(subRule.Key, subRule.Description);
            }
        }
        return result;
    }
}
