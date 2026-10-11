#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Run explicitly from the Editor bridge; deterministic contracts, not a replacement for live UI tests.</summary>
public static class HangzhouClientContractValidation {
    public static string RunScoreHints() {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        var cache = new HangzhouHintsCache();
        var hand = new List<int> {11,12,13,21,22,23,31,32,33,41,41,41,42};
        var query = new WaitHintQuery {HandWithWin = hand.Concat(new[]{42}).ToList(), HepaiTile = 42, Melds = new List<string>()};
        var snapshot = new HangzhouHints {
            source_hand_tiles = hand.ToArray(), waiting_tiles = new[]{42,46},
            waiting_details = new Dictionary<int, HangzhouWaitDetail> {
                {42, new HangzhouWaitDetail {fan=1,base_score=2}},
                {46, new HangzhouWaitDetail {fan=0,base_score=1}},
            },
        };
        Check(cache.Describe(query) == null, "No invented score before hints arrive");
        cache.Accept(JsonConvert.DeserializeObject<HangzhouHints>(JsonConvert.SerializeObject(snapshot)));
        var hint = cache.Describe(query);
        Check(hint.Label == "自摸1番2分" && hint.Kind == WaitTileHint.KindTsumoOnly, "Exact single-line label keeps yellow self-draw category");
        query.HandWithWin.Reverse();
        Check(cache.Describe(query).Label == "自摸1番2分", "Reordered hand keeps matching score");
        query.HandWithWin = hand.Concat(new[]{46}).ToList(); query.HepaiTile = 46;
        Check(cache.Describe(query).Label == "自摸0番1分", "Zero fan remains one point");
        query.HandWithWin[0] = 19;
        Check(cache.Describe(query) == null, "Different hand cannot borrow a cached score");
        query.HandWithWin = hand.Concat(new[]{46}).ToList(); query.Melds.Add("k45");
        Check(cache.Describe(query) == null, "Different melds cannot borrow a cached score");
        query.Melds.Clear();
        snapshot.source_hand_tiles = hand.Concat(new[]{46}).ToArray();
        snapshot.waiting_tiles = Array.Empty<int>(); snapshot.waiting_details = null;
        snapshot.waiting_by_discard = new Dictionary<int,int[]> {{46,new[]{42,46}}};
        snapshot.waiting_details_by_discard = new Dictionary<int,Dictionary<int,HangzhouWaitDetail>> {
            {46,new Dictionary<int,HangzhouWaitDetail>{{46,new HangzhouWaitDetail {fan=4,base_score=16}}}},
        };
        cache.Accept(snapshot);
        Check(cache.Describe(query).Label == "自摸4番16分", "Discard preview selects its own capped score row");
        cache.Accept(new HangzhouHints());
        Check(cache.Describe(query).Label == "自摸4番16分", "Public-only updates preserve matching scores");
        snapshot.waiting_details_by_discard[46][46].base_score = 99;
        Check(cache.Describe(query).Label == "自摸", "Malformed score never becomes displayed points");
        snapshot.waiting_details_by_discard = null;
        Check(cache.Describe(query).Label == "自摸", "Legacy snapshots do not invent missing points");
        cache.Reset();
        Check(cache.Describe(query) == null, "Reset clears scores");
        return JsonConvert.SerializeObject(new {passed=checks});
    }

    /// <summary>Read-only assertions on a real loaded Hangzhou replay; invoke at start, burn/draw and terminal nodes.</summary>
    public static string RunReplayWallContract() {
        var manager=GameRecordManager.Instance;
        if (manager==null || manager.gameRecord==null || RuleRegistry.Current?.RuleId!="hangzhou")
            throw new InvalidOperationException("Load an actual Hangzhou replay before checking its wall");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var wall=(List<int>)typeof(GameRecordManager).GetField("currentTilesList",flags).GetValue(manager);
        var indices=(List<int>)typeof(GameRecordManager).GetField("currentOriginalIndices",flags).GetValue(manager);
        var original=(List<int>)typeof(GameRecordManager).GetField("originalTilesList",flags).GetValue(manager);
        int remaining=(int)typeof(GameRecordManager).GetMethod("GetRecordRemainTiles",flags).Invoke(manager,null);
        int expected=Math.Max(0,wall.Count-20),checks=0;
        void Check(bool ok,string label) { if(!ok)throw new Exception(label);checks++; }
        Check((int)typeof(GameRecordManager).GetField("recordDeadWallCount",flags).GetValue(manager)==20,"Hangzhou reserves twenty physical tail tiles");
        Check(remaining==expected,"drawable remaining differs from physical wall count by the reserved tail");
        Check(indices.Count==wall.Count,"physical wall and remaining original indices stay aligned");
        for(int index=-1;index<=original.Count;index++) {
            int position=indices.IndexOf(index);
            Check(manager.IsOriginalWallIndexNormalDrawable(index)==(position>=0 && position<expected),"normal draw eligibility at original index "+index);
        }
        return JsonConvert.SerializeObject(new {passed=checks,wall_count=wall.Count,remaining,dead_wall_count=20});
    }

    public static string Run() {
        var checks=new List<string>();
        void Check(bool passed,string description) { if(!passed) throw new Exception(description);checks.Add(description); }
        HangzhouRuleBootstrap.Register();
        var manifest=RuleRegistry.Resolve("hangzhou","hangzhou/mil2025");
        Check(manifest!=null && manifest.OutboundChannel=="hangzhou","registered Hangzhou version/channel");
        Check(manifest.TileBadgeText(46)=="财" && manifest.TileBadgeText(47)==null,"physical Haku=46 is marked, Hatsu=47 is not");
        Check(!manifest.HasFlowerReplacement && manifest.ReplacementFromTailEnd,"no flowers and lower tail replacement");
        Check(!manifest.ShowsRonDangerHints,"self-draw-only Hangzhou does not mark ron danger tiles");
        Check(new[]{new GameInfo(),new GameInfo {hangzhou_info=new HangzhouInfo {dealer_streak=2}},new GameInfo {hangzhou_info=new HangzhouInfo {dealer_streak=3}}}.All(info=>manifest.RoundSupplementText(info)=="4番封顶"),"compact replay summary stays accurate; dealer streak belongs to the player's status labels");
        Check(GameRecordMeldCodec.BuildAngangMaskFromRemoved(new[]{11,11,11,11},"hangzhou/mil2025")
            .SequenceEqual(new[]{2,11,0,11,0,11,2,11}),"Hangzhou replay exposes only the two middle concealed-kong tiles");
        Check(manifest.SettlementFootnote==null,"Hangzhou settlement panels do not show rule footnotes");
        var automatic=AutoAction.Instance;
        Check(automatic!=null,"real auto-action component exists");
        var preference=typeof(AutoAction).GetField("isAutoCut",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        bool savedPreference=(bool)preference.GetValue(automatic),savedLock=automatic.IsAutoCutLocked;
        try {
            foreach(bool manual in new[]{false,true}) {
                automatic.SetAutoCutLocked(false);preference.SetValue(automatic,manual);
                automatic.SetAutoCutLocked(true);automatic.SetAutoCutLocked(true);
                Check(automatic.IsAutoCut && automatic.IsAutoCutLocked,"rule lock enforces automatic cut with preference "+manual);
                automatic.SetAutoCutLocked(false);
                Check(automatic.IsAutoCut==manual && !automatic.IsAutoCutLocked,"temporary rule unlock restores preference "+manual);
            }
        } finally { preference.SetValue(automatic,savedPreference);automatic.SetAutoCutLocked(savedLock); }
        Check(manifest.ActionVoice("hangzhou_ten_winds")=="hu" && manifest.ActionVoice("hangzhou_piao")=="piao" && manifest.ActionVoice("cut")==null,"ten winds and piao use dedicated voice keys; ordinary cut stays unvoiced");
        var cut=new TableAction {Words=new[]{"cut"},CutTile=46};
        Check(HangzhouGameState.IsPiaoDiscard(cut),"only physical white cut announces piao");
        cut.CutTile=47;Check(!HangzhouGameState.IsPiaoDiscard(cut),"green dragon cut does not announce piao");
        cut.CutTile=46;cut.Silent=true;Check(!HangzhouGameState.IsPiaoDiscard(cut),"silent cut cannot announce piao");
        var hints=new HangzhouHintsCache();
        var query=new TingpaiQuery {Hand=new List<int>{11,12,46},Melds=new List<string>()};
        Check(hints.Waiting(query).Count==0,"missing hints never invent waits");
        hints.Accept(new HangzhouHints {source_hand_tiles=new[]{46,12,11},waiting_tiles=new[]{13,46},source_melds=Array.Empty<string>()});
        Check(hints.Waiting(query).SetEquals(new[]{13,46}),"waits match reordered physical hand");
        hints.Accept(new HangzhouHints());
        Check(hints.Waiting(query).Count==2,"public state without hints preserves exact matching private hints");
        query.Hand=new List<int>{11,13,46};Check(hints.Waiting(query).Count==0,"different hand cannot use stale hints");
        hints.Accept(new HangzhouHints {source_hand_tiles=new[]{11,12,46,46},waiting_by_discard=new Dictionary<int,int[]>{{46,new[]{13,46}}}});
        query.Hand=new List<int>{11,12,46};Check(hints.Waiting(query).SetEquals(new[]{13,46}),"discard preview removes exactly one physical joker");
        query.Melds=new List<string>{"k41"};Check(hints.Waiting(query).Count==0,"different melds cannot use stale hints");
        hints.Reset();Check(hints.Waiting(query).Count==0,"round reset clears hint cache");
        var response=JsonConvert.DeserializeObject<Response>("{\"show_result_info\":{\"hangzhou_win_source\":\"ten_winds\",\"hepai_tile\":0,\"hepai_player_hand\":[11,12,13,21,22,23,31,32,33,41,41,46,46],\"hangzhou_info\":{\"dealer_streak\":3,\"dealer_multiplier\":8,\"joker_tiles\":[46],\"phase\":\"waiting_hangzhou_ten_winds\"}}}");
        Check(response.show_result_info.hangzhou_win_source=="ten_winds" && response.show_result_info.hepai_player_hand.Length==13,"ten winds protocol preserves 13 tiles and no winning draw");
        var fans=new[]{"HZ|ten_winds|3|十风","HZ|no_joker|1|无财神"};
        Check(manifest.SettlementHasNoWinTile(fans) && manifest.HuPresentationAction("hu_self",fans,null)=="hangzhou_ten_winds","ten winds result has no detached winning tile");
        Check(manifest.FanValueText("hangzhou",fans[0])=="3番" && manifest.FanNameText("hangzhou",fans[0])=="十风","scoring labels preserve name and fan value");
        var round=new Round();
        GameRecordJsonDecoder.ApplyRoundHeader(round,JObject.Parse("{\"seats\":[0,1,2,3],\"dealer_index\":0,\"start_player_index\":0,\"hangzhou\":{\"dealer_streak\":2,\"joker_tiles\":[46]}}"),1);
        Check(round.hangzhou.dealer_streak==2 && round.hangzhou.joker_tiles[0]==46,"record round metadata round-trips");
        GameRecordJsonDecoder.ApplyRoundHeader(round,JObject.Parse("{\"seats\":[0,1,2,3],\"dealer_index\":0,\"start_player_index\":0}"),1);
        Check(round.hangzhou==null,"old record headers remain valid");
        return JsonConvert.SerializeObject(new {passed=checks.Count,checks});
    }
}
#endif
