using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

public sealed class DuocaiHintsCache {
    private DuocaiHints hints;
    public void Reset(){hints=null;}
    public void Accept(DuocaiHints value){if(value?.source_hand_tiles!=null)hints=value;}
    private bool Resolve(List<int> hand,List<string> melds,out int? cut) {
        cut=null;
        if(hand==null||hints?.local_request==null||!(hints.source_melds??Array.Empty<string>()).OrderBy(v=>v).SequenceEqual((melds??new List<string>()).OrderBy(v=>v)))return false;
        var rest=new List<int>(hints.source_hand_tiles);foreach(int value in hand)if(!rest.Remove(value))return false;
        if(rest.Count>1)return false;
        if(rest.Count==1)cut=rest[0];
        return true;
    }
    public int CurrentDrawTile => (string)hints?.local_request?["context"]?["win_source"]=="self_draw"
        ?(int?)hints.local_request["context"]["current_win_tile"]??0:0;
    private JObject Request(string scope,List<int> hand,List<string> melds,int? cut=null,int? win=null,bool pendingRiichi=false) {
        var context=scope=="waiting"?new JObject{["variant"]=hints.local_request["context"]["variant"],
            ["physical_melds"]=hints.local_request["context"]["physical_melds"]?.DeepClone()}:(JObject)hints.local_request["context"].DeepClone();
        if(pendingRiichi)context["pending_riichi"]=true;
        return new JObject {["rule"]="duocai",["scope"]=scope,["hand"]=new JArray(hand),["melds"]=new JArray(melds??new List<string>()),
            ["context"]=context,["cut"]=cut,["win"]=win};
    }
    public HashSet<int> Waiting(TingpaiQuery q) {
        if(q==null||!Resolve(q.Hand,q.Melds,out var cut))return new HashSet<int>();
        var result=LocalHintSnapshots.Read<JObject>(Request("waiting",q.Hand,q.Melds,cut),q.OnReady);
        return new HashSet<int>(result?["waiting_tiles"]?.ToObject<int[]>()??Array.Empty<int>());
    }
    public WaitTileHint Describe(WaitHintQuery q,bool blocked=false) {
        if(q?.HandWithWin==null)return WaitTileHint.None("未满足");
        var before=new List<int>(q.HandWithWin);if(!before.Remove(q.HepaiTile)||!Resolve(before,q.Melds,out var cut))return WaitTileHint.None("未满足");
        if(blocked||hints.win_blocked)return WaitTileHint.None("未满足");
        cut=q.PendingCutTileId??(cut==CurrentDrawTile?null:cut);
        bool pending=q.Record==null&&RiichiCutSelectionController.Instance?.IsActive==true;
        var rows=new List<(int tile,WaitTileHint hint)>();
        foreach(int tile in q.WaitingVariants??new[]{q.HepaiTile}) {
            var complete=before.Concat(new[]{tile}).ToList();
            var result=LocalHintSnapshots.Read<JObject>(Request("describe",complete,q.Melds,cut,tile,pending),refreshTips:false);
            var row=result?["row"];
            if(row==null)return null;
            rows.Add((tile,new WaitTileHint{Kind=(string)row["kind"],Label=(string)row["label"]}));
        }
        return DuocaiHintPresentation.Merge(rows);
    }
}
