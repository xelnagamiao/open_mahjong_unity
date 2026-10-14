using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class SingaporeRuleBootstrap {
    public const string Description = "新加坡四人麻将：依 SPGG 2024 年规则，13 张手牌、148 张牌（含花季与猫鼠鸡蜈蚣）、1 台起和、5 台封顶。花季婚配与杠即时支付，400 分起分；庄和连庄，流局无杠连庄。完整口径见规则书。";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleNameDictionary.WholeName[SingaporeGameState.RuleId] = "新加坡四人麻将";
        RuleNameDictionary.WholeName[SingaporeGameState.SubRule] = "新加坡 · SPGG 2024";
        RuleNameDictionary.ShortName[SingaporeGameState.RuleId] = "新加坡";
        RuleNameDictionary.ShortName[SingaporeGameState.SubRule] = "新加坡";
        RuleRegistry.Register(new RuleManifest {
            RuleId=SingaporeGameState.RuleId,DefaultSubRule=SingaporeGameState.SubRule,
            DisplayName="新加坡四人麻将",LobbyName="新加坡四人麻将",LobbyOrder=30,OutboundChannel="singapore",
            BuildRoom=SingaporeRoomBuilder.Build,ReplayFactory=r=>r.CreateSingaporeReplayModule(),
            LobbySubRules=new[]{new RuleLobbySubRule(SingaporeGameState.SubRule,"SPGG 2024",Description)},
            CreateRoomDefaults=new Dictionary<string,object> {
                {CreateRoomKeys.SubRule,0},{CreateRoomKeys.GameRound,1},{CreateRoomKeys.RoundTimer,3},{CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true},{CreateRoomKeys.CountTips,false},{CreateRoomKeys.PointerTips,true},
                {CreateRoomKeys.Password,false},{CreateRoomKeys.RandomSeed,false},{CreateRoomKeys.TouristLimit,false},{CreateRoomKeys.AllowSpectator,true},
            },
            GameStateFactory=()=>new SingaporeGameState(),
            OpeningHandHasDrawSlot=i=>i?.singapore_info?.self_has_draw_slot==true,
            Tingpai=q=>GameRecordManager.Instance?.ShouldShowRecordTips()==true
                ? GameRecordManager.Instance.SingaporeWaiting(q) : SingaporeGameState.Active?.Hints.Waiting(q) ?? new HashSet<int>(),
            DescribeWaitingTile=q=>q.Record!=null ? GameRecordManager.Instance?.DescribeSingaporeWaiting(q) : SingaporeGameState.Active?.Hints.Describe(q),
            FanNameText=(r,n)=>FanName(n),FanValueText=(r,n)=>FanValue(n),
            SettlementTotal=q=>new SettlementTotalDisplay {FanText=$"{q.HuScore}台",ScoreText=""},
            ScoreboardFanText=q=>$"{q.HuScore}台",
            SettlementFootnote=q=>SingaporeGameState.Active?.PaymentSummary(),
            RoundName=RoundTextDictionary.WindNumberRoundName,
            RoundSupplementText=q=>"1台起和·5台封顶",
            ActionCaption=a=>a=="buhua" ? "补花" : null,
            ActionVoice=a=>a=="hu_self" ? "hu" : null,
            VisibleMeldTiles=(code,self)=>!self && code?.StartsWith("G")==true ? Array.Empty<int>() : null,
            HasFlowerReplacement=true,PeekAnkan=false,ReplacementFromTailEnd=true,
            SupportsRobbedAddedKongSource=true,RonWinTileTravelsFromRiver=true,RecordDangerUsesWaitHint=true,
            RecordFlowerWin=tick=>tick!=null && tick.Count>3 && (tick[3].Contains("自动和") || tick[3].Contains("八花和")),
            SettlementHasNoWinTile=NoWinTile,
            HuPresentationAction=(word,fans,config)=>fans?.Any(x=>x.Contains("八花和"))==true ? "hu_flower" : word,
            DefaultHepaiLimit=1,RulebookKey="singapore",
        });
        ActionWords.Register(new ActionWordSpec {Word="singapore_payment",Kind=ActionWordKind.Other,Label=_=>""});
    }
    internal static bool NoWinTile(IList<string> labels) => labels?.Any(x=>x.Contains("自动和") || x.Contains("八花和"))==true;
    internal static string FanName(string name) {
        int start=name?.LastIndexOf('（')??-1;
        return start<0 ? name : name.Substring(0,start);
    }
    internal static string FanValue(string name) {
        int start=name?.LastIndexOf('（')??-1;
        return start>=0 && name.EndsWith("台）") ? name.Substring(start+1,name.Length-start-2) : "";
    }
}
