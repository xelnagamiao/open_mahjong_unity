using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public static partial class ScoreHistoryRecordSettlementExtractor {
    private static bool ApplyChangchunScoreTick(List<string> tick,SimPlayer[] players,int[] scores,ref int winningTile,ref int currentPlayer) {
        if(tick.Count<2 || tick[0]!="cc") return false;
        var e=JObject.Parse(tick[1]);string kind=(string)e["kind"];
        int index=(int?)e["player"]??-1;var p=index>=0 && index<4?players[index]:null;
        if(kind=="kong_score") {
            foreach(var delta in e["delta"].ToObject<Dictionary<int,int>>()) if(delta.Key>=0 && delta.Key<4) scores[delta.Key]+=delta.Value;
        } else if(kind=="special" && p!=null) {
            var tiles=e["physical"].ToObject<int[]>();foreach(int tile in tiles) RemoveOneTile(p.tileList,tile);
            p.combinationTiles.Add((string)e["code"]);p.combinationMasks.Add(tiles.SelectMany(t=>new[]{0,t}).ToArray());
        } else if((kind=="added_offer" || kind=="tail_pass") && p!=null) {
            if(e["tile"]!=null) RemoveOneTile(p.tileList,(int)e["tile"]);
            if(kind=="tail_pass") currentPlayer=(index+1)%4;
        } else if(kind=="added_commit" && p!=null) {
            int pos=(int)e["position"];string code=(string)e["code"];
            p.combinationTiles[pos]=code;p.combinationMasks[pos]=ChangchunHandCalculator.MeldTiles(code).SelectMany(t=>new[]{0,t}).ToArray();
        } else if(kind=="added_robbed" && p!=null && (bool?)e["special"]!=true) {
            int tile=(int)e["tile"];int pos=p.combinationTiles.IndexOf("g"+tile);
            if(pos>=0) {
                p.combinationTiles[pos]="k"+tile;var mask=p.combinationMasks[pos].ToList();
                int offset=Enumerable.Range(0,mask.Count/2).FirstOrDefault(i=>mask[2*i]==3)*2;
                mask.RemoveRange(offset,2);p.combinationMasks[pos]=mask.ToArray();
            }
        } else if(kind=="settlement") {
            winningTile=(int?)e["tile"]??-1;
            if((string)e["source"]=="bao_indicator" && winningTile>0) players[(int)e["winner"]].tileList.Add(winningTile);
        }
        return true;
    }
}
