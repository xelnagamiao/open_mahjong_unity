using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class NewChineseRuleBootstrap {
    internal const string Description = "新中式麻将是融合了立直麻将，国标麻将和东北麻将的宝牌衍生出的一种玩法，取精华去糟粕，已有十年以上的实战验证基础，兼具竞技性与娱乐性，牌型难度和对应的番数做过大量优化使其合理，欢迎加入，微信15513635592";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Register() {
        const string rule = NewChineseGameState.RuleId, sub = NewChineseGameState.SubRule;
        RuleNameDictionary.WholeName[rule] = RuleNameDictionary.WholeName[sub] = "新中式麻将";
        RuleNameDictionary.ShortName[rule] = RuleNameDictionary.ShortName[sub] = "新中式";
        RuleRegistry.Register(new RuleManifest {
            RuleId=rule, DefaultSubRule=sub, DisplayName="新中式麻将", LobbyOrder=35, LobbyHasDetailedConfig=true,
            LobbySubRules=new[]{new RuleLobbySubRule(sub,"标准规则",Description)}, OutboundChannel=rule,
            BuildRoom=NewChineseRoomBuilder.Build, GameStateFactory=()=>new NewChineseGameState(),
            ReplayFactory=r=>r.CreateNewChineseReplayModule(), RulebookKey=rule,
            CreateRoomDefaults=new Dictionary<string,object> {
                {CreateRoomKeys.GameRound,1}, {CreateRoomKeys.RoundTimer,3}, {CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true}, {CreateRoomKeys.CountTips,false}, {CreateRoomKeys.PointerTips,true},
                {CreateRoomKeys.Password,false}, {CreateRoomKeys.RandomSeed,false}, {CreateRoomKeys.TouristLimit,false}, {CreateRoomKeys.AllowSpectator,true}
            },
            OpeningHandHasDrawSlot=i=>i?.new_chinese?.self_has_draw_slot==true,
            HasFlowerReplacement=false, DefaultHepaiLimit=0, FaceDownAnkan=true, PeekAnkan=true,
            PublicReadyStateReplay=true, RecordDangerUsesWaitHint=true, RonWinTileTravelsFromRiver=true,
            ActionCaption=a=>a=="riichi"||a=="riichi_cut"?"报听":a=="riichi_cut_cancel"?"取消报听":null,
            ActionVoice=a=>a=="riichi"||a=="riichi_cut"?"ting":a=="hu_self"?"hu":null,
            LiveVisibleIndicators=()=>NewChineseGameState.Active?.Info?.bao > 0 ? new[]{NewChineseGameState.Active.Info.bao} : Array.Empty<int>(),
            StatsFanNames=NewChineseHand.Values.Keys.ToDictionary(k=>k,k=>k),
            Tingpai=Waiting, DescribeWaitingTile=Describe, ScoreboardFanText=q=>$"{q.HuScore}点",
            FanNameText=(r,label)=>FanName(label), FanValueText=(r,label)=>FanValue(label),
            SettlementTotal=q=>new SettlementTotalDisplay{FanText=$"{q.HuScore}点",ScoreText=""},
            ReplayConcealedKongMask=(r,tiles)=>tiles.SelectMany(t=>new[]{2,t}).ToArray()
        });
        foreach (string chow in new[]{"chi_left", "chi_mid", "chi_right"}) {
            string word = chow + "_ting";
            ActionWords.Register(new ActionWordSpec {Word=word, BlocksAutoCut=true, Label=_=>"吃听",
                Expand=action=>ChowChoices(word)});
        }
    }
    private static IReadOnlyList<ActionCandidate> ChowChoices(string word) {
        int tile = TableMirror.Current.LastCutCardID;
        int[] offsets = word=="chi_left_ting"?new[]{-2,-1}:word=="chi_mid_ting"?new[]{-1,1}:new[]{1,2};
        return new[]{new ActionCandidate(word, offsets.Select(o=>o+tile).OrderBy(x=>x).ToArray(),0,"吃听")};
    }

    internal static string FanName(string label) {
        int index = label?.LastIndexOf('（') ?? -1;
        return index >= 0 ? label.Substring(0,index) : label;
    }
    internal static string FanValue(string label) {
        int i = label?.LastIndexOf('（') ?? -1;
        return i >= 0 && label.EndsWith("）") ? label.Substring(i+1,label.Length-i-2) : "";
    }

    private static NewChineseInfo Context(bool replay) => replay ? GameRecordManager.Instance?.NewChineseRecordInfo : NewChineseGameState.Active?.Info;

    internal static HashSet<int> Waiting(TingpaiQuery q) {
        var hand = NewChineseHand.Read(q.Hand,q.Melds);
        var result = new HashSet<int>(hand.Basic);
        var info = Context(q.RecordPlayerIndex.HasValue || GameRecordManager.Instance?.ShouldShowRecordTips()==true);
        int seat = q.RecordPlayerIndex ?? q.PlayerIndex ?? GameSession.Current.SelfIndex;
        if (info?.bao > 0 && IsReady(info,seat) && hand.Legal.Count > 0 && CanDrawTile(q.Hand,q.Melds,info.bao))
            result.Add(info.bao);
        return result;
    }

    private static bool CanDrawTile(List<int> hand,List<string> codes,int face) {
        int owned = hand.Count(t=>t==face);
        foreach (string code in codes ?? new List<string>()) owned += (ChangchunHandCalculator.MeldTiles(code) ?? Array.Empty<int>()).Count(t=>t==face);
        return owned < 4;
    }
    private static bool IsReady(NewChineseInfo info,int i) => i>=0 && i<(info?.declared?.Length ?? 0) && info.declared[i];

    internal static WaitTileHint Describe(WaitHintQuery q) {
        if (q.HandWithWin == null) return WaitTileHint.None("未满足");
        var before = new List<int>(q.HandWithWin);
        if (!before.Remove(q.HepaiTile)) return WaitTileHint.None("未满足");
        var info = Context(q.Record != null);
        int i = q.SelfIndex, tile = q.HepaiTile;
        var hand = NewChineseHand.Read(before,q.Melds);
        bool ready = IsReady(info,i), bao = ready && info.bao == tile;
        bool blocked = i >= 0 && i < (info?.blocked?.Length ?? 0) ? info.blocked[i] : info?.win_blocked == true;
        if (info == null || blocked || !hand.Legal.Contains(tile) && !(bao && hand.Legal.Count>0))
            return WaitTileHint.None("未满足");
        if (!ready) return WaitTileHint.None("未起和");
        int level = info.levels != null && i >= 0 && i < info.levels.Length ? info.levels[i] : info.pass_level;
        int[] passed = info.passed != null && i >= 0 && i < info.passed.Length ? info.passed[i] : info.passed_ron;
        int chain = info.chains != null && i >= 0 && i < info.chains.Length ? info.chains[i] : 0;
        if (info.best_chains != null && i >= 0 && i < info.best_chains.Length) chain = Math.Max(chain,info.best_chains[i]);
        int visible = info.visible?.TryGetValue(tile,out int n)==true ? n : 0;
        bool discardWindow = info.payer.HasValue && info.payer != i && info.phase=="waiting_action_after_cut";
        if ((discardWindow || info.rob_from_discard) && info.cut_tile == tile) visible = Math.Max(0,visible-1);
        bool afterKong = i >= 0 && i < (info.after_kongs?.Length ?? 0) ? info.after_kongs[i] : info.after_kong;
        var self = level < 4 && (level < 3 || bao) ? hand.Score(tile,bao?"bao_draw":"self_draw",i==0,false,chain,afterKong,visible+(bao?1:0)) : null;
        if (info.phase == "waiting_tongbao" && info.acting == i && bao && level < 4) {
            var natural = hand.Score(tile, "bao_seen", i == 0, false, chain, false, visible);
            if (natural != null) return WaitTileHint.TsumoOnly($"通宝\n{natural.Points}点");
        }
        var prices = new List<int>();
        if (!bao && level < 2 && !(passed ?? Array.Empty<int>()).Contains(tile)) {
            for (int payer=0; payer<4; payer++) {
                if (payer == i || info.payer.HasValue && info.payer != payer) continue;
                string mode = info.phase=="waiting_action_qianggang" || !IsReady(info,payer) ? "black" : "discard";
                var ron = hand.Score(tile,mode,i==0,payer==0,chain,false,visible);
                if (ron != null) prices.Add(ron.Points);
            }
        }
        if (prices.Count > 0) return WaitTileHint.Ron(prices.Min()==prices.Max()?$"{prices[0]}点":$"{prices.Min()}–{prices.Max()}点");
        return self != null ? WaitTileHint.TsumoOnly($"仅自摸\n{self.Points}点") : WaitTileHint.None("未满足");
    }
}
