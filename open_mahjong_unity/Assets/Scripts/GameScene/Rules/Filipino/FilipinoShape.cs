using System;
using System.Collections.Generic;
using System.Linq;
// This mirror supplies shape hints only. The server owns legality and scoring.
public static class FilipinoShape {
    public static readonly int[] Tiles=Enumerable.Range(1,3).SelectMany(s=>Enumerable.Range(1,9).Select(n=>10*s+n)).ToArray();
    public static HashSet<int> Waiting(TingpaiQuery q) {
        bool seven=Option(q.DetailedConfig,"seven_pairs",true), quads=Option(q.DetailedConfig,"pair_quads_as_two",false);
        var result=new HashSet<int>();
        if(q?.Hand==null) return result;
        var melds=q.Melds??(IList<string>)Array.Empty<string>();
        if(melds.Count>5 || q.Hand.Count!=(5-melds.Count)*3+1) return result;
        int[] counts=new int[48], physical=new int[48];
        for(int i=0;i<q.Hand.Count;i++){ int t=q.Hand[i]; if(t<11||t>39||t%10==0||++physical[t]>4) return result; counts[t]++; }
        for(int i=0;i<melds.Count;i++) {
            string code=melds[i];
            if(string.IsNullOrEmpty(code) || code.Length<3 || !"skgG".Contains(code[0]) || !int.TryParse(code.Substring(1),out int t) || t<11||t>39||t%10==0 || code[0]=='s' && (t%10<2 || t%10>8)) return result;
            if(code[0]=='s'){ if(++physical[t-1]>4 || ++physical[t]>4 || ++physical[t+1]>4) return result; }
            else { physical[t]+=code[0]=='k'?3:4; if(physical[t]>4) return result; }
        }
        var std=StandardMahjongSolver.StandardWaitingTiles(q.Hand, allowHonors: false, allowFifthCopy: false);
        foreach(int t in std) if(t<40 && physical[t]<4) result.Add(t);
        if(seven && melds.Count<=1) {
            foreach(int tile in Tiles) {
                if(physical[tile]>=4 || result.Contains(tile)) continue;
                counts[tile]++;
                if(IsSevenWithMeld(counts, melds.Count, quads)) result.Add(tile);
                counts[tile]--;
            }
        }
        return result;
    }
    private static bool Option(IDictionary<string,object> config,string key,bool fallback)=>config!=null && config.TryGetValue(key,out var value) && bool.TryParse(value?.ToString(),out bool b)?b:fallback;
    public static bool IsComplete(IList<int> hand,IList<string> melds,bool seven=true,bool quads=false) {
        melds=melds??Array.Empty<string>();
        if(hand==null || melds.Count>5 || hand.Count!=(5-melds.Count)*3+2) return false;
        int[] counts=new int[40],physical=new int[40];
        foreach(int t in hand) { if(t<11||t>39||t%10==0 || ++physical[t]>4) return false; counts[t]++; }
        foreach(string code in melds) {
            if(string.IsNullOrEmpty(code) || code.Length<3 || !"skgG".Contains(code[0]) || !int.TryParse(code.Substring(1),out int t) || t<11||t>39||t%10==0 || code[0]=='s' && (t%10<2 || t%10>8)) return false;
            if(code[0]=='s'){ if(++physical[t-1]>4 || ++physical[t]>4 || ++physical[t+1]>4) return false; }
            else { physical[t]+=code[0]=='k'?3:4; if(physical[t]>4) return false; }
        }
        var roHand = hand as IReadOnlyList<int> ?? new List<int>(hand);
        if(StandardMahjongSolver.CanStandardWin(roHand, allowHonors: false)) return true;
        return seven && melds.Count<=1 && IsSevenWithMeld(counts, melds.Count, quads);
    }
    private static bool IsSevenWithMeld(int[] counts, int meldCount, bool quads) {
        bool Pairs() {
            int sum = 0;
            for(int i=0;i<Tiles.Length;i++){ int n=counts[Tiles[i]]; if((n&1)!=0 || (!quads && n>2)) return false; sum+=n; }
            return sum==14;
        }
        if(meldCount==1) return Pairs();
        for(int i=0;i<Tiles.Length;i++) {
            int t=Tiles[i];
            if(counts[t]>=3) { counts[t]-=3; bool ok=Pairs(); counts[t]+=3; if(ok) return true; }
            if(t%10<=7 && counts[t]>0 && counts[t+1]>0 && counts[t+2]>0) {
                counts[t]--; counts[t+1]--; counts[t+2]--; bool ok=Pairs(); counts[t]++; counts[t+1]++; counts[t+2]++; if(ok) return true;
            }
        }
        return false;
    }
}
