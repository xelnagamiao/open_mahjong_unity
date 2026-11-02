using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class DuocaiRuleBootstrap {
    internal const string Description="夺彩麻将，由自恧发起，和雀庄的小伙伴们共同打磨的一款麻将游戏子规则，X计划的开山之作。";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Register() {
        RuleNameDictionary.WholeName["duocai"]="夺彩麻将";RuleNameDictionary.ShortName["duocai"]="夺彩";
        foreach(string variant in new[]{"guobiao","riichi"}) {
            RuleNameDictionary.WholeName["duocai/"+variant]="夺彩麻将 · "+(variant=="guobiao"?"国标":"立直");
            RuleNameDictionary.ShortName["duocai/"+variant]="夺彩"+(variant=="guobiao"?"国标":"立直");
        }
        RuleRegistry.Register(new RuleManifest {
            RuleId="duocai",DefaultSubRule="duocai/guobiao",DisplayName="夺彩麻将",LobbyName="夺彩麻将",LobbyOrder=26,
            LobbySubRules=new[]{new RuleLobbySubRule("duocai/guobiao","国标",Description),new RuleLobbySubRule("duocai/riichi","立直",Description)},
            BuildRoom=DuocaiRoomBuilder.Build,GameStateFactory=()=>new DuocaiGameState(),OutboundChannel="duocai",
            ReplayFactory=record=>record.CreateDuocaiReplayModule(),
            AdjustHepaiPresentation=(request,robbed)=>{if(robbed){request.WinTileMode=HepaiWinTilePresentMode.RonInstantThenPause;request.RestoreRecordHandFromSnapshot=true;request.IsQianggang=false;}},
            CreateRoomDefaults=new Dictionary<string,object>{{CreateRoomKeys.RoundTimer,3},{CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true},{CreateRoomKeys.CountTips,false},{CreateRoomKeys.PointerTips,true},{CreateRoomKeys.Password,false},
                {CreateRoomKeys.RandomSeed,false},{CreateRoomKeys.TouristLimit,false},{CreateRoomKeys.AllowSpectator,true}},
            OpeningHandHasDrawSlot=info=>info?.duocai_info?.self_has_draw_slot==true,
            Tingpai=q=>GameRecordManager.Instance?.ShouldShowRecordTips()==true?GameRecordManager.Instance.DuocaiWaiting(q):DuocaiGameState.Active?.Hints.Waiting(q)??new HashSet<int>(),
            DescribeWaitingTile=q=>q.Record!=null?GameRecordManager.Instance?.DescribeDuocaiWaiting(q):DuocaiGameState.Active?.DescribeWaiting(q),
            TileBadgeText=DuocaiTileVisual.Badge,PrepareHandDisplay=info=>DuocaiTableIndicators.Refresh(info?.duocai_info,info?.players_info),
            NormalizeTileId=tile=>tile,TileCopies=DuocaiTiles.Supply,
            RecordVisibleMeldTiles=masks=>masks?.Where(mask=>mask!=null).SelectMany(mask=>mask.Where((tile,index)=>index%2==1&&tile>0))??Enumerable.Empty<int>(),
            LiveVisibleIndicators=()=>DuocaiTableIndicators.DisplayedInfo?.indicators??Array.Empty<int>(),
            FanNameText=(_,label)=>FanName(label),FanValueText=FanValue,
            SettlementTotal=q=>new SettlementTotalDisplay{FanText=$"和牌 {q.HuScore}点",ScoreText=q.WinnerPointDelta.HasValue?$"{q.WinnerPointDelta.Value:+#;-#;0}点":""},
            ScoreboardFanText=q=>$"{q.HuScore}点",RoundIndicators=info=>DuocaiTableIndicators.FieldState(info?.duocai_info??DuocaiTableIndicators.DisplayedInfo),
            DefaultHepaiLimit=8,HasFlowerReplacement=false,PeekAnkan=true,RonWinTileTravelsFromRiver=true,ShowsRonDangerHints=false,RulebookKey="duocai"
        });
        ActionWords.Register(new ActionWordSpec{Word="duocai_group:",ResolveKind=word=>word.EndsWith("chi_left")?ActionWordKind.Chi:word.EndsWith("peng")?ActionWordKind.Peng:word.EndsWith(":gang")?ActionWordKind.MingGang:ActionWordKind.Other,
            DisplayPriority=30,Label=word=>Caption(word.Substring("duocai_group:".Length)),Expand=word=>DuocaiGameState.Active?.Candidates(word)??Array.Empty<ActionCandidate>()});
        ActionWords.Register(new ActionWordSpec{Word="duocai_choice:",Label=_=>"选择"});
        ActionWords.Register(new ActionWordSpec{Word="declare_kan",Label=_=>"杠"});
        ActionWords.Register(new ActionWordSpec{Word="rob_kan",Label=_=>"抢杠"});
    }
    private static string Caption(string word)=>word=="chi_left"?"吃":word=="peng"?"碰":word=="angang"?"暗杠":word=="jiagang"?"加杠":"杠";
    private static string FanName(string label) {
        int split=label.LastIndexOf('（');
        return split>=0&&label.EndsWith("番）")?label.Substring(0,split):label;
    }
    private static string FanValue(string subRule,string label) {
        int split=label.LastIndexOf('（');
        if(split>=0&&label.EndsWith("番）")) {
            string value=label.Substring(split+1,label.Length-split-2);
            if(subRule=="duocai/riichi"&&int.TryParse(value.TrimEnd('番'),out int fan)) {
                if(label.StartsWith("纯赖子"))return $"{fan/13}倍役满";
                if(RiichiFanText.FanToDisplayRiichi.TryGetValue(FanName(label),out string display)&&display.Contains("役满"))
                    return fan==13?"役满":$"{fan/13}倍役满";
            }
            return value;
        }
        return subRule=="duocai/riichi"?RiichiFanText.FanValue(subRule,label):GuobiaoFanText.FanValue("guobiao/standard",label);
    }
}
