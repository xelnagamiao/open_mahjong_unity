using System.Collections.Generic;
using UnityEngine;

internal static class TuidaoRuleBootstrap {
    internal const string RuleId = "guangdong";
    internal const string SubRule = "guangdong/tuidao_mil2024";
    private static readonly Dictionary<string, int> Fans = new Dictionary<string, int> {
        { "门清", 2 }, { "平和", 2 }, { "断幺", 2 }, { "报听", 2 },
        { "碰碰和", 6 }, { "全带幺", 6 }, { "大吊车", 6 }, { "混一色", 6 },
        { "抢杠", 8 }, { "海底", 8 }, { "杠上开花", 8 }, { "全不靠", 8 }, { "清龙", 8 },
        { "天听", 16 }, { "七对", 16 }, { "清一色", 16 }, { "豪华七对", 24 },
        { "天和", 32 }, { "十三幺", 32 }, { "大三元", 32 }, { "字一色", 32 },
        { "四暗刻", 32 }, { "大四喜", 32 },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleNameDictionary.WholeName[GuangdongMilRules.SubRule] = "广东麻将（MIL 2023，花鬼）";
        RuleNameDictionary.ShortName[GuangdongMilRules.SubRule] = "广东花鬼";
        RuleRegistry.Register(new RuleManifest {
            RuleId = RuleId, DefaultSubRule = SubRule, DisplayName = "广东麻将", LobbyOrder = 15,
            LobbySubRules = new[] { new RuleLobbySubRule(SubRule, "MIL 2024（推倒和）",
                "MIL《推倒和麻将（推广）竞赛规则（试行2024版）》：136张、十三张手牌，无花无癞子。可以吃碰杠，报听可选，头跳；番数相加32封顶，另加2底分，自摸三家付，点和一家付双份。杠分即时结算。战术鸣牌默认开启，有机器人时关闭；普通询问按房间步时加剩余局时，抢断再问独立5秒，允许升级申请。"), new RuleLobbySubRule(GuangdongMilRules.SubRule, "MIL 2023（花鬼）", GuangdongMilRules.Description) },
            CreateRoomDefaults = new Dictionary<string, object> {
                { CreateRoomKeys.SubRule, 0 }, { CreateRoomKeys.GameRound, 4 },
                { CreateRoomKeys.RoundTimer, 3 }, { CreateRoomKeys.StepTimer, 1 },
                { CreateRoomKeys.Tips, true }, { CreateRoomKeys.CountTips, false },
                { CreateRoomKeys.PointerTips, true }, { CreateRoomKeys.Password, false },
                { CreateRoomKeys.RandomSeed, false }, { CreateRoomKeys.TouristLimit, false },
                { CreateRoomKeys.AllowSpectator, true },
                { CreateRoomKeys.TacticalCall, true },
                { GuangdongMilRules.MinimumScoreKey, true },
            },
            GameStateFactory = () => new TuidaoGameState(), OutboundChannel = "guangdong",
            SubRuleGameStateFactory = subRule => GuangdongMilRules.IsMil(subRule) ? (IGameState)new GuangdongMilGameState() : new TuidaoGameState(),
            Tingpai = query => GuangdongMilRules.IsMil(query.SubRule) ? GuangdongServerTips.Waiting(query) : TuidaoHandCalculator.Waits(query.Hand, query.Melds),
            DescribeWaitingTile = query => GuangdongMilRules.IsMil(query.SubRule) ? GuangdongServerTips.Describe(query) : TuidaoHandCalculator.Describe(query),
            RoundName = round => $"第{round}局", MaxRoundText = rounds => $"{rounds * 4}局",
            RoundSupplementText = info => GuangdongMilRules.IsMil(info.sub_rule) ? "花鬼" : "无癞子",
            FanNameText = (rule, name) => GuangdongMilRules.IsMil(rule) ? GuangdongMilRules.FanName(name) : name,
            FanValueText = (rule, name) => GuangdongMilRules.IsMil(rule) ? GuangdongMilRules.FanValue(name) : Fans.TryGetValue(name, out int value) ? $"{value}番" : "",
            ScoreboardFanText = query => GuangdongMilRules.IsMil(query.Rule) ? $"基本分{query.HuScore}分" : $"{query.HuScore}番",
            SettlementTotal = query => GuangdongMilRules.IsMil(query.Rule) ? GuangdongMilRules.Total(query) : new SettlementTotalDisplay {
                FanText = $"{query.HuScore}番", ScoreText = $"{2 + query.HuScore}分"
            },
            SettlementFootnote = query => GuangdongMilRules.IsMil(query.Rule) ? "花鬼留手；奖马及杠分明细见花鬼账目。流局退杠。" : query.HuFan == null ? "荒庄：保留已成立的杠分。"
                : "基本分＝2＋番数（32封顶），杠分另计。\n自摸三家各付；点和一家付双份。",
            ActionCaption = word => word == "riichi" || word == "riichi_cut" ? "报听"
                : word == "riichi_cut_cancel" ? "取消报听" : null,
            ActionVoice = word => word == "riichi" || word == "riichi_cut" ? "baoting" : null,
            HasFlowerReplacement = false, DefaultHepaiLimit = 0,
            SupportsRobbedAddedKongSource = true, ReplacementFromTailEnd = true,
            PublicReadyStateReplay = true, PeekAnkan = true,
            RulebookPath = (subRule, detail) => "/rulebook/guangdong?sub_rule=" + System.Uri.EscapeDataString(GuangdongMilRules.IsMil(subRule) ? GuangdongMilRules.SubRule : SubRule),
        });
    }
}
