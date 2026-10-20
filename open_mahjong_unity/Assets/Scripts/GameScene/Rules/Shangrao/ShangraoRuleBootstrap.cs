using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class ShangraoRuleBootstrap {
    public const string Description = "上饶麻将是流行于江西上饶及周边的玩法，使用136张麻将牌（无花牌），只能自摸。除了基本的屁和牌型，还有碰碰胡、七对、九幺和十三烂特色牌型。宝牌（即“癞子”）为核心特色，单宝仅可作将，多余宝才能作万能牌，打出宝牌（“飞宝”）和牌可翻倍，因此又称为“飞宝麻将”。特色牌型和翻倍机制配合只能自摸的规则，使牌局兼具趣味性和策略性。";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Register() {
        RuleNameDictionary.WholeName[ShangraoGameState.RuleId] = "上饶麻将";
        RuleNameDictionary.WholeName[ShangraoGameState.SubRule] = "上饶麻将（飞宝）";
        RuleNameDictionary.ShortName[ShangraoGameState.RuleId] = "上饶";
        RuleNameDictionary.ShortName[ShangraoGameState.SubRule] = "上饶";
        RuleRegistry.Register(new RuleManifest {
            RuleId=ShangraoGameState.RuleId, DefaultSubRule=ShangraoGameState.SubRule,
            DisplayName="上饶麻将", LobbyName="上饶麻将", LobbyOrder=25, LobbyHasDetailedConfig=true,
            LobbySubRules=new[]{new RuleLobbySubRule(ShangraoGameState.SubRule,"飞宝麻将",Description)},
            BuildRoom=ShangraoRoomBuilder.Build, ReplayFactory=record=>record.CreateShangraoReplayModule(),
            GameStateFactory=()=>new ShangraoGameState(), OutboundChannel="shangrao",
            CreateRoomDefaults=new Dictionary<string,object> {
                {CreateRoomKeys.GameRound,4},{CreateRoomKeys.RoundTimer,3},{CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true},{CreateRoomKeys.CountTips,false},{CreateRoomKeys.PointerTips,true},
                {CreateRoomKeys.Password,false},{CreateRoomKeys.RandomSeed,false},
                {CreateRoomKeys.TouristLimit,false},{CreateRoomKeys.AllowSpectator,true},
            },
            OpeningHandHasDrawSlot=info=>info?.shangrao_info?.self_has_draw_slot==true,
            Tingpai=q=>GameRecordManager.Instance?.ShouldShowRecordTips()==true
                ? GameRecordManager.Instance.ShangraoWaiting(q) : ShangraoGameState.Active?.Hints.Waiting(q) ?? new HashSet<int>(),
            DescribeWaitingTile=q=>q.Record!=null ? GameRecordManager.Instance?.DescribeShangraoWaiting(q) : ShangraoGameState.Active?.DescribeWaiting(q),
            TileBadgeText=ShangraoTableIndicators.Badge,
            VisibleMeldTiles=(code,self)=>code?.StartsWith("G")==true&&int.TryParse(code.Substring(1),out int tile)?Enumerable.Repeat(tile,4).ToArray():null,
            HandJokerTiles=_=>ShangraoTableIndicators.DisplayedInfo is ShangraoInfo info ? new[]{info.bao}:Array.Empty<int>(),
            PrepareHandDisplay=info=>ShangraoTableIndicators.Refresh(info?.shangrao_info),
            RecordHandJokerTiles=round=>round?.shangrao is ShangraoInfo info?new[]{info.bao}:Array.Empty<int>(),
            LiveVisibleIndicators=()=>ShangraoTableIndicators.VisibleTiles,
            FanNameText=(rule,label)=>ShangraoText.FanName(label), FanValueText=(rule,label)=>ShangraoText.FanValue(label),
            SettlementTotal=q=>new SettlementTotalDisplay {FanText=$"和牌 {q.HuScore}分",ScoreText=q.WinnerPointDelta.HasValue?$"{q.WinnerPointDelta.Value:+#;-#;0}分":""},
            SettlementFootnote=q=>"本副净分包含和牌、杠分及四宝奖励", ScoreboardFanText=q=>$"{q.HuScore}分",
            ActionVoice=word=>word=="hu_self"?"hu":null, RoundName=round=>$"第{round}副", MaxRoundText=round=>$"{round*4}副",
            RoundIndicators=info=>ShangraoTableIndicators.FieldState(info?.shangrao_info??ShangraoTableIndicators.DisplayedInfo),
            DefaultHepaiLimit=1, HasFlowerReplacement=false, PeekAnkan=true, ShowsRonDangerHints=false,
            ReplayConcealedKongMask=(rule,tiles)=>tiles.SelectMany(tile=>new[]{0,tile}).ToArray(),
            RulebookKey="shangrao",
        });
        ActionWords.Register(new ActionWordSpec {Word="shangrao_payment",Kind=ActionWordKind.Other});
    }
}
public static class ShangraoText {
    public static string FanName(string label) {var p=label?.Split('|');return p?.Length>=4&&p[0]=="SR"?p[3]:label;}
    public static string FanValue(string label) {var p=label?.Split('|');return p?.Length>=4&&p[0]=="SR"?p[1]+p[2]:"";}
}
