using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

internal static partial class RegionalLocalCalculator {
    private static int[] ShenyangMeld(string code)=>code=="Ydragon"?new[]{45,46,47}:code=="Ywind"?new[]{41,42,43,44}:LocalJokerHand.MeldTiles(code);
    private sealed class SyShape {internal string Kind;internal int Pair;internal string[] Closed;}
    private static IEnumerable<SyShape> ShenyangShapes(List<int> hand,List<string> melds,int win,bool qualify) {
        if(melds.Count>4||hand.Count+3*melds.Count!=14||hand.Any(t=>!LocalJokerHand.Tiles.Contains(t)))yield break;
        var all=new List<int>(hand);foreach(string code in melds){var tiles=ShenyangMeld(code);if(tiles==null)yield break;all.AddRange(tiles);}if(all.GroupBy(t=>t).Any(g=>g.Count()>4))yield break;
        var suits=all.Where(t=>t<40).Select(t=>t/10).Distinct().ToArray();bool eligible=!qualify||all.Any(LocalJokerHand.Orphans.Contains)&&(suits.Length==3||suits.Length==1&&all.All(t=>t<40));
        var h=LocalJokerHand.Prepare(hand,System.Array.Empty<string>(),new LocalJokerHand.Policy{MeldCount=4-melds.Count,SevenPairs=melds.Count==0});
        foreach(var shape in LocalJokerHand.Shapes(h)) {
            if(shape.Kind=="seven_pairs"){yield return new SyShape{Kind="seven_pairs",Closed=System.Array.Empty<string>()};continue;}
            int pair=int.Parse(shape.Closed.First(m=>m[0]=='q').Substring(1));var closed=shape.Closed.Where(m=>m[0]!='q').ToArray();
            if(eligible&&(!qualify||pair>=45||closed.Concat(melds).Any(m=>m[0]!='S'&&m[0]!='s')))yield return new SyShape{Kind="standard",Pair=pair,Closed=closed};
        }
        if(eligible&&win>=45&&win<=47&&melds.Count<4&&new[]{45,46,47}.All(hand.Contains)) {
            var rest=new List<int>(hand);foreach(int t in new[]{45,46,47})rest.Remove(t);
            var reduced=LocalJokerHand.Prepare(rest,System.Array.Empty<string>(),new LocalJokerHand.Policy{MeldCount=3-melds.Count});
            foreach(var shape in LocalJokerHand.Shapes(reduced))yield return new SyShape{Kind="dragon",Pair=int.Parse(shape.Closed.First(m=>m[0]=='q').Substring(1)),Closed=shape.Closed.Where(m=>m[0]!='q').ToArray()};
        }
    }
    private static int[] ShenyangWaiting(List<int> hand,List<string> melds,bool qualify)=>hand.Count+3*melds.Count==13?LocalJokerHand.Tiles.Where(t=>ShenyangShapes(hand.Concat(new[]{t}).ToList(),melds,t,qualify).Any()).ToArray():System.Array.Empty<int>();
    private sealed class SyScore {internal int Raw;internal string Kind;}
    private static SyScore ShenyangScore(List<int> hand,List<string> melds,int win,JObject c,bool self,bool finalFour=false) {
        var before=new List<int>(hand);if(!before.Remove(win))return null;SyScore best=null;
        bool single=ShenyangWaiting(before,melds,true).Length==1;
        bool orphanOnly=LocalJokerHand.Orphans.Contains(win)&&!before.Concat(melds.SelectMany(ShenyangMeld)).Any(LocalJokerHand.Orphans.Contains);
        bool narrow=single&&(orphanOnly||ShenyangWaiting(before,melds,false).Length==1);
        foreach(var shape in ShenyangShapes(hand,melds,win,true)) {
            if(shape.Kind=="dragon"&&!self)continue;int raw=shape.Kind=="seven_pairs"?4:0;
            if(shape.Kind!="seven_pairs"){
                if(hand.Count==2)raw+=2;
                else if(narrow&&(orphanOnly||shape.Kind=="dragon"||shape.Pair==win||shape.Closed.Any(m=>m[0]=='S'&&(int.Parse(m.Substring(1))==win||int.Parse(m.Substring(1))%10==2&&win==int.Parse(m.Substring(1))+1||int.Parse(m.Substring(1))%10==8&&win==int.Parse(m.Substring(1))-1))))raw++;
                if(shape.Closed.Length+melds.Count+(shape.Kind=="dragon"?1:0)==4&&shape.Closed.Concat(melds).All(m=>m[0]!='S'&&m[0]!='s'))raw+=3;
                if(shape.Kind=="dragon")raw+=2;
            }
            var all=hand.Concat(melds.SelectMany(ShenyangMeld)).ToArray();if(all.All(t=>t<40)&&all.Select(t=>t/10).Distinct().Count()==1)raw+=4;
            var counts=new List<int>(hand);var borrowed=Array(c,"borrowed");
            foreach(string code in melds){if(code[0]=='Y'){raw+=2;continue;}int t=int.Parse(code.Substring(1));bool dragon=t>=45;
                if("gG".Contains(code[0])||code[0]=='k'&&borrowed.Contains(t))raw+=dragon?(code[0]=='G'?4:2):code[0]=='G'?2:1;
                else{counts.AddRange(ShenyangMeld(code));if(code[0]=='k'&&dragon)raw++;}}
            raw+=counts.GroupBy(t=>t).Count(g=>g.Count()==4);
            if(shape.Kind!="seven_pairs")raw+=shape.Closed.Count(m=>m[0]=='K'&&int.Parse(m.Substring(1))>=45);
            if(finalFour&&self)raw++;if(!self)raw++;if(Bool(c,"long_hair"))raw++;if(Bool(c,"ready"))raw++;
            if(best==null||raw>best.Raw||raw==best.Raw&&string.CompareOrdinal(shape.Kind,best.Kind)>0)best=new SyScore{Raw=raw,Kind=shape.Kind};
        }
        return best;
    }
    private static int ShenyangFan(SyScore q,int payer,JObject c) {
        int winner=Int(c,"index");var closed=c["closed"]?.ToObject<bool[]>()??new bool[4];int raw=q.Raw+(winner==0||payer==0?1:0);
        if(closed.All(v=>v))raw+=4;else{if(Enumerable.Range(0,4).Where(i=>i!=winner).All(i=>closed[i]))raw+=2;else if(closed[payer])raw++;if(closed[winner]&&q.Kind!="seven_pairs")raw++;}
        return Math.Min(6,raw);
    }
    private static JObject ShenyangDescribe(List<int> hand,List<string> melds,int win,JObject c,int? cut) {
        int threshold=Int(c,"passed_fan",-1),winner=Int(c,"index");var others=Enumerable.Range(0,4).Where(i=>i!=winner).ToArray();
        if(cut.HasValue){var declined=new List<int>(hand);declined.Remove(win);declined.Add(cut.Value);var q=ShenyangScore(declined,melds,cut.Value,c,false);if(q!=null)threshold=Math.Max(threshold,others.Min(p=>ShenyangFan(q,p,c)));}
        var draw=ShenyangScore(hand,melds,win,c,true,Bool(c,"final_four"));if(draw==null)return new JObject{["status"]="unmet",["label"]="未满足"};
        int self=others.Sum(p=>1<<ShenyangFan(draw,p,c));var ron=ShenyangScore(hand,melds,win,c,false);var points=ron==null||Bool(c,"final_four")?System.Array.Empty<int>():others.Where(p=>ShenyangFan(ron,p,c)>threshold).Select(p=>1<<ShenyangFan(ron,p,c)).ToArray();
        if(points.Length==0)return new JObject{["status"]="self_draw",["label"]="仅自摸",["self_draw_points"]=self};
        int low=points.Min(),high=points.Max();return new JObject{["status"]="win",["label"]=$"点和{(low==high?low.ToString():low+"–"+high)}分\n自摸{self}分",["ron_min"]=low,["ron_max"]=high,["self_draw_points"]=self};
    }
}
