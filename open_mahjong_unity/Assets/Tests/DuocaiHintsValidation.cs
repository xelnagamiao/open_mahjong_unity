using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

public static class DuocaiHintsValidation {
    public static string Run() {
        var checks=new List<string>();var failures=new List<string>();
        void Check(bool value,string name){checks.Add(name);if(!value)failures.Add(name);}
        JObject Context()=>new JObject{["variant"]="riichi",["physical_melds"]=new JArray(),["seat_wind"]=42,["round_wind"]=41};
        DuocaiLocal Local(JObject c)=>new DuocaiLocal(new List<string>(),c);
        var fifth=new[]{11,11,11,11,22,22,22,33,1033,2033,44,44,44};
        var ctx=Context();ctx["riichi"]=1;ctx["discards"]=new JArray(5000);
        var local=Local(ctx);
        Check(local.LogicalWaits(fifth).SequenceEqual(new[]{11}),"joker-only fifth logical copy remains tenpai");
        Check(!local.Waiting(fifth.ToList()).Contains(11)&&local.Waiting(fifth.ToList()).Contains(5039),"physical supply remains enforced");
        var row=local.Describe(fifth.Concat(new[]{5039}).ToList(),5039);
        Check((bool)row["furiten"]&&!(bool)row["can_ron"]&&(bool)row["can_self"],"discarded joker blocks ron on fifth-copy wait");
        var closed=new List<int>{11,12,13,22,23,24,34,35,36,36,37,38,41,41};
        ctx=Context();row=Local(ctx).Describe(closed,41);
        Check(!(bool)row["can_ron"]&&(bool)row["can_self"],"no-yaku ron retains menzen tsumo");
        ctx["pending_riichi"]=true;ctx["pending_riichi_value"]=1;
        Check((bool)Local(ctx).Describe(closed,41,19)["can_ron"],"pending riichi gives discard preview its actual yaku");
        ctx=Context();ctx["seat_wind"]=41;ctx["win_source"]="self_draw";ctx["current_win_tile"]=41;ctx["tenhou"]=true;
        row=Local(ctx).Describe(closed,41);
        Check((int)row["fan"]==13&&(int)row["self_points"]==48000&&!(bool)row["can_ron"]&&(string)row["label"]=="48000点","dealer opening draw shows tenhou and 48000 self points");
        Check((int)Local(ctx).Describe(closed,41,19)["self_points"]<48000,"discard preview cannot borrow tenhou");
        ctx["seat_wind"]=42;
        row=Local(ctx).Describe(closed,41);
        Check((int)row["fan"]<13&&(int)row["self_points"]<32000,"nondealer cannot receive tenhou from an invalid flag");
        ctx["tenhou"]=false;ctx["chiihou"]=true;
        row=Local(ctx).Describe(closed,41);
        Check((int)row["fan"]==13&&(int)row["self_points"]==32000&&!(bool)row["can_ron"]&&(string)row["label"]=="32000点","nondealer first draw shows chiihou and 32000 self points");
        Check((int)Local(ctx).Describe(closed,41,19)["self_points"]<32000,"discard preview cannot borrow chiihou");
        ctx["seat_wind"]=41;
        row=Local(ctx).Describe(closed,41);
        Check((int)row["fan"]<13&&(int)row["self_points"]<48000,"dealer cannot receive chiihou from an invalid flag");
        var open=new List<int>{22,23,24,34,35,36,36,37,41,41,38};
        ctx=Context();ctx["physical_melds"]=JArray.FromObject(new[]{new DuocaiMeld{physical=new[]{11,12,13},declared="chi"}});
        Check(!(bool)Local(ctx).Describe(open,38)["can_ron"],"open no-yaku hand cannot ron");
        ctx["win_source"]="rob_kong";ctx["current_win_tile"]=38;ctx["chankan"]=true;
        row=Local(ctx).Describe(open,38);
        Check((bool)row["can_ron"]&&(int)row["ron_points"]==1000,"actual rob-kong supplies its yaku and points");
        ctx["current_win_tile"]=35;
        Check(!(bool)Local(ctx).Describe(open,38)["can_ron"],"other hypothetical waits cannot borrow rob-kong yaku");
        ctx["win_source"]="self_draw";ctx["current_win_tile"]=38;ctx["chankan"]=false;ctx["rinshan"]=true;
        Check((bool)Local(ctx).Describe(open,38)["can_self"],"actual rinshan supplies its yaku");
        Check(!(bool)Local(ctx).Describe(open,38,19)["can_self"],"discard preview cannot borrow rinshan yaku");
        var mixed=new[]{37,1037,2037,5039};
        Check(DuocaiHintPresentation.DisplayTiles(mixed).SequenceEqual(new[]{37,5039}),"gold silver share one displayed tile without merging joker");
        var merged=DuocaiHintPresentation.Merge(new[]{(37,WaitTileHint.Ron("1000点")),(1037,WaitTileHint.Ron("2000点")),(2037,WaitTileHint.None("未起和"))});
        Check(merged.Label=="普 1000点\n金 2000点\n银 未起和","merged row preserves physical eligibility and score");
        Check(DuocaiHintPresentation.Merge(new[]{(37,WaitTileHint.None("未满足")),(1037,WaitTileHint.None("未满足"))}).Label=="未满足","identical variants collapse redundant text");
        var extras=DuocaiResultPresentation.BuildExtras(new ShowResultInfo{han=3,fu=40,dora_indicators=new[]{13},ura_dora_indicators=new[]{27},honba=2});
        var display=DuocaiResultPresentation.Total(new SettlementTotalQuery{HuScore=5200,Extras=extras});
        Check(display.FanText=="3番"&&display.FuText=="40符"&&extras.UraDoraIndicators.SequenceEqual(new[]{27}),"shared result exposes han fu and actual ura tile");
        display=DuocaiResultPresentation.Total(new SettlementTotalQuery{HuScore=32000,HuFan=new[]{"纯赖子明杠×1（13番）"},Extras=new RiichiEndResultExtras{Han=13,Fu=25}});
        Check(display.FuText==null&&display.LimitText=="役满","yakuman result never presents placeholder fu as real scoring");
        Check(DuocaiLocal.Points(new DuocaiScoring.Quote{Variant="guobiao",Fan=8,Minimum=true,Conditions=true},false,false,0)==308,"Guobiao ron remains three base hundreds plus fan");
        var pure=new[]{new DuocaiMeld{physical=new[]{5000,5008,5020,5039},declared="gang"}};
        var score=DuocaiScoring.Evaluate(new[]{45,45,45,22,23,24,36,37,41,41,38},pure,"riichi",new DuocaiScoring.Context());
        Check(score.Valid&&score.Yakuman==1&&DuocaiLocal.Points(score,false,false,0)==32000,"pure joker kong uses yakuman limit");
        score=DuocaiScoring.Evaluate(new[]{22,23,24,32,33,34,36,37,41,41,38},pure,"riichi",new DuocaiScoring.Context());
        Check(!score.Valid&&score.Fan==13,"pure joker reward cannot provide required yaku");
        CheckRobbedKongSources(Check);
        return new JObject{["passed"]=failures.Count==0,["count"]=checks.Count,["checks"]=new JArray(checks),["failures"]=new JArray(failures)}.ToString();
    }
    private static void CheckRobbedKongSources(Action<bool,string> check) {
        var mirror=TableMirror.Current;var originalSeats=mirror.IndexToPosition;
        var configure=typeof(HepaiRevealDirector).GetMethod("ConfigureRuleSpecificRonRequest",BindingFlags.Static|BindingFlags.NonPublic);
        try {
            mirror.IndexToPosition=new Dictionary<int,string>{{0,"self"},{1,"right"},{2,"top"},{3,"left"}};
            foreach(string rule in new[]{"duocai/guobiao","duocai/riichi"})foreach(int source in new[]{0,3}) {
                var info=Newtonsoft.Json.JsonConvert.DeserializeObject<ShowResultInfo>("{\"hepai_tile\":38,\"is_qianggang\":true,\"ron_discarder_index\":"+source+"}");
                var request=HepaiRevealDirector.BuildRequestCore("right","hu_first",new[]{11,12,38},null,rule,"top");
                configure.Invoke(null,new object[]{request,rule,info.is_qianggang==true,info.ron_discarder_index,info.hepai_tile});
                check(request.IsQianggang&&request.DiscardPlayerPosition==mirror.IndexToPosition[source]
                    &&request.HepaiTile==38&&request.WinTileMode==HepaiWinTilePresentMode.GuobiaoRonTravelFromRiver,
                    rule+" rob-kong uses protocol source "+source+" instead of previous river");
            }
        } finally {mirror.IndexToPosition=originalSeats;}
    }
    public static string RunPerformance() {
        var rows=new JArray();var baseline=new[]{11,12,13,22,23,24,34,35,36,36,37,28,28};
        for(int n=0;n<=3;n++) {
            var hand=new[]{5039,5001,5002}.Take(n).Concat(baseline.Skip(n)).ToArray();
            var local=new DuocaiLocal(new List<string>(),new JObject{["variant"]="riichi",["physical_melds"]=new JArray()});
            var clock=System.Diagnostics.Stopwatch.StartNew();var waits=local.LogicalWaits(hand);clock.Stop();double cold=clock.Elapsed.TotalMilliseconds;
            clock.Restart();for(int repeat=0;repeat<1000;repeat++)local.LogicalWaits(hand);clock.Stop();
            rows.Add(new JObject{["jokers"]=n,["cold_ms"]=cold,["hot_ms"]=clock.Elapsed.TotalMilliseconds/1000,["waits"]=new JArray(waits)});
        }
        return rows.ToString();
    }
    public static string ValidateVisibleHints(int[] physicalWaiting) {
        var view=TipsContainer.Instance;
        var container=(GameObject)typeof(TipsContainer).GetField("TileContainer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
        var cards=container.transform.Cast<Transform>().Select(t=>t.GetComponent<StaticCard>().TileId).ToArray();
        var expected=DuocaiHintPresentation.DisplayTiles(physicalWaiting);
        var labels=view.GetComponentsInChildren<TMP_Text>(true).Select(t=>t.text).ToArray();
        return new JObject{["passed"]=cards.SequenceEqual(expected)&&!labels.Contains("…"),["actual"]=new JArray(cards),["expected"]=new JArray(expected),["labels"]=new JArray(labels)}.ToString();
    }
}
