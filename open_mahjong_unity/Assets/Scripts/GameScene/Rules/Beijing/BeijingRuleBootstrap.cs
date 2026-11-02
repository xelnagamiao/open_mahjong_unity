using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class BeijingRuleBootstrap {
    public const string Description="北京麻将（吃碰提）是北京地区的民间麻将规则，其特点为有字牌、无花牌、有万能牌、非门前清状态不可点和，番种体系较为简单。本文参考网络资料与线下实践梳理北京麻将的基本玩法，体现北京麻将核心特征，便于各地麻将爱好者了解北京麻将主要框架。";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Register() {
        RuleNameDictionary.WholeName[BeijingGameState.RuleId]="北京麻将（吃碰提）";
        RuleNameDictionary.WholeName[BeijingGameState.SubRule]="北京麻将（吃碰提）";
        RuleNameDictionary.ShortName[BeijingGameState.RuleId]=RuleNameDictionary.ShortName[BeijingGameState.SubRule]="北京";
        RuleRegistry.Register(new RuleManifest {
            RuleId=BeijingGameState.RuleId,DefaultSubRule=BeijingGameState.SubRule,DisplayName="北京麻将（吃碰提）",LobbyName="北京麻将",LobbyOrder=27,LobbyHasDetailedConfig=true,
            LobbySubRules=new[]{new RuleLobbySubRule(BeijingGameState.SubRule,"吃碰提",Description)},
            BuildRoom=BeijingRoomBuilder.Build,ReplayFactory=record=>record.CreateBeijingReplayModule(),GameStateFactory=()=>new BeijingGameState(),OutboundChannel="beijing",
            CreateRoomDefaults=new Dictionary<string,object> {
                {CreateRoomKeys.GameRound,4},{CreateRoomKeys.RoundTimer,3},{CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true},{CreateRoomKeys.CountTips,false},{CreateRoomKeys.PointerTips,true},
                {CreateRoomKeys.Password,false},{CreateRoomKeys.RandomSeed,false},{CreateRoomKeys.TouristLimit,false},{CreateRoomKeys.AllowSpectator,true}
            },
            OpeningHandHasDrawSlot=info=>info?.beijing_info?.self_has_draw_slot==true,
            Tingpai=q=>GameRecordManager.Instance?.ShouldShowRecordTips()==true?GameRecordManager.Instance.BeijingWaiting(q):BeijingGameState.Active?.Hints.Waiting(q)??new HashSet<int>(),
            DescribeWaitingTile=q=>q.Record!=null?GameRecordManager.Instance?.DescribeBeijingWaiting(q):BeijingGameState.Active?.DescribeWaiting(q),
            TileBadgeText=BeijingTableIndicators.Badge,
            HandJokerTiles=_=>BeijingTableIndicators.DisplayedInfo is BeijingInfo info?new[]{info.hun}:Array.Empty<int>(),
            PrepareHandDisplay=info=>BeijingTableIndicators.Refresh(info?.beijing_info),
            RecordHandJokerTiles=round=>round?.beijing is BeijingInfo info?new[]{info.hun}:Array.Empty<int>(),
            LiveVisibleIndicators=()=>BeijingTableIndicators.VisibleTiles,
            FanNameText=(rule,label)=>BeijingText.FanName(label),FanValueText=(rule,label)=>BeijingText.FanValue(label),
            SettlementTotal=q=>new SettlementTotalDisplay{FanText=$"基本分 {q.HuScore}点",ScoreText=q.WinnerPointDelta.HasValue?$"{q.WinnerPointDelta.Value:+#;-#;0}点":""},
            ScoreboardFanText=q=>$"{q.HuScore}点",
            ActionVoice=word=>word=="hu_self"?"hu":word=="tianting"?"tianting":null,
            RoundIndicators=info=>BeijingTableIndicators.FieldState(info?.beijing_info??BeijingTableIndicators.DisplayedInfo),
            DefaultHepaiLimit=0,HasFlowerReplacement=false,PeekAnkan=true,ShowsRonDangerHints=false,RonWinTileTravelsFromRiver=true,
            AdjustHepaiPresentation=(request,robbed)=>{if(robbed){request.WinTileMode=HepaiWinTilePresentMode.RonInstantThenPause;request.RestoreRecordHandFromSnapshot=true;request.IsQianggang=false;request.DiscardPlayerPosition=null;}},
            ReplayConcealedKongMask=(rule,tiles)=>tiles.SelectMany(tile=>new[]{2,tile}).ToArray(),RulebookKey="beijing"
        });
        ActionWords.Register(new ActionWordSpec{Word="tianting",Kind=ActionWordKind.Other,BlocksAutoCut=true,Label=_=>"天听",DisplayPriority=40});
        ActionWords.Register(new ActionWordSpec{Word="hun_gang",Kind=ActionWordKind.Tsumo,Label=_=>"混儿杠",DisplayPriority=50});
        ActionWords.Register(new ActionWordSpec{Word="beijing_payment",Kind=ActionWordKind.Other});
    }
}
public static class BeijingText {
    public static string FanName(string label){var p=label?.Split('|');return p?.Length>=4&&p[0]=="BJ"?p[3]:label;}
    public static string FanValue(string label){var p=label?.Split('|');return p?.Length>=4&&p[0]=="BJ"?p[1]+p[2]:"";}
}
