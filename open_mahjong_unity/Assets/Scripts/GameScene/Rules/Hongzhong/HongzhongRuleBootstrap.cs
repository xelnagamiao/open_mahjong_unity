using System;
using System.Collections.Generic;
using UnityEngine;

internal static class HongzhongRuleBootstrap {
    public const string Description = "MIL《红中麻将（推广）竞赛规则（试行2024版）》：112张，十三张手牌，红中为万能牌。仅自摸，不吃；红中不可碰杠。最高番替代，4番封顶；和牌后扎2鸟，159及红中中鸟，每中一鸟三家各付1分。杠分即时结算，流局退杠；胜者下局坐庄。";
    private static readonly Dictionary<string,int> Fans = new Dictionary<string,int> {
        {"平和",0},{"无红中",1},{"大对子",1},{"一条龙",1},
        {"杠上花",1},{"七对子",2},{"清一色",2},{"龙七对",3},
    };
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleNameDictionary.WholeName[HongzhongGameState.RuleId] = "红中麻将";
        RuleNameDictionary.WholeName[HongzhongGameState.SubRule] = "红中麻将（MIL 2024）";
        RuleNameDictionary.ShortName[HongzhongGameState.RuleId] = "红中";
        RuleNameDictionary.ShortName[HongzhongGameState.SubRule] = "红中";
        RuleRegistry.Register(new RuleManifest {
            ReplayFactory = record => record.CreateHongzhongReplayModule(),
            BuildRoom = HongzhongRoomBuilder.Build,
            RuleId = HongzhongGameState.RuleId, DefaultSubRule = HongzhongGameState.SubRule,
            DisplayName = "红中麻将", LobbyName = "红中麻将", LobbyOrder = 14,
            LobbySubRules = new[] { new RuleLobbySubRule(HongzhongGameState.SubRule,"MIL 2024",Description) },
            CreateRoomDefaults = new Dictionary<string,object> {
                {CreateRoomKeys.SubRule,0},{CreateRoomKeys.GameRound,4},{CreateRoomKeys.RoundTimer,3},{CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true},{CreateRoomKeys.CountTips,false},{CreateRoomKeys.PointerTips,true},
                {CreateRoomKeys.Password,false},{CreateRoomKeys.RandomSeed,false},{CreateRoomKeys.TouristLimit,false},{CreateRoomKeys.AllowSpectator,true},
            },
            GameStateFactory = () => new HongzhongGameState(), OutboundChannel = "hongzhong",
            OpeningHandHasDrawSlot = info => info?.hongzhong_info?.self_has_draw_slot == true,
            Tingpai = q => GameRecordManager.Instance?.ShouldShowRecordTips() == true
                ? GameRecordManager.Instance.HongzhongWaiting(q) : HongzhongGameState.Active?.Hints.Waiting(q) ?? new HashSet<int>(),
            DescribeWaitingTile = q => q.Record != null
                ? GameRecordManager.Instance?.DescribeHongzhongWaiting(q) : HongzhongGameState.Active?.DescribeWaiting(q),
            TileBadgeText = tile => tile == 45 ? "癞" : null,
            FanValueText = (rule,name) => Fans.TryGetValue(name,out int value) ? $"{value}番" : "",
            SettlementTotal = q => new SettlementTotalDisplay { FanText=$"{q.HuScore}番",ScoreText=$"基本分 {1 << Math.Min(4,q.HuScore)}分" },
            ScoreboardFanText = q => $"{q.HuScore}番",
            ActionVoice = word => word == "hu_self" ? "hu" : null,
            RoundName = round => $"第{round}局", MaxRoundText = rounds => $"{rounds*4}局",
            RoundSupplementText = info => "<size=18>仅自摸</size>",
            HasFlowerReplacement = false, DefaultHepaiLimit = 0, PeekAnkan = true, ShowsRonDangerHints = false,
            // 默认MCR补牌顺序就是末墩上牌、下牌交替。
            RulebookKey = "hongzhong",
        });
    }
}
