using System;
using System.Collections.Generic;
using UnityEngine;

internal static class JiujiangRuleBootstrap {
    public const string Description = "九江麻将是九江地区一个没落的麻将规则，目前九江地区通行红中麻将，本规则是芋蔗凭借小时候的记忆和一定程度上的改良复现的一种失传规则，如发现任何问题或者能够提供九江麻将的资料的玩家可以联系Q2264033554";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleNameDictionary.WholeName[JiujiangGameState.RuleId] = "九江麻将";
        RuleNameDictionary.WholeName[JiujiangGameState.SubRule] = "九江麻将";
        RuleNameDictionary.ShortName[JiujiangGameState.RuleId] = "九江";
        RuleNameDictionary.ShortName[JiujiangGameState.SubRule] = "九江";
        RuleRegistry.Register(new RuleManifest {
            ReplayFactory = record => record.CreateJiujiangReplayModule(),
            BuildRoom = JiujiangRoomBuilder.Build,
            RuleId=JiujiangGameState.RuleId,DefaultSubRule=JiujiangGameState.SubRule,
            DisplayName="九江麻将",LobbyName="九江麻将",LobbyOrder=17,OutboundChannel="jiujiang",
            LobbySubRules=new[]{new RuleLobbySubRule(JiujiangGameState.SubRule,"标准",Description)},
            CreateRoomDefaults=new Dictionary<string,object> {
                {CreateRoomKeys.SubRule,0},{CreateRoomKeys.GameRound,4},{CreateRoomKeys.RoundTimer,3},{CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true},{CreateRoomKeys.CountTips,false},{CreateRoomKeys.PointerTips,true},
                {CreateRoomKeys.Password,false},{CreateRoomKeys.RandomSeed,false},{CreateRoomKeys.TouristLimit,false},{CreateRoomKeys.AllowSpectator,true},
            },
            GameStateFactory=()=>new JiujiangGameState(),
            OpeningHandHasDrawSlot=info=>info?.jiujiang_info?.self_has_draw_slot==true,
            Tingpai=q=>GameRecordManager.Instance?.ShouldShowRecordTips()==true
                ? GameRecordManager.Instance.JiujiangWaiting(q) : JiujiangGameState.Active?.Hints.Waiting(q) ?? new HashSet<int>(),
            DescribeWaitingTile=q=>q.Record!=null ? GameRecordManager.Instance?.DescribeJiujiangWaiting(q) : JiujiangGameState.Active?.DescribeWaiting(q),
            FanNameText=(rule,name)=>FanName(name),FanValueText=(rule,name)=>FanValue(name),
            SettlementTotal=q=>new SettlementTotalDisplay {FanText=$"{q.HuScore}分",ScoreText=""},
            ScoreboardFanText=q=>$"{q.HuScore}分",
            RoundName=r=>$"第{r}副",RoundStatusText=q=>$"第{q.current_round}副",MaxRoundText=r=>$"{r*4}副",
            ActionCaption=a=>a=="riichi"||a=="riichi_cut" ? "报听" : a=="riichi_cut_cancel" ? "取消报听" : a=="buhua" ? "补发财" : null,
            ActionVoice=a=>a=="riichi"||a=="riichi_cut" ? "ting" : a=="hu_self" ? "hu" : null,
            VisibleMeldTiles=(code,self)=>!self && code?.StartsWith("G")==true ? Array.Empty<int>() : null,
            HasFlowerReplacement=true,PeekAnkan=false,PublicReadyStateReplay=true,
            SupportsRobbedAddedKongSource=true,RonWinTileTravelsFromRiver=true,RecordDangerUsesWaitHint=true,
            DefaultHepaiLimit=1,RulebookKey="jiujiang",
        });
    }
    internal static string FanName(string name) {
        int start=name?.LastIndexOf('（') ?? -1;
        return start<0 ? name : name.Substring(0,start);
    }
    internal static string FanValue(string name) {
        int start=name?.LastIndexOf('（') ?? -1;
        return start>=0 && name.EndsWith("分）") ? name.Substring(start+1,name.Length-start-2) : "";
    }
}
