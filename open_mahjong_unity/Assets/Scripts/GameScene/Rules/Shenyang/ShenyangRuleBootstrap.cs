using System;
using System.Collections.Generic;
using UnityEngine;

internal static class ShenyangRuleBootstrap {
    public const string RuleId="shenyang", SubRule="shenyang/mil2025";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Register() {
        RuleNameDictionary.WholeName[RuleId]="沈阳麻将";
        RuleNameDictionary.WholeName[SubRule]="沈阳麻将（MIL 2025）";
        RuleNameDictionary.ShortName[RuleId]="沈阳";
        RuleNameDictionary.ShortName[SubRule]="沈阳";
        RuleRegistry.Register(new RuleManifest {
            RuleId=RuleId, DefaultSubRule=SubRule, DisplayName="沈阳麻将", LobbyOrder=21, OutboundChannel=RuleId,
            BuildRoom=ShenyangRoomBuilder.Build, GameStateFactory=()=>new ShenyangGameState(),
            ReplayFactory=record=>record.CreateShenyangReplayModule(),
            AdjustHepaiPresentation=(request,robbed)=> {
                if(!robbed) return;
                request.WinTileMode=HepaiWinTilePresentMode.RonInstantThenPause;
                request.RestoreRecordHandFromSnapshot=true;request.IsQianggang=false;
            },
            LobbySubRules=new[]{new RuleLobbySubRule(SubRule,"MIL 2025","136张、十三张手牌，无花无癞子。先碰或明杠开门才能吃，闭门可报听；基本和须有幺九、刻杠或箭牌将，并具三门或清一色，七对不限。喜杠不补牌，旋风杠补牌；杠后补牌公示筒子过蛋每家5分，一条每家10分。末四张只和不打，6番封顶，和者坐庄。线上使用房间局时与步时。")},
            CreateRoomDefaults=new Dictionary<string,object> {
                {CreateRoomKeys.SubRule,0},{CreateRoomKeys.GameRound,4},{CreateRoomKeys.RoundTimer,3},{CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true},{CreateRoomKeys.CountTips,false},{CreateRoomKeys.PointerTips,true},
                {CreateRoomKeys.Password,false},{CreateRoomKeys.RandomSeed,false},{CreateRoomKeys.TouristLimit,false},{CreateRoomKeys.AllowSpectator,true}},
            OpeningHandHasDrawSlot=info=>info?.shenyang?.self_has_draw_slot==true,
            Tingpai=q=>GameRecordManager.Instance?.ShouldShowRecordTips()==true ? GameRecordManager.Instance.ShenyangWaiting(q) : ShenyangGameState.Active?.Hints.Waiting(q) ?? new HashSet<int>(),
            DescribeWaitingTile=q=>q.Record!=null ? GameRecordManager.Instance?.DescribeShenyangWaiting(q) : ShenyangGameState.Active?.Hints.Describe(q),
            VisibleMeldTiles=(code,self)=>!self && code?.StartsWith("G")==true ? Array.Empty<int>() : SpecialTiles(code),
            FanValueText=(r,n)=>FanValue(n), ScoreboardFanText=q=>$"{q.HuScore}番（逐家计分）",
            SettlementTotal=q=>new SettlementTotalDisplay{FanText=$"最高{q.HuScore}番",ScoreText=q.WinnerPointDelta.HasValue?$"{q.WinnerPointDelta.Value}分":"6番封顶"},
            RoundName=r=>$"第{r}副",RoundStatusText=q=>$"第{q.current_round}副",MaxRoundText=r=>$"{r*4}副",
            ActionCaption=a=>a=="riichi" || a=="riichi_cut" ? "报听" : a=="riichi_cut_cancel" ? "取消报听" : null,
            ActionVoice=a=>a=="riichi" || a=="riichi_cut" ? "ting" : a=="hu_self" ? "hu" : a=="sy_xi" || a=="sy_wind" ? "gang" : null,
            HasFlowerReplacement=false, FaceDownAnkan=true, PublicReadyStateReplay=true,
            RonWinTileTravelsFromRiver=true, ReplacementFromTailEnd=true, DefaultHepaiLimit=0, RecordDangerUsesWaitHint=true
        });
        ActionWords.Register(new ActionWordSpec{Word="sy_xi",BlocksAutoCut=true,Label=_=>"喜杠"});
        ActionWords.Register(new ActionWordSpec{Word="sy_wind",BlocksAutoCut=true,Label=_=>"旋风杠"});
        ActionWords.Register(new ActionWordSpec{Word="sy_reveal",BlocksAutoCut=true,Label=_=>"公示过蛋"});
        ActionWords.Register(new ActionWordSpec{Word="sy_hide",BlocksAutoCut=true,Label=_=>"不公示"});
        ActionWords.Register(new ActionWordSpec{Word="sy_pass",Kind=ActionWordKind.Pass,Label=_=>"过"});
        ActionWords.Register(new ActionWordSpec{Word="sy_state",Kind=ActionWordKind.Other,Label=_=>""});
    }
    public static int[] SpecialTiles(string code) => code=="Ydragon" ? new[]{45,46,47} : code=="Ywind" ? new[]{41,42,43,44} : null;
    public static string FanValue(string name) {
        switch(name) {
            case "手把一":case "抢杠和":case "杠流":case "三家闭":case "暗杠":case "中/发/白明杠":case "喜杠":case "旋风杠":case "和中/发/白":return "2番";
            case "飘和":return "3番";
            case "中/发/白暗杠":case "清一色":case "闭大四":case "七对":return "4番";
            default:return "1番";
        }
    }
}
