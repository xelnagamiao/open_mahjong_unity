#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public static class ShenyangClientValidation {
    public static string Run() {
        ShenyangRuleBootstrap.Register();
        var checks=new List<string>();
        void Check(bool ok,string label) {if(!ok) throw new Exception("Shenyang: "+label);checks.Add(label);}
        var manifest=RuleRegistry.Resolve("shenyang","shenyang/mil2025");
        Check(manifest!=null && manifest.DisplayName=="沈阳麻将","rule registration");
        Check(!manifest.HasFlowerReplacement && manifest.FaceDownAnkan,"136 tiles and hidden concealed kongs");
        Check(manifest.OutboundChannel=="shenyang" && manifest.DefaultHepaiLimit==0,"wire route and zero minimum");
        Check(ShenyangRuleBootstrap.SpecialTiles("Ydragon").SequenceEqual(new[]{45,46,47}),"dragon group physical identities");
        Check(ShenyangRuleBootstrap.SpecialTiles("Ywind").SequenceEqual(new[]{41,42,43,44}),"wind group physical identities");
        foreach(string action in new[]{"sy_xi","sy_wind","sy_reveal","sy_hide","sy_pass"})
            Check(ActionWords.TryGet(action,out var spec) && (action=="sy_pass"?spec.Kind==ActionWordKind.Pass:spec.BlocksAutoCut),"action registered "+action);
        var cache=new ShenyangHintsCache();
        var hand=new List<int>{11,12,13,21,22,23,31,32,33,45,45,45,19};
        foreach(var row in new[]{new[]{"unmet","未满足",WaitTileHint.KindNone},new[]{"below","未起和",WaitTileHint.KindNone},
                new[]{"self_draw","仅自摸",WaitTileHint.KindTsumoOnly},new[]{"win","点和32分\n自摸80分",WaitTileHint.KindRon}}) {
            var json=JsonConvert.SerializeObject(new ShenyangHints{source_hand_tiles=hand.ToArray(),source_melds=Array.Empty<string>(),
                waiting=new Dictionary<int,ShenyangWaitDetail>{{19,new ShenyangWaitDetail{status=row[0],label=row[1]}}}});
            cache.Accept(JsonConvert.DeserializeObject<ShenyangHints>(json));
            var q=new TingpaiQuery{Hand=hand,Melds=new List<string>()};
            Check(cache.Waiting(q).SetEquals(new[]{19}),"waiting snapshot "+row[0]);
            var hint=cache.Describe(new WaitHintQuery{HandWithWin=hand.Concat(new[]{19}).ToList(),HepaiTile=19,Melds=new List<string>()});
            Check(hint.Label==row[1] && hint.Kind==row[2],"hint label and color "+row[0]);
            Check(cache.Waiting(new TingpaiQuery{Hand=hand.Take(12).Concat(new[]{18}).ToList(),Melds=q.Melds}).Count==0,"reject stale hand "+row[0]);
            Check(cache.Waiting(new TingpaiQuery{Hand=hand,Melds=new List<string>{"Ydragon"}}).Count==0,"reject stale meld "+row[0]);
        }
        for(int rotation=0;rotation<4;rotation++) {
            var seats=Enumerable.Range(0,4).Select(i=>(i+rotation)%4).ToList();
            var round=new Round{seats=seats};
            GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round,new List<string>{"sy","{\"kind\":\"egg\",\"changes\":{\"0\":15,\"1\":-5,\"2\":-5,\"3\":-5}}"});
            Check(round.scoreChanges.SequenceEqual(seats.Select(s=>s==0?15:-5)),"egg scores follow original seat "+rotation);
            GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round,new List<string>{"hu_self","0","6","[]","[192,-64,-64,-64]","19"});
            Check(round.scoreChanges.SequenceEqual(seats.Select(s=>s==0?207:-69)),"egg plus win not doubled "+rotation);
            GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round,new List<string>{"sy","{\"kind\":\"settlement\",\"round_score_changes\":{\"0\":207,\"1\":-69,\"2\":-69,\"3\":-69}}"});
            Check(round.scoreChanges.Sum()==0,"settlement preserves zero sum "+rotation);
        }
        return JsonConvert.SerializeObject(new{passed=checks.Count,checks},Formatting.Indented);
    }
}
#endif
