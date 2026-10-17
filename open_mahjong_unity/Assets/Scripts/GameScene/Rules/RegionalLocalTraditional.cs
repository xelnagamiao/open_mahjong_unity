using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

internal static partial class RegionalLocalCalculator {
    private static readonly int[] WuhanWinds={41,42,43,44,47,46};
    private static readonly int[] WuhanJiang=Numbers.Where(t=>t%10==2||t%10==5||t%10==8).ToArray();
    private static (int joker,int[] skins) WuhanSpecial(int indicator) {
        if(indicator<40)return (indicator/10*10+indicator%10%9+1,new[]{indicator/10*10+(indicator%10+7)%9+1,indicator,45});
        int index=System.Array.IndexOf(WuhanWinds,indicator==45?44:indicator);
        return (WuhanWinds[(index+1)%6],new[]{WuhanWinds[(index+5)%6],indicator==45?44:indicator,45}.Distinct().ToArray());
    }
    private sealed class WuhanShape {internal int Names;internal bool Gold,Hard,Seed;}
    private static IEnumerable<WuhanShape> WuhanShapes(List<int> hand,List<string> melds,JObject c) {
        if(!LocalJokerHand.Tiles.Contains(Int(c,"indicator")))yield break;
        var special=WuhanSpecial(Int(c,"indicator"));var h=Prepare("wuhan",hand,melds,c);if(h==null||hand.Any(special.skins.Contains))yield break;
        var exposed=melds.SelectMany(LocalJokerHand.MeldTiles).ToArray();if(exposed.Any(t=>special.skins.Contains(t)||t==special.joker))yield break;
        var ordinary=hand.Where(t=>t!=special.joker).Concat(exposed).ToArray();
        foreach(var domain in new[]{WuhanWinds,WuhanJiang})if(ordinary.All(domain.Contains))yield return new WuhanShape{Names=1,Gold=domain==WuhanWinds,Hard=domain.Contains(special.joker)||!hand.Contains(special.joker),Seed=true};
        var physicalCounts=LocalJokerHand.Tiles.Select(t=>hand.Count(x=>x==t)).ToArray();
        foreach(var shape in LocalJokerHand.Shapes(h)) {
            var all=shape.Tiles.Concat(exposed).ToArray();int pair=int.Parse(shape.Closed.First(m=>m[0]=='q').Substring(1));
            bool gold=all.All(WuhanWinds.Contains);
            yield return new WuhanShape{Names=(shape.Closed.Concat(melds).All(m=>m[0]!='S'&&m[0]!='s')?1:0)+(all.All(t=>t<40)&&all.Select(t=>t/10).Distinct().Count()==1?1:0)+(all.All(WuhanJiang.Contains)?1:0)+(gold?1:0),Gold=gold,Hard=shape.Logical.SequenceEqual(physicalCounts),Seed=WuhanJiang.Contains(pair)&&hand.Contains(pair)};
        }
    }
    private static int[] WuhanWaiting(List<int> hand,List<string> melds,JObject c) {
        if(!LocalJokerHand.Tiles.Contains(Int(c,"indicator")))return System.Array.Empty<int>();
        return LocalJokerHand.Tiles.Where(tile=>WuhanShapes(hand.Concat(new[]{tile}).ToList(),melds,c).Any()).ToArray();
    }
    private static int WuhanTableFan(JToken row,int joker) {
        var melds=row["melds"].ToObject<string[]>();var added=row["added"]?.ToObject<int[]>()??System.Array.Empty<int>();
        return melds.Count(m=>m[0]!='G')+melds.Sum(m=>m[0]=='G'?2:m[0]=='g'?(added.Contains(int.Parse(m.Substring(1)))?1:2):0)+row["specials"].ToObject<int[]>().Sum(t=>t==joker?2:t==45?1:0);
    }
    private static int WuhanPayment(WuhanShape shape,int names,JObject c,int? payer) {
        var table=(JArray)c["table"];int winner=Int(c,"index"),joker=WuhanSpecial(Int(c,"indicator")).joker;
        int shapeFan=(names>0?names+2:0)+(shape.Hard?1:0);var others=Enumerable.Range(0,4).Where(i=>i!=winner).ToArray();
        var fans=others.ToDictionary(i=>i,i=>WuhanTableFan(table[winner],joker)+WuhanTableFan(table[i],joker)+shapeFan+(names==0&&(winner==0||i==0)?1:0)+(!payer.HasValue||payer==i?1:0));
        if(!shape.Gold&&fans.Values.Min()<6)return 0;bool gold=shape.Gold||fans.Values.Min()>=10;
        var unopened=others.Where(i=>!table[i]["melds"].Any()&&!table[i]["specials"].Any()).ToArray();int? previous=(int?)c["previous_gold"];
        return others.Sum(i=>(gold?100:new[]{0,0,0,0,0,0,10,20,40,60,80}[Math.Min(10,fans[i])])*(WuhanWinds.Contains(joker)||WuhanJiang.Contains(joker)||gold&&(previous==winner||previous==i)?2:1)*(unopened.Length==3?4:unopened.Contains(i)?2:1));
    }
    private static JObject WuhanDescribe(List<int> hand,List<string> melds,int win,JObject c) {
        JObject Row(string kind,string label,int points)=>new JObject{["kind"]=kind,["label"]=label,["points"]=points};
        if(Bool(c,"blocked")||melds.All(m=>m[0]=='G'))return Row("none","未满足",0);
        var special=WuhanSpecial(Int(c,"indicator"));bool sea=Bool(c,"sea");
        var best=WuhanShapes(hand,melds,c).Where(s=>(s.Names>0||s.Seed)&&(s.Names>0||sea||hand.Count(t=>t==special.joker)<2)).OrderByDescending(s=>s.Gold).ThenByDescending(s=>(s.Names+(sea?1:0)>0?s.Names+(sea?1:0)+2:0)+(s.Hard?1:0)).FirstOrDefault();
        if(best==null)return Row("none","未满足",0);int names=best.Names+(sea?1:0);var ron=new List<int>();
        if(!sea&&win!=special.joker&&!special.skins.Contains(win))foreach(int payer in Enumerable.Range(0,4).Where(i=>i!=Int(c,"index"))){int value=WuhanPayment(best,names,c,payer);if(value>0)ron.Add(value);}
        if(ron.Count>0)return Row("ron",ron.Min()==ron.Max()?ron.Min()+"点":$"{ron.Min()}～{ron.Max()}点",ron.Min());
        int self=WuhanPayment(best,names,c,null);return self>0?Row("tsumo_only","仅自摸",self):Row("below_minimum","未起和",0);
    }
    private sealed class NanjingScore {internal int Points,Pattern,Raw;internal bool Qualified;}
    private static NanjingScore NanjingEvaluate(List<int> hand,List<string> melds,int win,JObject c) {
        var h=Prepare("nanjing",hand,melds,c);if(h==null||!hand.Contains(win))return null;
        var flowers=Array(c,"flowers");if(flowers.Any(t=>!(t>=45&&t<=47||t>=51&&t<=58))||flowers.GroupBy(t=>t).Any(g=>g.Count()>(g.Key<50?4:1)))return null;
        var before=new List<int>(hand);before.Remove(win);bool unique=Waiting("nanjing",before,melds,c).Length==1;
        var all=hand.Concat(melds.SelectMany(LocalJokerHand.MeldTiles));int suits=all.Where(t=>t<40).Select(t=>t/10).Distinct().Count();bool honors=all.Any(t=>t>=41);
        bool pung=(bool?)c["pung_claimed"]??melds.Any(m=>m[0]=='k');NanjingScore best=null;
        foreach(var shape in LocalJokerHand.Shapes(h)) {
            int pattern=suits==1?(honors?20:30):0;bool seven=shape.Kind=="seven_pairs",qualified=suits==1;
            if(seven){pattern+=before.Count(t=>t==win)==3?90:40;qualified=true;}
            else if(hand.Count==2){pattern+=60;qualified=true;}
            else if(shape.Closed.Concat(melds).All(m=>m[0]!='S'&&m[0]!='s')){pattern+=20;qualified=true;}
            if(!pung&&!seven){pattern+=10;qualified=true;}if(flowers.Length==0)pattern+=20;
            bool narrow=!seven&&unique&&shape.Closed.Any(m=>m=="q"+win||m[0]=='S'&&(int.Parse(m.Substring(1))==win||int.Parse(m.Substring(1))%10==2&&win==int.Parse(m.Substring(1))+1||int.Parse(m.Substring(1))%10==8&&win==int.Parse(m.Substring(1))-1));
            bool last=narrow&&Array(c,"public_pungs").Contains(win);if(last)pattern+=20;
            qualified|=flowers.Length>=4;
            int winds=seven?0:shape.Closed.Concat(melds).Count(m=>"kKgG".Contains(m[0])&&int.Parse(m.Substring(1))>=41&&int.Parse(m.Substring(1))<=44);
            int raw=10+pattern+flowers.Length+(suits==2?1:0)+(narrow&&!last?1:0)+winds+melds.Sum(m=>m[0]=='G'?2:m[0]=='g'?1:0);
            var score=new NanjingScore{Points=Math.Min(120,raw),Pattern=pattern,Raw=raw,Qualified=qualified};
            if(best==null||(score.Qualified&&!best.Qualified)||score.Qualified==best.Qualified&&score.Raw>best.Raw)best=score;
        }
        return best;
    }
    private static JObject NanjingDescribe(List<int> hand,List<string> melds,int win,JObject c,int? cut) {
        int? water=(int?)c["passed_win_value"];
        if(cut.HasValue){var declined=new List<int>(hand);declined.Remove(win);declined.Add(cut.Value);var old=NanjingEvaluate(declined,melds,cut.Value,c);if(old?.Qualified==true)water=Math.Max(water??0,old.Pattern);}
        var q=NanjingEvaluate(hand,melds,win,c);
        return new JObject{["kind"]=q==null||Bool(c,"peida")?"unmet":!q.Qualified?"not_ready":water.HasValue&&q.Pattern<=water?"self_only":"ron",["points"]=q?.Qualified==true?q.Points:0,["pattern_points"]=q?.Pattern??0,["flower_count"]=Array(c,"flowers").Length};
    }
}
