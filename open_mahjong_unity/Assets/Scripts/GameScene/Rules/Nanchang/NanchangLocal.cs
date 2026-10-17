using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

// A request-scoped solver: honor chows and fixed discarded jing need their own policy.
internal sealed class NanchangLocal {
    private static readonly int[] Tiles=LocalJokerHand.Tiles;
    private static readonly int[][] Sequences=Tiles.Where(t=>t<40&&t%10<=7).Select(t=>new[]{t,t+1,t+2}).Concat(new[]{new[]{41,42,43},new[]{41,42,44},new[]{41,43,44},new[]{42,43,44},new[]{45,46,47}}).ToArray();
    private readonly JObject context;
    private readonly List<string> melds;
    private readonly int[] jing;
    private readonly Dictionary<string,bool> groupCache=new Dictionary<string,bool>();
    private readonly Dictionary<string,string[]> shapeCache=new Dictionary<string,string[]>();
    internal NanchangLocal(List<string> melds,JObject context){this.melds=melds;this.context=context;int primary=(int?)context["indicator"]??11;var cycle=primary<40?Enumerable.Range(primary/10*10+1,9).ToArray():primary<45?new[]{41,42,43,44}:new[]{45,47,46};jing=new[]{primary,cycle[(System.Array.IndexOf(cycle,primary)+1)%cycle.Length]};}
    private static int[] Meld(string code){if(code.StartsWith("s["))return code.Substring(2,code.Length-3).Split(',').Select(int.Parse).ToArray();if(code[0]=='s'&&code.Length==7)return new[]{int.Parse(code.Substring(1,2)),int.Parse(code.Substring(3,2)),int.Parse(code.Substring(5,2))};return LocalJokerHand.MeldTiles(code);}
    private bool Valid(List<int> hand,bool complete=true){
        if(melds.Count>4||hand.Count+3*melds.Count!=(complete?14:13))return false;
        var all=new List<int>(hand);foreach(string code in melds){var group=Meld(code);if(group==null||group.Length<3||group.Length>4||group.Distinct().Count()!=1&&!Sequences.Any(s=>s.SequenceEqual(group.OrderBy(t=>t))))return false;all.AddRange(group);}
        return all.All(Tiles.Contains)&&all.GroupBy(t=>t).All(g=>g.Count()<=4);
    }
    private (int[] counts,int wild) Counts(IEnumerable<int> hand,bool natural=false,int? fixedTile=null){var rest=hand.ToList();if(fixedTile.HasValue)rest.Remove(fixedTile.Value);int wild=natural?0:rest.Count(jing.Contains);var counts=Tiles.Select(t=>rest.Count(x=>x==t&&(natural||!jing.Contains(x)))+(fixedTile==t?1:0)).ToArray();return(counts,wild);}
    private bool Groups(int[] counts,int wild,int groups,int pair,bool triplets=false){
        if(wild<0||groups<0||counts.Sum()+wild!=groups*3+pair*2)return false;
        int index=System.Array.FindIndex(counts,n=>n>0);if(index<0)return true;
        string key=string.Join(",",counts)+$"/{wild}/{groups}/{pair}/{triplets}";if(groupCache.TryGetValue(key,out bool cached))return cached;
        bool found=false;
        if(pair>0){int used=Math.Min(2,counts[index]);var rest=(int[])counts.Clone();rest[index]-=used;found=Groups(rest,wild-2+used,groups,0,triplets);}
        if(!found&&groups>0){int used=Math.Min(3,counts[index]);var rest=(int[])counts.Clone();rest[index]-=used;found=Groups(rest,wild-3+used,groups-1,pair,triplets);}
        if(!found&&groups>0&&!triplets)foreach(var sequence in Sequences.Where(s=>s.Contains(Tiles[index]))){var rest=(int[])counts.Clone();int need=0;foreach(int t in sequence){int i=LocalJokerHand.Index[t];if(rest[i]>0)rest[i]--;else need++;}if(Groups(rest,wild-need,groups-1,pair)){found=true;break;}}
        groupCache[key]=found;return found;
    }
    private static bool Pairs(int[] counts,int wild,int number=7)=>counts.Sum()+wild==number*2&&counts.Sum(n=>n%2)<=wild;
    private static bool Lan(int[] counts,int wild){
        if(counts.Any(n=>n>1)||counts.Sum()+wild!=14)return false;int capacity=7;
        for(int suit=1;suit<=3;suit++){
            int present=0;for(int r=1;r<=9;r++)if(counts[LocalJokerHand.Index[suit*10+r]]>0)present|=1<<(r-1);
            int best=-1;for(int mask=0;mask<512;mask++){if((mask&present)!=present)continue;int last=-3,n=0;bool okay=true;for(int r=0;r<9;r++)if((mask&(1<<r))!=0){if(r-last<3){okay=false;break;}last=r;n++;}if(okay)best=Math.Max(best,n);}
            if(best<0)return false;capacity+=best;
        }
        return capacity>=14;
    }
    private string[] Shapes(List<int> hand,int win=0,bool self=true,bool natural=false){
        if(!Valid(hand)||!self&&!hand.Contains(win))return System.Array.Empty<string>();
        string key=string.Join(",",hand.OrderBy(t=>t))+$"/{win}/{self}/{natural}";if(shapeCache.TryGetValue(key,out var saved))return saved;
        var h=Counts(hand,natural,self?(int?)null:win);var result=new List<string>();
        if(Groups(h.counts,h.wild,4-melds.Count,1))result.Add("standard");
        if(melds.All(m=>Meld(m).Distinct().Count()==1)&&Groups(h.counts,h.wild,4-melds.Count,1,true))result.Add("all_triplets");
        if(melds.Count==0){if(Pairs(h.counts,h.wild))result.Add("seven_pairs");if(Lan(h.counts,h.wild))result.Add("thirteen_lan");}
        return shapeCache[key]=result.ToArray();
    }
    private bool Jingdiao(List<int> hand){if(!Valid(hand,false))return false;foreach(int tile in jing.Where(hand.Contains)){var rest=new List<int>(hand);rest.Remove(tile);var h=Counts(rest);if(Groups(h.counts,h.wild,4-melds.Count,0)||melds.Count==0&&Pairs(h.counts,h.wild,6))return true;}return false;}
    internal int[] Waiting(List<int> hand)=>Valid(hand,false)?Tiles.Where(t=>Shapes(hand.Concat(new[]{t}).ToList()).Length>0).ToArray():System.Array.Empty<int>();
    private sealed class Quote{internal int Fan,Points,German;internal double Effective;internal string Shape;}
    private Quote Score(List<int> hand,int win,bool self,int? payer,int[] discarded,bool other=true,bool declared=true){
        var shapes=Shapes(hand,win,self);if(shapes.Length==0)return null;var natural=Shapes(hand,win,self,true);var before=new List<int>(hand);before.Remove(win);
        bool jd=declared&&(bool?)context["declared_jingdiao"]==true&&self&&Jingdiao(before);int winner=(int?)context["index"]??1;
        int common=discarded.Sum(t=>t==jing[0]?2:t==jing[1]?1:0)+(jd?1:0);Quote best=null;
        foreach(string shape in shapes.OrderBy(s=>s,StringComparer.Ordinal)){
            bool isNatural=natural.Contains(shape);int german=isNatural?(other?5:10):0;
            int fan=common+(isNatural?(other?1:2):0)+(shape=="all_triplets"?2:shape=="seven_pairs"?(isNatural?2:1):shape=="thirteen_lan"?(Enumerable.Range(41,7).All(hand.Contains)?2:1):0);
            int points=Enumerable.Range(0,4).Where(i=>i!=winner).Sum(i=>(1<<Math.Min(6,fan+(self?1:0)+(winner==0||i==0?1:0)+(i==payer?1:0)))+(self||i==payer?german+(jd?5:0):0));
            var q=new Quote{Fan=fan,Points=points,German=german,Effective=Math.Min(6,fan+(winner==0?1:0)+(self||payer==0 ? 0.5 : 0)),Shape=shape};
            if(best==null||q.Points>best.Points||q.Points==best.Points&&(q.Fan>best.Fan||q.Fan==best.Fan&&string.CompareOrdinal(q.Shape,best.Shape)>0))best=q;
        }
        return best;
    }
    internal JObject Describe(List<int> hand,int win,int? cut=null){
        int winner=(int?)context["index"]??1;var discarded=context["discarded"]?.ToObject<List<int>>()??new List<int>();if(cut.HasValue)discarded.Add(cut.Value);
        var before=new List<int>(hand);before.Remove(win);bool jd=Jingdiao(before);int floor=6;
        if(jd)foreach(int t in Tiles){var q=Score(before.Concat(new[]{t}).ToList(),t,false,(winner+1)%4,discarded.ToArray(),declared:false);if(q!=null)floor=Math.Min(floor,Math.Min(6,q.Fan+(winner==0?1:0)));}
        string Status(Quote q,bool self){if(q==null||(bool?)context["peida"]==true)return "unmet";if(q.Fan==0)return "below_minimum";if(!self&&((bool?)context["declared_jingdiao"]==true||q.Effective<=((double?)context["missed_fan"]??-1)||jd&&Math.Min(6,q.Fan+(winner==0?1:0))<=floor))return "self_draw_only";return "allowed";}
        var statuses=new JObject();var points=new JObject();var upper=new List<int>();
        foreach(int payer in Enumerable.Range(0,4).Where(i=>i!=winner)){var q=Score(hand,win,false,payer,discarded.ToArray());string status=Status(q,false);statuses[payer.ToString()]=status;int value=status=="allowed"?q.Points:0;points[payer.ToString()]=value;upper.Add(status=="allowed"&&q.German>0?Score(hand,win,false,payer,discarded.ToArray(),false).Points:value);}
        var own=Score(hand,win,true,null,discarded.ToArray());string selfStatus=Status(own,true);var values=points.Properties().Select(p=>(int)p.Value).ToArray();
        return new JObject{["tile"]=win,["melds"]=new JArray(melds),["status"]=statuses.Properties().Any(p=>(string)p.Value=="allowed")?"allowed":selfStatus=="below_minimum"?"below_minimum":selfStatus=="allowed"?"self_draw_only":"unmet",["self_draw"]=selfStatus=="allowed",["self_draw_points"]=selfStatus=="allowed"?own.Points:0,["ron_status"]=statuses,["ron_points"]=points,["points"]=values.Max(),["points_min"]=values.Where(v=>v>0).DefaultIfEmpty().Min(),["points_max"]=upper.Max()};
    }
}
