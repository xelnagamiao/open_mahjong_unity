using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class NanjingRuleBootstrap {
    public const string Description="MIL 南京麻将（推广）2024：144张，中发白及八花须补花；不吃牌，四花或非偶然番起和，底分10分、120分封顶，轮庄，含花杠、跟牌、承包和外包。";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        foreach(string key in new[]{NanjingGameState.RuleId,NanjingGameState.SubRule}) {
            RuleNameDictionary.WholeName[key]="南京麻将";RuleNameDictionary.ShortName[key]="南京";
        }
        RuleRegistry.Register(new RuleManifest {
            RuleId=NanjingGameState.RuleId,DefaultSubRule=NanjingGameState.SubRule,
            DisplayName="南京麻将",LobbyName="南京麻将",LobbyOrder=19,OutboundChannel="nanjing",
            LobbySubRules=new[]{new RuleLobbySubRule(NanjingGameState.SubRule,"MIL 2024",Description)},
            BuildRoom=NanjingRoomBuilder.Build,ReplayFactory=record=>record.CreateNanjingReplayModule(),
            CreateRoomDefaults=new Dictionary<string,object> {
                {CreateRoomKeys.SubRule,0},{CreateRoomKeys.GameRound,4},{CreateRoomKeys.RoundTimer,3},{CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true},{CreateRoomKeys.CountTips,false},{CreateRoomKeys.PointerTips,true},
                {CreateRoomKeys.Password,false},{CreateRoomKeys.RandomSeed,false},{CreateRoomKeys.TouristLimit,false},{CreateRoomKeys.AllowSpectator,true},
            },
            GameStateFactory=()=>new NanjingGameState(),
            OpeningHandHasDrawSlot=info=>info?.nanjing_info?.self_has_draw_slot==true,
            Tingpai=q=>GameRecordManager.Instance?.ShouldShowRecordTips()==true
                ? GameRecordManager.Instance.NanjingWaiting(q) : NanjingGameState.Active?.Hints.Waiting(q)??new HashSet<int>(),
            DescribeWaitingTile=q=>q.Record!=null ? GameRecordManager.Instance?.DescribeNanjingWaiting(q) : NanjingGameState.Active?.DescribeWaiting(q),
            FanNameText=(rule,label)=>FanText(label,false),FanValueText=(rule,label)=>FanText(label,true),
            SettlementTotal=q=>new SettlementTotalDisplay {FanText=$"和牌点数 {q.HuScore}分",ScoreText=q.WinnerPointDelta.HasValue?$"{q.WinnerPointDelta.Value:+#;-#;0}分":""},
            ScoreboardFanText=q=>$"{q.HuScore}分",
            RoundName=r=>$"第{r}副",RoundStatusText=q=>$"第{q.current_round}副",MaxRoundText=r=>$"{r*4}副",
            HasFlowerReplacement=true,ReplacementFromTailEnd=true,PeekAnkan=true,
            ReplayConcealedKongMask=(rule,tiles)=>new[]{2,tiles[0],0,tiles[1],0,tiles[2],2,tiles[3]},
            SupportsRobbedAddedKongSource=true,RonWinTileTravelsFromRiver=true,RecordDangerUsesWaitHint=true,
            RecordFlowerWin=tick=>tick!=null && tick.Count>3 && tick[3].Contains("承包自动和"),
            SettlementHasNoWinTile=labels=>labels?.Any(x=>x.Contains("承包自动和"))==true,
            DefaultHepaiLimit=0,RulebookKey="nanjing",
        });
        ActionWords.Register(new ActionWordSpec {Word="nanjing_score",Kind=ActionWordKind.Other,Label=_=>""});
    }
    internal static string FanText(string label,bool value) {
        string[] fields=label?.Split('|');
        return fields?.Length==3 && fields[0]=="NJ" ? fields[value?2:1]+(value?"分":"") : value?"":label;
    }
}
