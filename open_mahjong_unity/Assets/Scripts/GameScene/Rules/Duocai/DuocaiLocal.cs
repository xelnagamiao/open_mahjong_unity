using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

internal sealed class DuocaiLocal {
    private readonly DuocaiMeld[] melds;
    private readonly string variant;
    private readonly JObject context;
    private readonly Dictionary<string,int[]> waits=new Dictionary<string,int[]>(),logicalWaits=new Dictionary<string,int[]>();
    internal DuocaiLocal(List<string> codes,JObject context) {
        this.context=context;
        melds=context["physical_melds"]?.ToObject<DuocaiMeld[]>()??Array.Empty<DuocaiMeld>();
        variant=(string)context["variant"]??"guobiao";
        if(variant!="guobiao"&&variant!="riichi")throw new ArgumentException("夺彩仅开放国标与立直");
    }
    private bool Flag(string key)=>(bool?)context[key]??false;
    private int Number(string key,int fallback=0)=>(int?)context[key]??fallback;
    private int[] Values(string key)=>context[key]?.ToObject<int[]>()??Array.Empty<int>();
    private static string Key(IEnumerable<int> hand)=>string.Join(",",hand.OrderBy(v=>v));
    private bool Structural(int[] hand)=>DuocaiSolver.Shapes(hand,melds,variant).Any();
    internal int[] Waiting(List<int> hand) {
        string key=Key(hand);if(waits.TryGetValue(key,out var found))return found;
        return waits[key]=DuocaiTiles.Physical.Where(t=>Structural(hand.Concat(new[]{t}).ToArray())).ToArray();
    }
    private int[] LogicalWaits(int[] hand) {
        string key=Key(hand);if(logicalWaits.TryGetValue(key,out var found))return found;
        return logicalWaits[key]=DuocaiTiles.Faces.Where(t=>DuocaiTiles.Physical.Any(v=>!DuocaiTiles.Joker(v)&&DuocaiTiles.Face(v)==t&&Structural(hand.Concat(new[]{v}).ToArray()))).ToArray();
    }
    private bool Furiten(int[] hand,int[] discards,int win,DuocaiScoring.Context ctx) {
        var discarded=new HashSet<int>();var conditional=new HashSet<int>();
        foreach(int tile in discards) {
            if(tile==DuocaiTiles.Chow||tile==DuocaiTiles.Pung)continue;
            if(tile==DuocaiTiles.Mangan||tile==DuocaiTiles.Odd||tile==DuocaiTiles.Even)conditional.Add(tile);
            else discarded.UnionWith(DuocaiTiles.Domain(tile));
        }
        if(discarded.Count>0&&LogicalWaits(hand).Any(discarded.Contains))return true;
        if(conditional.Count==0)return false;
        var effective=DuocaiSolver.Shapes(hand.Concat(new[]{win}).ToArray(),melds,"riichi").Select(s=>DuocaiScoring.Score(s,"riichi",ctx)).Where(s=>s.Valid).ToArray();
        return effective.Length>0&&conditional.Any(t=>effective.All(s=>DuocaiScoring.Conditions(new[]{t},s.Visible,s.Fu,"riichi")));
    }
    internal static int Points(DuocaiScoring.Quote q,bool self,bool dealer,int honba) {
        if(q==null||!q.Valid||q.Fan<=0)return 0;
        if(q.Variant=="guobiao")return self?3*(100+q.Fan):300+q.Fan;
        int Round(int v)=>(v+99)/100*100;
        return (self?dealer?3*Round(q.Base*2):Round(q.Base*2)+2*Round(q.Base):Round(q.Base*(dealer?6:4)))+300*honba;
    }
    internal JObject Describe(List<int> source,int win,int? cut=null) {
        var hand=source.ToArray();var before=hand.Take(hand.Length-1).ToArray();var discards=Values("discards").Concat(cut.HasValue?new[]{cut.Value}:Array.Empty<int>()).ToArray();
        DuocaiScoring.Context Ctx(bool self) {
            return new DuocaiScoring.Context {Tsumo=self,Seat=Number("seat_wind",42),Wind=Number("round_wind",41),Riichi=Number("riichi"),
                Ippatsu=!cut.HasValue&&Flag("ippatsu"),Indicators=Values("indicators"),LastCopy=DuocaiTiles.Face(win)!=0&&(Values("visible_tiles").Count(v=>DuocaiTiles.Face(v)==DuocaiTiles.Face(win))+(cut.HasValue&&DuocaiTiles.Face(cut.Value)==DuocaiTiles.Face(win)?1:0))==3,
                Rinshan=self&&!cut.HasValue&&Flag("has_draw_slot")&&Flag("after_kong"),
                Last=self?(!cut.HasValue&&Flag("has_draw_slot")?Number("remaining",-1)==0:Number("remaining",-1)==1):Number("remaining",-1)==0};
        }
        var ronContext=Ctx(false);var ron=DuocaiScoring.Evaluate(hand,melds,variant,ronContext);var self=DuocaiScoring.Evaluate(hand,melds,variant,Ctx(true));
        bool furiten=variant=="riichi"&&(Flag("riichi_furiten")||!cut.HasValue&&Flag("temporary_furiten")||Furiten(before,discards,win,ronContext));
        bool canRon=ron?.Valid==true&&!furiten&&!Flag("blocked"),canSelf=self?.Valid==true&&!Flag("blocked");
        int rp=Points(ron,false,Number("seat_wind",42)==41,Number("honba")),sp=Points(self,true,Number("seat_wind",42)==41,Number("honba"));
        string kind="wuyi",label="未满足";int points=0;
        if(canRon){kind=canSelf?"dianhe":"ron_only";points=rp;label=canSelf?$"{rp}点":$"仅点和\n{rp}点";}
        else if(canSelf){kind="zimo";points=sp;label=$"仅自摸\n{sp}点";}
        else if((ron?.Conditions==true&&!ron.Minimum||self?.Conditions==true&&!self.Minimum)&&!Flag("blocked"))label="未起和";
        return new JObject{["tile"]=win,["kind"]=kind,["label"]=label,["points"]=points,["ron_points"]=canRon?rp:0,["self_points"]=canSelf?sp:0,["can_ron"]=canRon,["can_self"]=canSelf,
            ["fan"]=canRon?ron.Fan:canSelf?self.Fan:Math.Max(ron?.Fan??0,self?.Fan??0),["furiten"]=furiten,
            ["remaining"]=Math.Max(0,DuocaiTiles.Supply(win)-Values("visible_tiles").Count(v=>v==win)-before.Count(v=>v==win)-(cut==win?1:0))};
    }
}
