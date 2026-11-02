using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

internal sealed class BeijingLocal {
    private static readonly int[] Tiles = LocalJokerHand.Tiles;
    private static readonly Dictionary<int,int> Index = Tiles.Select((t,i)=>(t,i)).ToDictionary(x=>x.t,x=>x.i);
    private static readonly int[][] Sequences = Tiles.Where(t=>t<40&&t%10<=7).Select(t=>new[]{t,t+1,t+2}).ToArray();
    private readonly List<string> melds;
    private readonly JObject context, config;
    private readonly int indicator, hun;
    private readonly Dictionary<string,List<int[][]>> memo = new Dictionary<string,List<int[][]>>();
    private readonly Dictionary<string,int[]> waits = new Dictionary<string,int[]>();
    private sealed class Shape { internal bool Seven; internal int[] Pair; internal int[][] Groups; }

    internal BeijingLocal(List<string> melds,JObject context) {
        this.melds=melds??new List<string>();this.context=context??new JObject();
        config=this.context["config"] as JObject??new JObject();
        indicator=(int?)this.context["indicator"]??0;hun=Hun(indicator,(string)config["hun_mode"]??"up");
    }
    internal static int Hun(int indicator,string mode="up") {
        if(!Index.ContainsKey(indicator)||!new[]{"up","down","self"}.Contains(mode))return 0;
        if(mode=="self")return indicator;
        int[] cycle=indicator<40?Enumerable.Range(1,9).Select(n=>indicator/10*10+n).ToArray():indicator<45?new[]{41,42,43,44}:new[]{45,47,46};
        return cycle[(Array.IndexOf(cycle,indicator)+(mode=="up"?1:cycle.Length-1))%cycle.Length];
    }
    private bool Option(string key)=>(bool?)config[key]??false;
    private bool Flag(string key)=>(bool?)context[key]??false;
    private int Number(string key,int fallback=0)=>(int?)context[key]??fallback;
    private static int[] Meld(string code) {
        if(code==null||code.Length!=3||!"skgG".Contains(code[0])||!int.TryParse(code.Substring(1),out int tile)||!Index.ContainsKey(tile))return null;
        if(code[0]!='s')return Enumerable.Repeat(tile,"gG".Contains(code[0])?4:3).ToArray();
        return tile<40&&tile%10>=2&&tile%10<=8?new[]{tile-1,tile,tile+1}:null;
    }
    private bool Valid(List<int> hand,bool complete=true) {
        if(hun==0||hand==null||melds.Count>4||hand.Count+melds.Count*3!=(complete?14:13))return false;
        var physical=new List<int>(hand);
        foreach(string code in melds){var group=Meld(code);if(group==null||group.Contains(hun))return false;physical.AddRange(group);}
        return physical.GroupBy(t=>t).All(g=>Index.ContainsKey(g.Key)&&g.Count()<=(g.Key==indicator?3:4));
    }
    private List<int[][]> Groups(int[] counts,int wild,int remaining) {
        if(wild<0||remaining<0||counts.Sum()+wild!=remaining*3)return new List<int[][]>();
        if(remaining==0)return new List<int[][]>{Array.Empty<int[]>()};
        string key=string.Concat(counts.Select(n=>(char)('0'+n)))+"/"+wild+"/"+remaining;
        if(memo.TryGetValue(key,out var cached))return cached;
        int first=Array.FindIndex(counts,n=>n>0);
        var choices=first<0?Tiles.Select(t=>new[]{t,t,t}).Concat(Sequences):new[]{new[]{Tiles[first],Tiles[first],Tiles[first]}}.Concat(Sequences.Where(s=>s.Contains(Tiles[first])));
        var result=new Dictionary<string,int[][]>();
        foreach(var group in choices) {
            var rest=(int[])counts.Clone();int missing=0;
            foreach(int tile in group){int i=Index[tile];if(rest[i]>0)rest[i]--;else missing++;}
            foreach(var tail in Groups(rest,wild-missing,remaining-1)) {
                var combined=new[]{group}.Concat(tail).OrderBy(g=>string.Join(",",g),StringComparer.Ordinal).ToArray();
                result[string.Join("/",combined.Select(g=>string.Join(",",g)))]=combined;
            }
        }
        return memo[key]=result.Values.ToList();
    }
    private IEnumerable<Shape> Shapes(List<int> hand,bool ronHun=false) {
        if(hun==0||hand.Count+melds.Count*3!=14||hand.Any(t=>!Index.ContainsKey(t)))yield break;
        int[] counts=Tiles.Select(t=>hand.Count(x=>x==t)).ToArray();int wild=counts[Index[hun]]-(ronHun?1:0);counts[Index[hun]]=ronHun?1:0;
        var external=melds.Select(Meld).ToArray();if(external.Any(m=>m==null))yield break;
        foreach(int tile in Tiles) {
            int[] rest=(int[])counts.Clone();int used=Math.Min(2,rest[Index[tile]]);rest[Index[tile]]-=used;
            foreach(var groups in Groups(rest,wild-(2-used),4-melds.Count))yield return new Shape{Pair=new[]{tile,tile},Groups=groups.Concat(external).ToArray()};
        }
        if(melds.Count>0)yield break;
        else {
            int spare=wild-counts.Sum(n=>n%2);
            if(spare>=0&&spare%2==0) {
                var paired=Tiles.SelectMany((t,i)=>Enumerable.Repeat(t,(counts[i]+1)/2)).ToList();
                foreach(var extra in ExtraPairs(spare/2,0))yield return new Shape{Seven=true,Pair=Array.Empty<int>(),Groups=paired.Concat(extra).OrderBy(t=>t).Select(t=>new[]{t,t}).ToArray()};
            }
        }
    }
    private static IEnumerable<int[]> ExtraPairs(int count,int start) {
        if(count==0){yield return Array.Empty<int>();yield break;}
        for(int i=start;i<Tiles.Length;i++)foreach(var tail in ExtraPairs(count-1,i))yield return new[]{Tiles[i]}.Concat(tail).ToArray();
    }
    internal int[] LogicalWaits(List<int> hand,bool ron=false) {
        if(hand.Count+melds.Count*3!=13)return Array.Empty<int>();
        string key=string.Join(",",hand.OrderBy(t=>t))+"/"+ron;
        if(waits.TryGetValue(key,out var found))return found;
        return waits[key]=Tiles.Where(t=>Shapes(hand.Concat(new[]{t}).ToList(),ron&&t==hun).Any()).ToArray();
    }
    internal int[] Waiting(List<int> hand) {
        if(!Valid(hand,false))return Array.Empty<int>();
        int wild=hand.Count(t=>t==hun);bool special=Option("four_hun_win");
        var normal=special&&wild==4?Array.Empty<int>():LogicalWaits(hand);
        return Tiles.Where(t=>(normal.Contains(t)||special&&wild+(t==hun?1:0)==4)&&Valid(hand.Concat(new[]{t}).ToList())).ToArray();
    }
    internal JObject Score(List<int> hand,int win,string source="self_draw",JObject state=null,bool physical=true) {
        var c=state??context;bool F(string key)=>(bool?)c[key]??false;int N(string key,int fallback=0)=>(int?)c[key]??fallback;
        bool ron=source=="discard"||source=="rob_kong";
        if(!new[]{"self_draw","discard","rob_kong"}.Contains(source)||F("discarded_hun")||!hand.Contains(win)||(physical&&!Valid(hand)))return null;
        bool closed=melds.All(m=>m.StartsWith("G"));int wild=hand.Count(t=>t==hun)-(ron&&win==hun?1:0);
        if(ron&&(!closed||Option("three_hun_self_draw")&&wild>=3))return null;
        if(F("four_hun"))return !Option("four_hun_win")||ron||hand.Count(t=>t==hun)!=4?null:new JObject {
            ["fan"]=2,["raw_fan"]=2,["points"]=4,["fan_values"]=new JObject{["混儿杠"]=2},["fan_names"]=new JArray("混儿杠"),["substitutions"]=0,["shape"]="four_hun",["logical_tiles"]=new JArray(hand),["hun"]=hun};
        var before=new List<int>(hand);before.Remove(win);var logicalWaits=LogicalWaits(before);
        bool strictFive=win==15&&LogicalWaits(before,true).SequenceEqual(new[]{15});
        var common=new JObject();
        void Add(string name,bool enabled,int value=1){if(enabled&&value>0)common[name]=value;}
        Add("门清",closed);Add("杠上开花",!ron&&F("after_kong"));Add("海底捞月",F("last_tile"));Add("抢杠和",source=="rob_kong");
        Add("天和",!ron&&F("heavenly"),2);Add("天听",F("tian_ting"));Add("地和",F("earthly"));
        Add("自摸",Option("fan_zimo")&&!ron);Add("庄",Option("fan_dealer")&&F("dealer"));Add("连庄",Option("fan_repeat"),N("repeat"));
        Add("混儿悠",Option("fan_all_waits")&&logicalWaits.Length==34);Add("杠",Option("fan_kong"),melds.Count(m=>"gG".Contains(m[0])));
        JObject best=null;
        foreach(var shape in Shapes(hand,ron&&win==hun)) {
            var logical=shape.Pair.Concat(shape.Groups.SelectMany(g=>g)).ToArray();
            var counts=logical.GroupBy(t=>t).ToDictionary(g=>g.Key,g=>g.Count());
            int substitutions=Math.Max(0,wild-Math.Max(0,counts.GetValueOrDefault(hun)-(ron&&win==hun?1:0)));
            var fans=(JObject)common.DeepClone();
            if(substitutions==0)fans["素"]=1;
            if(shape.Seven){fans["七对"]=1;int luxury=counts.Values.Sum(n=>n/4);if(luxury>0)fans["豪华七对"]=luxury;}
            else if(Enumerable.Range(1,3).Any(s=>new[]{1,4,7}.All(n=>shape.Groups.Any(g=>g.SequenceEqual(new[]{s*10+n,s*10+n+1,s*10+n+2})))))fans["一条龙"]=1;
            if(Option("fan_all_pungs")&&!shape.Seven&&shape.Groups.All(g=>g.Distinct().Count()==1))fans["碰碰和"]=1;
            var suits=logical.Select(t=>t/10).Distinct().ToArray();if(suits.Length==1&&suits[0]>=1&&suits[0]<=3)fans["清一色"]=1;
            if(strictFive&&counts.GetValueOrDefault(15)>0)fans["捉五魁"]=1;
            int raw=fans.Properties().Sum(p=>(int)p.Value),fan=Option("half_hun")&&raw>=1?Math.Max(1,raw-substitutions):raw;
            if(best!=null&&(fan<(int)best["fan"]||fan==(int)best["fan"]&&(substitutions>(int)best["substitutions"]||substitutions==(int)best["substitutions"]&&raw<=(int)best["raw_fan"])))continue;
            best=new JObject{["fan"]=fan,["raw_fan"]=raw,["points"]=(long)Math.Pow(2,fan),["fan_values"]=fans,["fan_names"]=new JArray(fans.Properties().Select(p=>p.Name)),
                ["substitutions"]=substitutions,["shape"]=shape.Seven?"seven":"standard",["logical_tiles"]=new JArray(logical),["hun"]=hun};
        }
        return best;
    }
    internal JObject Describe(List<int> hand,int win,int? cut=null) {
        var c=(JObject)context.DeepClone();c["discarded_hun"]=Flag("discarded_hun")||cut==hun;
        c["tian_ting"]=Flag("tian_ting")&&(!cut.HasValue||cut==Number("drawn_tile",-1));
        c["dealer"]=Number("seat",-1)==0;c["heavenly"]=!cut.HasValue&&Flag("opening")&&Number("seat",-1)==0;
        c["earthly"]=!cut.HasValue&&Flag("first_draw")&&Number("seat",0)!=0;
        c["after_kong"]=!cut.HasValue&&Flag("has_draw_slot")&&Flag("after_kong");
        c["last_tile"]=Number("remaining",-1)==0;
        var ron=Score(hand,win,"discard",c);var self=Score(hand,win,"self_draw",c);
        if(Option("four_hun_win")&&hand.Count(t=>t==hun)==4) {
            var special=(JObject)c.DeepClone();special["four_hun"]=true;
            var quote=Score(hand,win,"self_draw",special);
            if(quote!=null&&(self==null||(long)quote["points"]>(long)self["points"]))self=quote;
        }
        bool canRon=ron!=null&&!Flag("blocked"),canSelf=self!=null&&!Flag("blocked");
        long rp=canRon?(long)ron["points"]*(Option("ron_all_pay")?3:1):0,sp=canSelf?(long)self["points"]*3:0;
        string kind=canRon?canSelf?"dianhe":"ron_only":canSelf?"zimo":"wuyi";
        string label=canRon?(canSelf?$"{rp}点":$"仅点和\n{rp}点"):canSelf?$"仅自摸\n{sp}点":"未满足";
        return new JObject{["tile"]=win,["kind"]=kind,["label"]=label,["points"]=canRon?rp:sp,["ron_points"]=rp,["self_points"]=sp,["can_ron"]=canRon,["can_self"]=canSelf,
            ["fan"]=canRon?(int)ron["fan"]:canSelf?(int)self["fan"]:0};
    }
}
