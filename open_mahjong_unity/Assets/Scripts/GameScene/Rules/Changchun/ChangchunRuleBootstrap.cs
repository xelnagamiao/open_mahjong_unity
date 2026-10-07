using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class ChangchunRuleBootstrap {
    public const string RuleId = "changchun";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleNameDictionary.WholeName[RuleId] = "长春麻将";
        RuleNameDictionary.WholeName["changchun/mil2024"] = "长春麻将（MIL 2024）";
        RuleNameDictionary.ShortName[RuleId] = "长春";
        RuleNameDictionary.ShortName["changchun/mil2024"] = "长春";
        RuleRegistry.Register(new RuleManifest {
            RuleId = RuleId, DefaultSubRule = "changchun/mil2024", DisplayName = "长春麻将", LobbyOrder = 16, OutboundChannel="changchun",
            LobbySubRules = new[] { new RuleLobbySubRule("changchun/mil2024", "MIL 2024",
                "MIL《长春麻将（推广）竞赛规则（试行2024版）》：136张、十三张手牌，须有三门与幺九。首个出牌回合可亮幺九杠、旋风杠、喜杠，一条只在这些特殊杠中替牌。报听后下次轮到自己看宝，支持冲宝、摸宝；6番封顶，未听或未合理看宝放铳包三家。末四张只和不打。") },
            CreateRoomDefaults = new Dictionary<string, object> {
                {CreateRoomKeys.SubRule,0},{CreateRoomKeys.GameRound,4},{CreateRoomKeys.RoundTimer,3},{CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true},{CreateRoomKeys.CountTips,false},{CreateRoomKeys.PointerTips,true},{CreateRoomKeys.TacticalCall,true},
                {CreateRoomKeys.Password,false},{CreateRoomKeys.RandomSeed,false},{CreateRoomKeys.TouristLimit,false},{CreateRoomKeys.AllowSpectator,true}
            },
            GameStateFactory = () => new ChangchunGameState(),
            Tingpai = q => ChangchunHandCalculator.BasicWaits(q.Hand,q.Melds),
            VisibleMeldTiles = (code,self) => !self && code?.StartsWith("G")==true ? Array.Empty<int>() : code?.StartsWith("C")==true ? ChangchunHandCalculator.MeldTiles(code) : null,
            LiveVisibleIndicators = VisibleBaoTiles,
            DescribeWaitingTile = q => ChangchunHandCalculator.IsQualified(q.HandWithWin,q.Melds)
                ? WaitTileHint.Ron($"基础{ChangchunHandCalculator.Score(q.HandWithWin,q.Melds,q.HepaiTile)}番，另计庄及和法")
                : WaitTileHint.None("未满足"),
            RoundName = r => $"第{r}副", RoundStatusText = q => $"第{q.current_round}副", MaxRoundText = r => $"{r*4}副",
            FanValueText = (r,n) => FanValue(n), ScoreboardFanText = q => $"基础{q.HuScore}番",
            SettlementTotal = q => new SettlementTotalDisplay {FanText=$"基础{q.HuScore}番",ScoreText="6番封顶"},
            SettlementFootnote = q => "逐家结算，杠分另计；庄家、和法加番后每家6番封顶。",
            AdjustHepaiPresentation = AdjustRobbedKongPresentation,
            ActionCaption = a => a == "riichi" || a == "riichi_cut" ? "报听" : a=="riichi_cut_cancel" ? "取消报听" : null,
            // MIL 2024 三-23、六-2：声明报“听”；自摸也报“和”。
            ActionVoice = a => a == "riichi" || a == "riichi_cut" ? "ting" : a == "hu_self" ? "hu" : null,
            HasFlowerReplacement=false,RonWinTileTravelsFromRiver=true,FaceDownAnkan=true,PublicReadyStateReplay=true,DefaultHepaiLimit=0,
            RecordDangerUsesWaitHint=true
        });
        ActionWords.Register(new ActionWordSpec {Word="cc_special",BlocksAutoCut=true,Label=_=>"特殊杠",Expand=_=>Candidates(false)});
        ActionWords.Register(new ActionWordSpec {Word="cc_added",BlocksAutoCut=true,Label=_=>"加特殊杠",Expand=_=>Candidates(true)});
        ActionWords.Register(new ActionWordSpec {Word="cc_draw",Label=_=>"摸牌"});
        ActionWords.Register(new ActionWordSpec {Word="cc_change_bao",Label=_=>"换宝"});
        ActionWords.Register(new ActionWordSpec {Word="cc_pass",Kind=ActionWordKind.Pass,Label=_=>"过"});
    }
    private static IReadOnlyList<ActionCandidate> Candidates(bool added) {
        var active=RuleRegistry.ActiveGameState as ChangchunGameState;
        var options=added ? active?.Info?.added_candidates : active?.Info?.special_candidates;
        return (options ?? Array.Empty<ChangchunCandidate>()).Select(c=>new ActionCandidate(added?"cc_added":"cc_special",
            c.physical,c.token,c.label+"："+string.Join("",(c.logical??Array.Empty<int>()).Select(TileName)))).ToArray();
    }
    private static IEnumerable<int> VisibleBaoTiles() {
        var info=(RuleRegistry.ActiveGameState as ChangchunGameState)?.Info;
        return info?.bao_visible==true && info.bao_tile>0 ? new[]{info.bao_tile} : Array.Empty<int>();
    }
    private static void AdjustRobbedKongPresentation(HepaiPresentationRequest request,bool robbed) {
        if(!robbed) return;
        // Both ordinary and special robbed additions are removed by the
        // authoritative event before hu; no committed fourth meld tile or
        // river tile remains for the normal travel animation to take.
        request.WinTileMode=HepaiWinTilePresentMode.RonInstantThenPause;
        request.RestoreRecordHandFromSnapshot=true;
        request.IsQianggang=false;
    }
    public static string TileName(int tile) {
        if (tile>=41 && tile<=47) return new[]{"东","南","西","北","中","白","发"}[tile-41];
        if (tile>=11 && tile<=39 && tile%10>=1 && tile%10<=9) return (tile%10)+new[]{"万","筒","条"}[tile/10-1];
        return "未知";
    }
    private static string FanValue(string name) {
        switch(name) {
            case "门清":case "夹/单吊/单和":case "摸宝":return "1番";
            case "飘和":case "冲宝":return "2番";
            case "七对":return "3番";case "豪华七对":return "4番";default:return "";
        }
    }
}
