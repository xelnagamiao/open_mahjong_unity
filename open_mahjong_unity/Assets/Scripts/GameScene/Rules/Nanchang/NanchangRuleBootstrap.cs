using System;
using System.Collections.Generic;
using UnityEngine;

internal static class NanchangRuleBootstrap {
    public const string Description="MIL《南昌麻将（推广）竞赛规则（试行2026版）》四人单打：136张，翻正副精，精牌可作任意牌；可吃数牌、三种风牌或中发白。支持十三烂、七对、精吊、德国、德中德及冲关、霸王精。1番起和、6番封顶，精分、杠分、和牌分分别结算，流局也结算精分和杠分。";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Register() {
        RuleNameDictionary.WholeName[NanchangGameState.RuleId]="南昌麻将";
        RuleNameDictionary.WholeName[NanchangGameState.SubRule]="南昌麻将（MIL 2026）";
        RuleNameDictionary.ShortName[NanchangGameState.RuleId]="南昌";
        RuleNameDictionary.ShortName[NanchangGameState.SubRule]="南昌";
        RuleRegistry.Register(new RuleManifest {
            RuleId=NanchangGameState.RuleId,DefaultSubRule=NanchangGameState.SubRule,
            DisplayName="南昌麻将",LobbyName="南昌麻将",LobbyOrder=24,
            LobbySubRules=new[]{new RuleLobbySubRule(NanchangGameState.SubRule,"MIL 2026（单打）",Description)},
            BuildRoom=NanchangRoomBuilder.Build,ReplayFactory=record=>record.CreateNanchangReplayModule(),
            GameStateFactory=()=>new NanchangGameState(),OutboundChannel="nanchang",
            CreateRoomDefaults=new Dictionary<string,object> {
                {CreateRoomKeys.GameRound,4},{CreateRoomKeys.RoundTimer,3},{CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true},{CreateRoomKeys.CountTips,false},{CreateRoomKeys.PointerTips,true},
                {CreateRoomKeys.Password,false},{CreateRoomKeys.RandomSeed,false},
                {CreateRoomKeys.TouristLimit,false},{CreateRoomKeys.AllowSpectator,true},
            },
            OpeningHandHasDrawSlot=info=>info?.nanchang_info?.self_has_draw_slot==true,
            Tingpai=NanchangGameState.Waiting,DescribeWaitingTile=NanchangGameState.Describe,
            TileBadgeText=NanchangTableIndicators.Badge,VisibleMeldTiles=VisibleMeld,
            HandJokerTiles=_=>NanchangTableIndicators.DisplayedInfo is NanchangInfo info
                ? new[]{info.indicator,info.secondary} : Array.Empty<int>(),
            PrepareHandDisplay=info=>NanchangTableIndicators.Refresh(info?.nanchang_info),
            RecordHandJokerTiles=round=>round?.nanchang is NanchangInfo info
                ? new[]{info.indicator,info.secondary} : Array.Empty<int>(),
            FanNameText=(rule,label)=>NanchangText.FanName(label),FanValueText=(rule,label)=>NanchangText.FanValue(label),
            SettlementTotal=q=>new SettlementTotalDisplay {FanText=$"和牌 {q.HuScore}分",ScoreText=q.WinnerPointDelta.HasValue?$"{q.WinnerPointDelta.Value:+#;-#;0}分":""},
            SettlementFootnote=q=>"本副净分包含和牌、精分及杠分",ScoreboardFanText=q=>$"{q.HuScore}分",
            ActionVoice=word=>word=="hu_self"?"hu":null,RoundName=round=>$"第{round}副",MaxRoundText=round=>$"{round*4}副",
            RoundIndicators=info=>NanchangTableIndicators.FieldState(info?.nanchang_info??NanchangTableIndicators.DisplayedInfo),
            DefaultHepaiLimit=1,HasFlowerReplacement=false,RonWinTileTravelsFromRiver=true,
            ReplayConcealedKongMask=(rule,tiles)=>new[]{2,tiles[0],2,tiles[1],2,tiles[2],2,tiles[3]},
            RecordDangerUsesWaitHint=true,RulebookKey="nanchang",
        });
        ActionWords.Register(new ActionWordSpec {Word="nc_jingdiao",Kind=ActionWordKind.Other,BlocksAutoCut=true,Label=_=>"精吊"});
        ActionWords.Register(new ActionWordSpec {Word="nc_draw",Kind=ActionWordKind.Other,BlocksAutoCut=true,Label=_=>"摸牌"});
    }
    internal static int[] VisibleMeld(string code,bool self) {
        if(!self && code?.StartsWith("G")==true)return Array.Empty<int>();
        if(code?.Length==7 && code[0]=='s')return new[]{int.Parse(code.Substring(1,2)),int.Parse(code.Substring(3,2)),int.Parse(code.Substring(5,2))};
        return null;
    }
}
public static class NanchangText {
    public static string FanName(string label) {var p=label?.Split('|');return p?.Length>=4 && p[0]=="NC"?p[3]:label;}
    public static string FanValue(string label) {var p=label?.Split('|');return p?.Length>=4 && p[0]=="NC"?p[1]+p[2]:"";}
    public static string Tile(int tile)=>WenzhouText.Tile(tile);
}
