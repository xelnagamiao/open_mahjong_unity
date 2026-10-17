using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>Pure client calculations from own hand and public rule context.</summary>
internal static partial class RegionalLocalCalculator {
    private static readonly int[] Numbers=LocalJokerHand.Tiles.Where(t=>t<40).ToArray();
    internal static int Int(JObject c,string key,int fallback=0)=>(int?)c[key]??fallback;
    internal static bool Bool(JObject c,string key)=>(bool?)c[key]??false;
    internal static int[] Array(JObject c,string key)=>c[key]?.ToObject<int[]>()??System.Array.Empty<int>();
    private static LocalJokerHand.Policy Policy(string rule,JObject c,bool natural=false) {
        var p=new LocalJokerHand.Policy();
        switch(rule) {
            case "wuhan":var special=WuhanSpecial(Int(c,"indicator"));p.Jokers=new[]{special.joker};p.Logical=LocalJokerHand.Tiles.Except(special.skins).ToArray();p.ClosedCap=4;break;
            case "nanjing":p.Physical=p.Logical=Numbers.Concat(new[]{41,42,43,44}).ToArray();p.SevenPairs=true;p.MeldKinds="kgG";break;
            case "jiujiang":p.Physical=p.Logical=Numbers.Concat(new[]{45,46}).ToArray();p.SevenPairs=true;break;
            case "singapore":p.OrphanShape=true;break;
            case "hangzhou":p.Jokers=new[]{46};p.SevenPairs=true;break;
            case "hongzhong":p.Jokers=new[]{45};p.Physical=Numbers.Concat(new[]{45}).ToArray();p.Logical=Numbers;p.ClosedCap=4;p.SevenPairs=true;p.MeldKinds="kgG";break;
            case "fuzhou":int jin=Int(c,"jin");p.Jokers=new[]{jin};p.Physical=Numbers;p.Logical=Numbers;p.Supply[jin]=3;p.MeldCount=5;break;
            case "malaysia":p.Jokers=new[]{81};p.Logical=Enumerable.Range(21,9).Concat(Enumerable.Range(41,7)).ToArray();p.Physical=p.Logical.Concat(new[]{81}).ToArray();p.Supply[81]=4;p.SevenPairs=true;p.AllowExposedJokers=true;break;
            case "wenzhou":int cai=Int(c,"caishen");p.Jokers=new[]{cai};p.Map[46]=cai;p.Supply[cai]=3;p.MeldCount=5;break;
            default:throw new ArgumentException("Unsupported local rule: "+rule);
        }
        if(c["meld_records"] is JArray records)p.MeldPhysical=records.Select(row=>row["physical"].ToObject<int[]>().Zip(row["logical"].ToObject<int[]>(),(a,b)=>(physical:a,logical:b)).OrderBy(x=>x.logical).Select(x=>x.physical).ToArray()).ToArray();
        if(natural)p.Jokers=System.Array.Empty<int>();
        return p;
    }
    private static LocalJokerHand.Hand Prepare(string rule,List<int> hand,List<string> melds,JObject c,bool complete=true,bool natural=false,int? tail=null) {
        var p=Policy(rule,c,natural);
        if((rule=="fuzhou"&&!Numbers.Contains(Int(c,"jin")))||(rule=="wenzhou"&&!LocalJokerHand.Tiles.Contains(Int(c,"caishen"))))return null;
        return LocalJokerHand.Prepare(hand,melds,p,complete,tail);
    }
    private static bool EightPairs(LocalJokerHand.Hand h) {
        if(h==null||h.Melds.Count>0||h.Counts.Sum()+h.Ghosts!=17)return false;
        int need=Math.Abs(h.Counts.Sum(n=>n%2)-1);return need<=h.Ghosts&&(h.Ghosts-need)%2==0;
    }
    private static bool Complete(string rule,List<int> hand,List<string> melds,JObject c) {
        var h=Prepare(rule,hand,melds,c);return h!=null&&(LocalJokerHand.CanWin(h)||rule=="wenzhou"&&(h.Ghosts>=3||EightPairs(h)));
    }
    internal static int[] Waiting(string rule,List<int> hand,List<string> melds,JObject c) {
        if(rule=="changsha_exchange_six")return hand.Count!=13-3*melds.Count?System.Array.Empty<int>():ChangshaExchangeSixShape.Tiles.Where(t=>ChangshaExchangeSixTips.ComputeRow(hand.Concat(new[]{t}).ToList(),melds,t,c)!=null).ToArray();
        if(rule=="hongkong")return new HongKongLocal(melds,c).Waiting(hand);
        if(rule=="shenyang")return ShenyangWaiting(hand,melds,false);
        if(rule=="nanchang")return new NanchangLocal(melds,c).Waiting(hand);
        if(rule=="wuhan")return WuhanWaiting(hand,melds,c);
        if(rule=="guangdong")return GuangdongBaseHints.Waiting(hand,melds).OrderBy(t=>t).ToArray();
        var h=Prepare(rule,hand,melds,c,false);if(h==null)return System.Array.Empty<int>();
        return h.Policy.Physical.Where(t=>Complete(rule,hand.Concat(new[]{t}).ToList(),melds,c)).ToArray();
    }
    private static int HangzhouScore(List<int> hand,List<string> melds,int win,JObject c,int? cut) {
        var h=Prepare("hangzhou",hand,melds,c);if(h==null||!LocalJokerHand.CanWin(h))return -1;
        bool pairs=melds.Count==0&&LocalJokerHand.CanFormPairs(h);
        bool luxury=hand.Any(t=>t!=46&&hand.Count(x=>x==t)==4)||hand.Count(t=>t==46)==4&&hand.Where(t=>t!=46).All(t=>hand.Count(x=>x==t)%2==0);
        int fan=pairs?(luxury?4:2):0;if(!hand.Contains(46))fan++;
        var before=new List<int>(hand);before.Remove(win);
        if(before.Remove(46)) {
            var rest=Prepare("hangzhou",before,melds,c,tail:0);
            if(LocalJokerHand.CanFormMelds(rest)||melds.Count==0&&LocalJokerHand.CanFormPairs(rest))fan+=1+(cut.HasValue?(cut==46?Int(c,"cai_piao_count")+1:0):Int(c,"cai_piao_count"));
        }
        return Math.Min(4,fan);
    }
    private static int HongzhongScore(List<int> hand,List<string> melds,int win,JObject c) {
        var h=Prepare("hongzhong",hand,melds,c);int best=-1;if(h==null)return best;
        var external=melds.SelectMany(m=>LocalJokerHand.MeldTiles(m)).ToArray();
        foreach(var shape in LocalJokerHand.Shapes(h))foreach(int logicalWin in LocalJokerHand.WinningTiles(h,shape,win)) {
            int fan=hand.Contains(45)?0:1;var all=shape.Tiles.Concat(external).ToArray();
            if(all.Select(t=>t/10).Distinct().Count()==1)fan+=2;
            if(shape.Kind=="seven_pairs")fan+=shape.Logical[LocalJokerHand.Index[logicalWin]]==4?3:2;
            else {if(shape.Closed.All(s=>s[0]!='S'))fan++;if(Enumerable.Range(1,3).Any(s=>new[]{"S"+(s*10+2),"S"+(s*10+5),"S"+(s*10+8)}.All(shape.Closed.Contains)))fan++;}
            best=Math.Max(best,Math.Min(4,fan));if(best==4)return best;
        }
        return best;
    }
    private static bool AllPungs(List<int> hand,List<string> melds,int jin,bool pair) {
        if(melds.Any(m=>m[0]=='s'))return false;
        var counts=hand.Where(t=>t!=jin).GroupBy(t=>t).ToDictionary(g=>g.Key,g=>g.Count());int gold=hand.Count(t=>t==jin);
        foreach(int choice in pair?new[]{0}.Concat(counts.Keys):new[]{0}) {
            int cost=pair?Math.Max(0,2-counts.GetValueOrDefault(choice)):0;
            int need=counts.Sum(kv=>(3-Math.Max(0,kv.Value-(kv.Key==choice?2:0))%3)%3);
            if(cost+need<=gold&&(gold-cost-need)%3==0)return true;
        }
        return false;
    }
    private static int FuzhouScore(List<int> hand,List<string> melds,JObject c) {
        int jin=Int(c,"jin");if(!Complete("fuzhou",hand,melds,c))return 0;
        int gold=hand.Count(t=>t==jin),patterns=0;
        foreach(int size in new[]{3,2,0}) {
            if(gold<size)continue;var rest=hand.Where(t=>t!=jin).Concat(Enumerable.Repeat(jin,gold-size)).ToList();
            if(size==3){var policy=Policy("fuzhou",c);policy.MeldCount=4;if(!LocalJokerHand.CanWin(LocalJokerHand.Prepare(rest,melds,policy)))continue;}
            if(size==2&&!LocalJokerHand.CanFormMelds(Prepare("fuzhou",rest,melds,c,tail:0)))continue;
            patterns=(size==3?120:size==2?60:0)+(AllPungs(rest,melds,jin,size!=2)?15:0);break;
        }
        if(hand.Count==2&&melds.Count==5)patterns+=60;
        var physical=hand.Concat(melds.SelectMany(m=>LocalJokerHand.MeldTiles(m))).ToArray();if(physical.Where(t=>t!=jin).Select(t=>t/10).Distinct().Count()==1)patterns+=gold>0?30:60;
        var flowers=Array(c,"flowers");int sets=Enumerable.Range(41,7).Count(t=>flowers.Count(x=>x==t)==4)+new[]{51,55}.Count(start=>Enumerable.Range(start,4).All(flowers.Contains));
        if(flowers.Length==1)patterns+=15;if(flowers.Length==0&&!melds.Any(m=>"gG".Contains(m[0])))patterns+=30;
        if(Bool(c,"golden_middle"))patterns+=40;patterns+=60*Int(c,"youjin");
        return Int(c,"base_score",15)+flowers.Length+sets*2+gold+melds.Sum(m=>m[0]=='G'?2:m[0]=='g'?1:0)+Int(c,"dealer_streak")*3+patterns;
    }
    private static int WenzhouScore(List<int> hand,List<string> melds,JObject c,bool self) {
        var h=Prepare("wenzhou",hand,melds,c);if(h==null)return 0;
        bool standard=LocalJokerHand.CanWin(h),eight=EightPairs(h);var natural=Prepare("wenzhou",hand,melds,c,natural:true);
        bool naturalStandard=LocalJokerHand.CanWin(natural),naturalEight=EightPairs(natural);int value=0;
        if(standard)value=self||naturalStandard?2:1;if(eight)value=Math.Max(value,self||naturalEight?4:1);
        if(h.Ghosts>=3)value=Math.Max(value,standard||eight?4:2);return value;
    }
    internal static JObject Describe(string rule,List<int> hand,List<string> melds,int win,JObject c,int? cut=null) {
        switch(rule) {
            case "changsha_exchange_six":return JObject.FromObject(ChangshaExchangeSixTips.ComputeRow(hand,melds,win,c));
            case "nanchang":return new NanchangLocal(melds,c).Describe(hand,win,cut);
            case "hongkong":return new HongKongLocal(melds,c).Describe(hand,win);
            case "shenyang":return ShenyangDescribe(hand,melds,win,c,cut);
            case "wuhan":return WuhanDescribe(hand,melds,win,c);
            case "nanjing":return NanjingDescribe(hand,melds,win,c,cut);
            case "jiujiang":case "singapore": {
                int ron=rule=="jiujiang"?JiujiangScore(hand,melds,win,c,false,cut):SingaporeScore(hand,melds,win,c,false);
                int self=ron>0?0:rule=="jiujiang"?JiujiangScore(hand,melds,win,c,true,cut):SingaporeScore(hand,melds,win,c,true);
                return new JObject{["points"]=Math.Max(ron,self),["kind"]=ron>0?"dianhe":self>0?"zimo":"wuyi"};
            }
            case "guangdong": {
                int discarded=Int(c,"discarded_ghosts")+(cut>=55&&cut<=58?1:0);
                var row=GuangdongBaseHints.Evaluate(hand,melds,win,(bool?)c["require_minimum_score"]??true,discarded);var result=JObject.FromObject(row);
                result["kind"]=row.ron?"dianhe":row.self_draw?"zimo":"wuyi";
                result["label"]=row.ron?$"点和{row.ron_fan}番{row.ron_score}分"+(row.self_draw?$"\n自摸{row.self_draw_fan}番{row.self_draw_score}分":""):row.self_draw?$"自摸{row.self_draw_fan}番{row.self_draw_score}分":"未起和";
                return result;
            }
            case "hangzhou":case "hongzhong": {
                int fan=rule=="hangzhou"?HangzhouScore(hand,melds,win,c,cut):HongzhongScore(hand,melds,win,c);
                return new JObject{["kind"]=fan<0||Bool(c,"blocked")?"wuyi":"zimo",["fan"]=Math.Max(0,fan),["base_score"]=fan<0?0:1<<fan,["label"]=fan<0||Bool(c,"blocked")?"未满足":$"自摸{fan}番{1<<fan}分"};
            }
            case "fuzhou": {
                bool limited=Array(c,"locked_waits").Length>0&&!Array(c,"locked_waits").Contains(win);
                bool self=Bool(c,"discarded_gold")||cut==Int(c,"jin")||Bool(c,"golden_middle")||Bool(c,"youjin_active")||win==Int(c,"jin")||Bool(c,"last_four");
                int points=limited||Bool(c,"blocked")?0:FuzhouScore(hand,melds,c);
                return new JObject{["kind"]=points==0?"unmet":self?"self_only":"ron",["points"]=points,["label"]=points==0?"未满足":points+"分"};
            }
            case "wenzhou": {
                int ron=WenzhouScore(hand,melds,c,false),self=WenzhouScore(hand,melds,c,true),threshold=Int(c,"passed_multiplier");
                if(cut.HasValue){var declined=new List<int>(hand);declined.Remove(win);declined.Add(cut.Value);threshold=Math.Max(threshold,WenzhouScore(declined,melds,c,false));}
                return new JObject{["tile"]=win,["ron"]=ron>threshold,["self_draw"]=self>0,["ron_multiplier"]=ron,["self_draw_multiplier"]=self,["kind"]=ron>threshold?"dianhe":self>0?"zimo":"wuyi",["label"]=ron>threshold?$"点和{ron}倍\n自摸{self}倍":self>0?$"自摸{self}倍":"未满足"};
            }
            case "malaysia":return new JObject{["kind"]="wuyi",["label"]="结构听牌 · 须满5番或满番"};
            default:throw new ArgumentException("Unsupported local hint rule: "+rule);
        }
    }
    internal static JObject Snapshot(JObject request) {
        var result=new JObject();
        foreach(var step in SnapshotSteps(request,result)) { }
        return result;
    }
    // Unity WebGL has no managed worker threads. Its managed calculators yield
    // between exact wait quotes; the other rules use the shared JavaScript worker.
    internal static IEnumerable<int> SnapshotSteps(JObject request,JObject result) {
        string rule=(string)request["rule"];var hand=request["hand"].ToObject<List<int>>();var melds=request["melds"].ToObject<List<string>>();var c=(JObject)request["context"]??new JObject();
        result["source_hand_tiles"]=new JArray(hand);result["source_melds"]=new JArray(melds);result["waiting_tiles"]=new JArray();result["waiting_details"]=new JObject();result["waiting_by_discard"]=new JObject();result["waiting_details_by_discard"]=new JObject();
        result["win_blocked"]=Bool(c,"blocked");
        var nanchang=rule=="nanchang"?new NanchangLocal(melds,c):null;
        var hongkong=rule=="hongkong"?new HongKongLocal(melds,c):null;
        IEnumerable<int> Rows(List<int> before,int? cut) {
            var waits=nanchang?.Waiting(before)??hongkong?.Waiting(before)??Waiting(rule,before,melds,c);
            yield return 0;
            var details=new JObject();
            foreach(int tile in waits) {
                var row=nanchang?.Describe(before.Concat(new[]{tile}).ToList(),tile,cut)??hongkong?.Describe(before.Concat(new[]{tile}).ToList(),tile)??Describe(rule,before.Concat(new[]{tile}).ToList(),melds,tile,c,cut);
                row["tile"]=tile;
                details[tile.ToString()]=row;
                yield return 0;
            }
            if(cut.HasValue){result["waiting_by_discard"][cut.Value.ToString()]=new JArray(waits);result["waiting_details_by_discard"][cut.Value.ToString()]=details;}
            else {result["waiting_tiles"]=new JArray(waits);result["waiting_details"]=details;}
        }
        if(hand.Count%3==1){foreach(var step in Rows(hand,null))yield return step;}
        else if(hand.Count%3==2)foreach(int cut in request["cuts"]?.ToObject<int[]>()??hand.Distinct().OrderBy(t=>t).ToArray()){var before=new List<int>(hand);if(before.Remove(cut))foreach(var step in Rows(before,cut))yield return step;}
    }
}
