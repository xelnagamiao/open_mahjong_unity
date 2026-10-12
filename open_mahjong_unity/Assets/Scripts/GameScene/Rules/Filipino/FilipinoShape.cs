using System;
using System.Collections.Generic;
using System.Linq;
// This mirror supplies shape hints only. The server owns legality and scoring.
public static class FilipinoShape {
    public static readonly int[] Tiles=Enumerable.Range(1,3).SelectMany(s=>Enumerable.Range(1,9).Select(n=>10*s+n)).ToArray();
    public static HashSet<int> Waiting(TingpaiQuery q) {
        bool seven=Option(q.DetailedConfig,"seven_pairs",true), quads=Option(q.DetailedConfig,"pair_quads_as_two",false);
        var result=new HashSet<int>();
        if(q.Hand==null) return result;
        foreach(int tile in Tiles) if(IsComplete(q.Hand.Concat(new[]{tile}).ToArray(),q.Melds,seven,quads)) result.Add(tile);
        return result;
    }
    private static bool Option(IDictionary<string,object> config,string key,bool fallback)=>config!=null && config.TryGetValue(key,out var value) && bool.TryParse(value?.ToString(),out bool b)?b:fallback;
    public static bool IsComplete(IList<int> hand,IList<string> melds,bool seven=true,bool quads=false) {
        melds=melds??Array.Empty<string>();
        if(hand==null || melds.Count>5 || hand.Count!=(5-melds.Count)*3+2) return false;
        int[] counts=new int[40],physical=new int[40];
        foreach(int t in hand) { if(!Tiles.Contains(t) || ++physical[t]>4) return false; counts[t]++; }
        foreach(string code in melds) {
            if(string.IsNullOrEmpty(code) || code.Length<3 || !"skgG".Contains(code[0]) || !int.TryParse(code.Substring(1),out int t) || !Tiles.Contains(t) || code[0]=='s' && (t%10<2 || t%10>8)) return false;
            foreach(int v in code[0]=='s'?new[]{t-1,t,t+1}:Enumerable.Repeat(t,code[0]=='k'?3:4).ToArray()) if(++physical[v]>4) return false;
        }
        foreach(int t in Tiles) if(counts[t]>=2) {
            counts[t]-=2; bool win=Groups(counts,5-melds.Count); counts[t]+=2; if(win) return true;
        }
        if(!seven || melds.Count>1) return false;
        bool Pairs()=>counts.Sum()==14 && counts.All(n=>n%2==0 && (quads || n<=2));
        if(melds.Count==1) return Pairs();
        foreach(int t in Tiles) {
            if(counts[t]>=3) { counts[t]-=3; bool ok=Pairs(); counts[t]+=3; if(ok) return true; }
            if(t%10<=7 && counts[t]>0 && counts[t+1]>0 && counts[t+2]>0) {
                counts[t]--; counts[t+1]--; counts[t+2]--; bool ok=Pairs(); counts[t]++; counts[t+1]++; counts[t+2]++; if(ok) return true;
            }
        }
        return false;
    }
    private static bool Groups(int[] counts,int left) {
        int t=Array.FindIndex(counts,n=>n>0); if(t<0) return left==0; if(left<=0) return false;
        if(counts[t]>=3) { counts[t]-=3; bool ok=Groups(counts,left-1); counts[t]+=3; if(ok) return true; }
        if(t%10<=7 && counts[t+1]>0 && counts[t+2]>0) { counts[t]--; counts[t+1]--; counts[t+2]--; bool ok=Groups(counts,left-1); counts[t]++; counts[t+1]++; counts[t+2]++; if(ok) return true; }
        return false;
    }
}
