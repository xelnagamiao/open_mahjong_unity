using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class FuzhouRuleBootstrap {
    public const string Description="福州麻将是一款采用17张和牌机制的地方麻将规则，旨在寻求一种在传统麻将框架内、以开金为核心的做大、抢金、留墩防守三者平衡的博弈游戏，同时试图为三金倒、金龙、游金等特殊和牌提供基于难度和收益的赋分参照；如发现规则漏洞或有任何建议，可在群906497522提交。";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        foreach(string key in new[]{FuzhouGameState.RuleId,FuzhouGameState.SubRule}) {
            RuleNameDictionary.WholeName[key]="福州麻将";RuleNameDictionary.ShortName[key]="福州";
        }
        RuleRegistry.Register(new RuleManifest {
            RuleId=FuzhouGameState.RuleId,DefaultSubRule=FuzhouGameState.SubRule,
            DisplayName="福州麻将",LobbyName="福州麻将",LobbyOrder=29,OutboundChannel="fuzhou",
            LobbySubRules=new[]{new RuleLobbySubRule(FuzhouGameState.SubRule,"福州十六张",Description)},
            BuildRoom=FuzhouRoomBuilder.Build,ReplayFactory=record=>record.CreateFuzhouReplayModule(),
            CreateRoomDefaults=new Dictionary<string,object> {
                {CreateRoomKeys.SubRule,0},{CreateRoomKeys.GameRound,1},{CreateRoomKeys.RoundTimer,3},{CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true},{CreateRoomKeys.CountTips,false},{CreateRoomKeys.PointerTips,true},
                {CreateRoomKeys.Password,false},{CreateRoomKeys.RandomSeed,false},{CreateRoomKeys.TouristLimit,false},{CreateRoomKeys.AllowSpectator,true},
            },
            GameStateFactory=()=>new FuzhouGameState(),
            OpeningHandHasDrawSlot=info=>info?.fuzhou_info?.self_has_draw_slot==true,
            HandJokerTiles=_=>FuzhouTableIndicators.GoldTiles,
            RecordHandJokerTiles=round=>round?.fuzhou?.gold_tile>0?new[]{round.fuzhou.gold_tile}:Array.Empty<int>(),
            PrepareHandDisplay=info=>FuzhouTableIndicators.Refresh(info?.fuzhou_info),
            LiveVisibleIndicators=()=>FuzhouTableIndicators.DisplayedInfo?.indicator_claimed==false?FuzhouTableIndicators.GoldTiles:Array.Empty<int>(),
            TileBadgeText=tile=>FuzhouTableIndicators.DisplayedInfo?.gold_tile>0 && tile==FuzhouTableIndicators.DisplayedInfo.gold_tile?"金":null,
            Tingpai=q=>GameRecordManager.Instance?.ShouldShowRecordTips()==true
                ?GameRecordManager.Instance.FuzhouWaiting(q):FuzhouGameState.Active?.Hints.Waiting(q)??new HashSet<int>(),
            DescribeWaitingTile=q=>q.Record!=null?GameRecordManager.Instance?.DescribeFuzhouWaiting(q):FuzhouGameState.Active?.Hints.Describe(q),
            FanNameText=(rule,label)=>FanText(label,false),FanValueText=(rule,label)=>FanText(label,true),
            SettlementTotal=q=>new SettlementTotalDisplay {FanText=$"和牌点数 {q.HuScore}分",ScoreText=q.WinnerPointDelta.HasValue?$"{q.WinnerPointDelta.Value:+#;-#;0}分":""},
            ScoreboardFanText=q=>$"{q.HuScore}分",RoundName=RoundName,
            RoundStatusText=q=>q.fuzhou_info?.rule_version=="fuzhou-user-20261014-v1"?LegacyRoundName(q.current_round):RoundName(q.current_round),
            MaxRoundText=r=>$"{r*4}局",
            MaxRoundInfoText=q=>$"{q.max_round*(q.fuzhou_info?.rule_version=="fuzhou-user-20261014-v1"?6:4)}局",
            RoundIndicators=info=>FuzhouTableIndicators.FieldState(info?.fuzhou_info??FuzhouTableIndicators.DisplayedInfo),
            HasFlowerReplacement=true,ReplacementFromTailEnd=true,PeekAnkan=true,
            AutoBuhuaDelay=()=>FuzhouGameState.Active?.Info?.gold_tile==0 ? 0f : 0.3f,
            ReplayConcealedKongMask=(rule,tiles)=>new[]{2,tiles[0],0,tiles[1],0,tiles[2],2,tiles[3]},
            SupportsRobbedAddedKongSource=false,RonWinTileTravelsFromRiver=true,RecordDangerUsesWaitHint=true,
            AdjustHepaiPresentation=(request,robbed)=> {if(robbed){request.WinTileMode=HepaiWinTilePresentMode.RonInstantThenPause;request.RestoreRecordHandFromSnapshot=true;request.IsQianggang=false;}},
            RecordFlowerWin=tick=>tick!=null && tick.Count>3 && (tick[3].Contains("flower_win")||tick[3].Contains("three_gold")),
            SettlementHasNoWinTile=labels=>labels?.Any(x=>x.Contains("flower_win")||x.Contains("three_gold"))==true,
            ActionVoice=word=>word=="hu_self"?"hu":null,DefaultHepaiLimit=0,RulebookKey="fuzhou",
        });
        ActionWords.Register(new ActionWordSpec {Word="sanjin",Kind=ActionWordKind.Tsumo,Label=_=>"三金倒"});
        ActionWords.Register(new ActionWordSpec {Word="fuzhou_flower_win",Kind=ActionWordKind.Tsumo,
            Label=_=>"花和",BlocksAutoCut=true,RequiresManualChoice=true});
        foreach(string word in new[]{"youjin","tingjin","qiangjin"}) {
            string captured=word;
            ActionWords.Register(new ActionWordSpec {Word=word,BlocksAutoCut=true,Label=_=>Caption(captured),Expand=_=>Candidates(captured)});
        }
        foreach(string word in new[]{"kaijin","kaijin_flower","qiangjin_take"})
            ActionWords.Register(new ActionWordSpec {Word=word,Label=_=>""});
    }
    internal static string Caption(string action)=>action=="youjin"?(FuzhouGameState.Active?.SelfDeclaration?.youjin==1?"二游":"游金"):action=="tingjin"?"听金坎":"抢金";
    internal static IReadOnlyList<ActionCandidate> Candidates(string word) {
        var options=FuzhouGameState.Active?.DeclarationCandidates;
        if(options!=null && options.TryGetValue(word,out int[] tiles))
            return tiles.Select(tile=>new ActionCandidate(word,new[]{tile},tile,$"{Caption(word)}：打{TileText(tile)}")).ToArray();
        return word=="qiangjin"?new[]{new ActionCandidate(word,Array.Empty<int>(),0,"抢金")}:Array.Empty<ActionCandidate>();
    }
    public static string RoundName(int round)=>$"第{round}局";
    internal static string LegacyRoundName(int round) {int n=Math.Max(0,round-1)%6;return $"{(n<3?"东":"南")}{n%3+1}局";}
    public static string TileText(int tile)=>tile>=11 && tile<=39?$"{tile%10}{new[]{"万","饼","条"}[tile/10-1]}":"未开金";
    internal static string FanText(string label,bool value) {var p=label?.Split('|');return p?.Length==4&&p[0]=="FZ"?p[value?2:3]+(value?"分":""):value?"":label;}
}

public static class FuzhouTableIndicators {
    public static FuzhouInfo DisplayedInfo {get;private set;}
    public static int[] GoldTiles=>DisplayedInfo?.gold_tile>0?new[]{DisplayedInfo.gold_tile}:Array.Empty<int>();
    public static RoundIndicatorDisplay FieldState(FuzhouInfo info) {
        string declaration="";
        if(info?.declarations!=null) for(int i=0;i<info.declarations.Length;i++) {
            var d=info.declarations[i];
            if(d!=null && (d.youjin>0||d.golden_middle)) declaration+=$" {new[]{"东","南","西","北"}[i]}{(d.golden_middle?"听金坎":d.youjin==2?"二游":"一游")}";
        }
        return new RoundIndicatorDisplay {Tiles=new[]{info?.gold_tile>0?info.gold_tile:0},
            Caption=(declaration+(info?.last_four==true?" 末四张":"")).Trim(),
            Counter=info==null?"":$"连庄：{info.dealer_streak} 死牌：{info.reserved_tiles}"};
    }
    public static void Refresh(FuzhouInfo info) {
        bool changed=DisplayedInfo?.gold_tile!=info?.gold_tile;
        DisplayedInfo=info;RoundPanel.Instance?.RefreshRuleIndicators(FieldState(info));
        if(!changed) return;
        GameCanvas.Instance?.RefreshRuleHandOrder();
        Game3DManager.Instance?.RefreshRecordJokerHandOrder();
        GameRecordManager.Instance?.RefreshInitialHandJokerOrder();
        foreach(var card in UnityEngine.Object.FindObjectsByType<TileCard>(FindObjectsSortMode.None)) RuleTileBadge.Apply(card,card.tileId);
        foreach(var card in UnityEngine.Object.FindObjectsByType<StaticCard>(FindObjectsSortMode.None)) RuleTileBadge.Apply(card.transform,card.TileId);
        foreach(var tile in UnityEngine.Object.FindObjectsByType<Tile3D>(FindObjectsSortMode.None)) tile.RefreshRuleBadge();
    }
}
