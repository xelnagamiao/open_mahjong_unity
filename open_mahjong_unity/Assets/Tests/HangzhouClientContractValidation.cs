#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Run explicitly from the Editor bridge; deterministic contracts, not a replacement for live UI tests.</summary>
public static class HangzhouClientContractValidation {
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
        Check(new[]{new GameInfo(),new GameInfo {hangzhou_info=new HangzhouInfo {dealer_streak=2}},new GameInfo {hangzhou_info=new HangzhouInfo {dealer_streak=3}}}.All(info=>manifest.RoundSupplementText(info)=="4番封顶"),"compact replay summary stays accurate with or without Hangzhou GameInfo; live dealer streak belongs to the HUD");
        Check(manifest.SettlementFootnote(new SettlementTotalQuery {Rule="hangzhou",HuFan=null})==null,"draw result does not retain a Hangzhou win-only footnote");
        Check(manifest.SettlementFootnote(new SettlementTotalQuery {Rule="hangzhou",HuFan=new[]{"HZ|plain|0|平和"}}).Contains("\n"),"Hangzhou win footnote uses two bounded lines at the existing result position");
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
        var tenWindsState=JsonConvert.DeserializeObject<HangzhouInfo>("{\"phase\":\"END\",\"ledger\":{\"source\":\"ten_winds\",\"score\":{\"decomposition\":null},\"payment\":{\"transfers\":[{\"payer\":1,\"winner\":0,\"points\":8,\"reason\":\"自摸\"}]}}}");
        string tenWindsText=HangzhouStatePanel.Describe(tenWindsState);
        Check(tenWindsText.Contains("支付明细") && !tenWindsText.Contains("财神替代"),"ten winds with JSON-null decomposition keeps payments without indexing a JValue");
        Check(tenWindsText.Contains("8分 · 十风") && (string)tenWindsState.ledger["payment"]["transfers"][0]["reason"]=="自摸","ten winds payment uses the correct display name without mutating saved payment reason");
        var drawState=JsonConvert.DeserializeObject<HangzhouInfo>("{\"phase\":\"END\",\"ledger\":{\"score\":null,\"payment\":null}}");
        Check(!HangzhouStatePanel.Describe(drawState).Contains("支付明细"),"draw settlement safely handles JSON-null score and payment");
        Check(!string.IsNullOrEmpty(HangzhouStatePanel.Describe(JsonConvert.DeserializeObject<HangzhouInfo>("{\"ledger\":null}"))),"JSON-null ledger preserves the public status panel");
        Check(!string.IsNullOrEmpty(HangzhouStatePanel.Describe(new HangzhouInfo())),"missing ledger preserves the public status panel");
        var round=new Round();
        GameRecordJsonDecoder.ApplyRoundHeader(round,JObject.Parse("{\"seats\":[0,1,2,3],\"dealer_index\":0,\"start_player_index\":0,\"hangzhou\":{\"dealer_streak\":2,\"joker_tiles\":[46]}}"),1);
        Check(round.hangzhou.dealer_streak==2 && round.hangzhou.joker_tiles[0]==46,"record round metadata round-trips");
        GameRecordJsonDecoder.ApplyRoundHeader(round,JObject.Parse("{\"seats\":[0,1,2,3],\"dealer_index\":0,\"start_player_index\":0}"),1);
        Check(round.hangzhou==null,"old record headers remain valid");
        return JsonConvert.SerializeObject(new {passed=checks.Count,checks});
    }
}
#endif
