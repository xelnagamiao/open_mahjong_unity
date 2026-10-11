using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public static partial class ScoreHistoryRecordSettlementExtractor {
    private static bool ApplyShenyangScoreTick(List<string> tick,SimPlayer[] players,int[] scores,ref int currentPlayer) {
        if(tick.Count<2 || tick[0]!="sy") return false;
        var e=JObject.Parse(tick[1]);string kind=(string)e["kind"];
        int actor=(int?)e["player"]??-1;
        var p=actor>=0 && actor<4?players[actor]:null;
        if(kind=="egg") {
            foreach(var delta in e["changes"].ToObject<Dictionary<int,int>>()) if(delta.Key>=0 && delta.Key<4) scores[delta.Key]+=delta.Value;
        } else if(kind=="special" && p!=null) {
            foreach(int tile in e["physical"].ToObject<int[]>()) RemoveOneTile(p.tileList,tile);
            p.combinationTiles.Add((string)e["code"]);p.combinationMasks.Add(e["mask"].ToObject<int[]>());
        } else if(kind=="robbed" && p!=null) {
            p.combinationTiles=e["melds"].ToObject<List<string>>();p.combinationMasks=e["masks"].ToObject<List<int[]>>();
        } else if(kind=="tail_pass" && p!=null) {
            RemoveOneTile(p.tileList,(int)e["tile"]);currentPlayer=(actor+1)%4;
        }
        return true;
    }
}
