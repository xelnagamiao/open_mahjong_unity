using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>Ordinary future-tile previews, independent of Unity state and server requests.</summary>
internal sealed partial class HongKongLocal {
    private static readonly int[] Tiles=LocalJokerHand.Tiles,Orphans=LocalJokerHand.Orphans;
    private static readonly int[][] Orders={new[]{1,2,3},new[]{1,3,2},new[]{2,1,3},new[]{2,3,1},new[]{3,1,2},new[]{3,2,1}};
    private readonly List<string> codes;
    private readonly JObject c;
    private readonly string profile,ready;
    private readonly bool old,sixteen,lian,remix,flowersEnabled,selfOnly;
    private readonly int seat,round,streak;
    private readonly int[] flowers;
    private readonly Dictionary<string,int[]> waitsCache=new Dictionary<string,int[]>();
    private sealed class M {
        internal int Tile;internal char Kind;internal bool External,Concealed;
        internal int[] Tiles=>Kind=='s'?new[]{Tile-1,Tile,Tile+1}:Enumerable.Repeat(Tile,Kind=='g'?4:3).ToArray();
    }
    private sealed class S {internal string Kind;internal int Pair,Win=-1;internal M[] Melds=System.Array.Empty<M>();}
    internal HongKongLocal(List<string> melds,JObject context){
        codes=melds;c=context;profile=(string)c["profile"]??"hongkong/qingzhang";old=profile=="hongkong/qingzhang";sixteen=profile=="hongkong/new16";remix=profile=="hongkong/qingzhang_lianhuise";lian=profile=="hongkong/new13_lianhuise"||profile=="hongkong/new13"&&(string)c["new13_version"]=="lianhuise";
        flowersEnabled=(bool?)c["flowers_enabled"]??(sixteen||lian||remix);selfOnly=(bool?)c["self_draw_only"]??false;flowers=c["flowers"]?.ToObject<int[]>()??System.Array.Empty<int>();seat=(int?)c["seat"]??0;round=(int?)c["round_wind"]??41;streak=(int?)c["dealer_streak"]??0;ready=(string)c["ready"]??"";
    }
    private static M Parse(string code,bool external)=>new M{Tile=int.Parse(code.Substring(1)),Kind=char.ToLowerInvariant(code[0])=='s'?'s':char.ToLowerInvariant(code[0])=='g'?'g':'k',External=external,Concealed=!external||code[0]=='G'};
    private static bool IsOrphans(IEnumerable<int> hand){var list=hand.ToArray();return list.Length==14&&Orphans.All(list.Contains)&&list.All(Orphans.Contains);}
    private IEnumerable<S> Shapes(List<int> hand,int win=0) {
        var policy=new LocalJokerHand.Policy{MeldCount=sixteen?5:4,SevenPairs=!old&&!sixteen&&!lian,OrphanShape=!sixteen};var h=LocalJokerHand.Prepare(hand,codes,policy);if(h==null||win!=0&&!hand.Contains(win))yield break;
        var external=codes.Select(m=>Parse(m,true)).ToArray();
        foreach(var shape in LocalJokerHand.Shapes(h)) {
            if(shape.Kind!="standard"){yield return new S{Kind=shape.Kind=="thirteen_orphans"?"orphans":shape.Kind};continue;}
            int pair=int.Parse(shape.Closed.First(m=>m[0]=='q').Substring(1));var ms=external.Concat(shape.Closed.Where(m=>m[0]!='q').Select(m=>Parse(m,false))).ToArray();
            if(win==0||pair==win)yield return new S{Kind="standard",Pair=pair,Melds=ms};
            if(win!=0)for(int i=external.Length;i<ms.Length;i++)if(ms[i].Tiles.Contains(win))yield return new S{Kind="standard",Pair=pair,Melds=ms,Win=i};
        }
        var count=hand.GroupBy(t=>t).ToDictionary(g=>g.Key,g=>g.Count());
        if(sixteen){
            if(external.Length==1&&external[0].Kind=='g'&&IsOrphans(hand))yield return new S{Kind="orphans",Melds=external};
            if(external.Length==0){
                foreach(var meld in Tiles.Where(t=>count.GetValueOrDefault(t)>=3).Select(t=>new M{Kind='k',Tile=t,Concealed=true}).Concat(Tiles.Where(t=>t<40&&t%10<=7&&new[]{t,t+1,t+2}.All(hand.Contains)).Select(t=>new M{Kind='s',Tile=t+1,Concealed=true}))){var rest=new List<int>(hand);foreach(int t in meld.Tiles)rest.Remove(t);if(IsOrphans(rest))yield return new S{Kind="orphans",Melds=new[]{meld}};}
                if(count.Values.Count(n=>n%2==1)==1&&count.Values.Contains(3))yield return new S{Kind="likugu"};
                if(count.Count==16&&Enumerable.Range(41,7).All(hand.Contains)&&Enumerable.Range(1,3).All(s=>{var ranks=count.Keys.Where(t=>t/10==s).Select(t=>t%10).OrderBy(t=>t).ToArray();return ranks.Length==3&&ranks[1]-ranks[0]>=3&&ranks[2]-ranks[1]>=3;}))yield return new S{Kind="sixteen_unconnected"};
            }
        }else if(remix&&external.Length==0&&count.Count==14&&Orders.Any(order=>{var knitted=Enumerable.Range(1,3).SelectMany(s=>new[]{s*10+order[s-1],s*10+order[s-1]+3,s*10+order[s-1]+6}).ToArray();return hand.All(t=>t>=41||knitted.Contains(t))&&(knitted.All(hand.Contains)||Enumerable.Range(41,7).All(hand.Contains));}))yield return new S{Kind="knitted"};
    }
    internal int[] Waiting(List<int> hand){string key=string.Join(",",hand.OrderBy(t=>t));if(waitsCache.TryGetValue(key,out var value))return value;return waitsCache[key]=Tiles.Where(t=>Shapes(hand.Concat(new[]{t}).ToList()).Any()).ToArray();}
    private static int[] Concealed(S shape,bool self)=>Enumerable.Range(0,shape.Melds.Length).Where(i=>shape.Melds[i].Kind!='s'&&shape.Melds[i].Concealed&&(self||i!=shape.Win)).ToArray();
    private bool Gates(List<int> hand,int win,bool self,bool strict=false){if(codes.Count>0||hand.Count!=14||hand.Any(t=>t>=40)||hand.Select(t=>t/10).Distinct().Count()!=1)return false;var rest=new List<int>(hand);if(strict&&!self)rest.Remove(win);return Enumerable.Range(1,9).All(r=>rest.Count(t=>t%10==r)>=(r==1||r==9?3:1));}
    private sealed class Value {internal int Fan,Raw;internal bool Allowed;internal string Kind;internal Dictionary<string,int> Fans;}
    private Value Score(List<int> hand,int win,bool self){
        if(flowers.Any(t=>t<51||t>58)||flowers.Distinct().Count()!=flowers.Length||!flowersEnabled&&flowers.Length>0||!old&&!sixteen&&!lian&&!remix&&flowers.Length>0)return new Value();
        var shapes=Shapes(hand,win).ToArray();if(shapes.Length==0)return new Value();var before=new List<int>(hand);before.Remove(win);var waits=Waiting(before);
        if(!self&&!old&&!sixteen&&!lian&&!remix){var pungs=shapes.Where(s=>s.Kind=="standard"&&s.Win>=0&&s.Melds[s.Win].Kind=='k').ToArray();if(pungs.Length>0)shapes=pungs;}
        var values=shapes.Select(s=>old||lian?Old(hand,s,win,self):sixteen?Sixteen(hand,s,win,self,waits):remix?Remix(hand,s,win,self,waits):Modern13(hand,s,win,self,waits)).ToList();
        if(sixteen)return SixteenBest(values);
        return values.OrderByDescending(v=>remix&&v.Allowed).ThenByDescending(v=>v.Fan).ThenByDescending(v=>v.Raw).First();
    }
    internal JObject Describe(List<int> hand,int win){var ron=Score(hand,win,false);var self=Score(hand,win,true);return new JObject{["tile"]=win,["ron"]=ron.Allowed&&!selfOnly,["self_draw"]=self.Allowed,["ron_fan"]=ron.Fan,["self_draw_fan"]=self.Fan};}
    private Value Old(List<int> hand,S s,int win,bool self){
        var all=hand.Concat(s.Melds.Where(m=>m.External).SelectMany(m=>m.Tiles)).ToArray();int suits=all.Where(t=>t<40).Select(t=>t/10).Distinct().Count();bool honors=all.Any(t=>t>=41),terminal=all.All(t=>t<40&&(t%10==1||t%10==9)),mixed=all.All(Orphans.Contains);
        var pungs=s.Melds.Where(m=>m.Kind!='s').Select(m=>m.Tile).ToArray();int dragons=pungs.Count(t=>t>=45),winds=pungs.Count(t=>t>=41&&t<=44),hidden=Concealed(s,self).Length;bool allPungs=s.Melds.All(m=>m.Kind!='s'),smallWinds=winds==3&&s.Pair>=41&&s.Pair<=44,smallDragons=dragons==2&&s.Pair>=45;
        int fan=0;
        if(lian){
            int limit=winds==4||s.Kind=="orphans"||s.Melds.Count(m=>m.Kind=='g')==4?13:suits==0||terminal||Gates(hand,win,self)?10:0;if(limit>0)return new Value{Fan=limit,Raw=limit,Allowed=true};
            bool pureHidden=codes.Count==0&&hidden==4,mixedPungs=allPungs&&mixed;
            fan+=(s.Melds.All(m=>m.Kind=='s')?1:0)+(!flowersEnabled&&codes.Count==0&&!pureHidden?1:0)+(allPungs&&!mixedPungs&&!pureHidden?3:0)+(mixedPungs?4:0)+(pureHidden?8:0)+(smallDragons?5:0)+(dragons==3?8:0)+(smallWinds?6:0)+(suits==1&&honors&&!smallWinds?3:0)+(suits==1&&!honors?7:0);
            if(!smallDragons&&dragons!=3)fan+=dragons;fan+=(pungs.Contains(seat+41)?1:0)+(pungs.Contains(round)?1:0)+(self&&!pureHidden?1:0);
            if(flowersEnabled)fan+=flowers.Length==0?1:new[]{51,55}.Sum(start=>Enumerable.Range(start,4).All(flowers.Contains)?2:flowers.Contains(start+seat)?1:0);
        }else{
            fan+=(suits==1?(honors?3:7):suits==0?10:0)+(terminal?10:0)+(mixed?1:0)+(!flowersEnabled&&codes.Count==0?1:0)+(s.Kind=="orphans"?10:0)+(Gates(hand,win,self,true)?10:0)+(self?1:0);
            if(flowersEnabled)fan+=flowers.Length==0?1:flowers.Count(t=>(t-51)%4==seat)+new[]{51,55}.Count(start=>Enumerable.Range(start,4).All(flowers.Contains));
            if(s.Kind=="standard")fan+=(s.Melds.All(m=>m.Kind=='s')?1:0)+(allPungs?3:0)+(smallDragons?3:0)+(dragons==3?5:0)+(smallWinds?10:0)+(winds==4?10:0)+(s.Melds.Count(m=>m.Kind=='g')==4?10:0)+(hidden==4&&s.Melds.All(m=>m.Kind!='g')?10:0)+dragons+(pungs.Contains(seat+41)?1:0)+(pungs.Contains(round)?1:0);
        }
        return new Value{Fan=Math.Min(lian?13:10,fan),Raw=fan,Allowed=fan>=3};
    }
    private Value Modern13(List<int> hand,S s,int win,bool self,int[] waits){
        var all=hand.Concat(codes.SelectMany(LocalJokerHand.MeldTiles)).ToArray();int suits=all.Where(t=>t<40).Select(t=>t/10).Distinct().Count();bool honors=all.Any(t=>t>=41),closed=codes.All(m=>m[0]=='G'),gates=Gates(hand,win,self);
        int fan=closed&&self?3:(closed?1:0)+(self?1:0);fan+=(s.Kind=="orphans"?40:0)+(s.Kind=="seven_pairs"?7:0)+(suits==1&&honors?3:0)+(gates?40:0)+((suits==1&&!honors||suits==0)&&!gates?10:0)+(suits==0?40:0)+(all.All(t=>new[]{32,33,34,36,38,46}.Contains(t))?40:0)+(suits==2&&!honors?1:0)+(!all.Any(t=>t<40&&t%10==5)?1:0)+(!honors&&all.All(t=>t%10>=2&&t%10<=8)?1:0)+(all.All(Orphans.Contains)?10:0)+(all.Select(Category).Distinct().Count()==5?5:0)+(waits.Length==1?1:0);
        fan+=ready=="heaven"?8:ready=="earth"?4:ready.Length>0&&closed?1:0;
        if(s.Kind=="standard"){
            var ms=s.Melds;var pungs=ms.Where(m=>m.Kind!='s').Select(m=>m.Tile).ToArray();var chows=ms.Where(m=>m.Kind=='s').ToArray();int dragons=pungs.Count(t=>t>=45),winds=pungs.Count(t=>t>=41&&t<=44),kongs=ms.Count(m=>m.Kind=='g'),hidden=Concealed(s,self).Length;bool allPungs=chows.Length==0,full258=allPungs&&all.All(t=>t<40&&new[]{2,5,8}.Contains(t%10));int ranks=all.Select(t=>t<40?t%10:t<45?10:11).Distinct().Count();
            fan+=(chows.Length==4?1:0)+(allPungs?3:0)+(s.Pair<40&&new[]{2,5,8}.Contains(s.Pair%10)?1:0)+(full258?20:0)+(allPungs&&ranks==2?20:0)+(allPungs&&ranks==3&&!full258?10:0)+(dragons==2&&s.Pair>=45?5:0)+(dragons==3?10:0)+(winds==3&&s.Pair>=41&&s.Pair<=44?20:0)+(winds==4?40:0)+dragons+(pungs.Contains(seat+41)?1:0)+(pungs.Contains(round)?1:0)+(kongs>=2?new[]{0,0,1,10,40}[kongs]:0)+(hidden>=3?(hidden==3?2:30):0);
            if(codes.Count==4&&codes.All(m=>"sk".Contains(m[0])))fan+=self?2:3;if(codes.Count==4&&codes.All(m=>m[0]=='k'))fan++;
            var groups=ms.Select(m=>m.Tiles).Concat(new[]{new[]{s.Pair,s.Pair}}).ToArray();if(Enumerable.Range(1,9).Any(r=>groups.All(g=>g.Any(t=>t<40&&t%10==r))))fan+=10;
            if(groups.All(g=>g.Any(Orphans.Contains)))fan+=honors?2:3;if(allPungs&&all.All(t=>t<40&&(t%10==1||t%10==9)))fan+=40;if(allPungs&&honors&&suits>0&&all.All(Orphans.Contains))fan+=2;
            foreach(int suit in new[]{1,2,3}){
                var dark=chows.Where(m=>!m.External&&m.Tile/10==suit).Select(m=>m.Tile%10).ToArray();if(new[]{2,5,8}.All(dark.Contains))fan+=3;
                var pr=pungs.Where(t=>t/10==suit).Select(t=>t%10).ToArray();int longest=new[]{3,4}.Where(n=>Enumerable.Range(1,10-n).Any(r=>Enumerable.Range(r,n).All(pr.Contains))).DefaultIfEmpty().Max();fan+=longest==4?40:longest==3?2:0;
                var cr=chows.Where(m=>m.Tile/10==suit).Select(m=>m.Tile%10).ToArray();if(new[]{2,8}.All(cr.Contains)||new[]{1,9}.All(pr.Contains))fan++;
            }
            int doubles=chows.Where(m=>!m.External).GroupBy(m=>m.Tile).Sum(g=>g.Count()/2);fan+=doubles>=2?10:doubles>0?1:0;
            for(int r=1;r<=9;r++){bool sameP=new[]{1,2,3}.All(suit=>pungs.Contains(suit*10+r)),sameC=new[]{1,2,3}.All(suit=>chows.Any(m=>m.Tile==suit*10+r));fan+=(sameP?2:0)+(sameP||sameC?3:0);}
            if(s.Win>=0&&ms[s.Win].Kind=='s'&&ms[s.Win].Tile==win&&win%10==5)fan++;
            foreach(int t in all.GroupBy(t=>t).Where(g=>g.Count()==4).Select(g=>g.Key)){var used=groups.Where(g=>g.Contains(t)).ToArray();if(used.Length==2&&used.Any(g=>g.Count(x=>x==t)==3))fan++;else if(used.Length==3&&s.Pair==t)fan+=2;}
        }
        return new Value{Fan=fan,Raw=fan,Allowed=true};
    }
    private static int Category(int t)=>t<40?t/10:t<45?4:5;
}
