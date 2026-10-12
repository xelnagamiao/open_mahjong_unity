using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Scoped physical jokers; this mirror provides shape hints, never scores.</summary>
public static class VietnameseShape {
    public static readonly int[] Tiles=Enumerable.Range(1,3).SelectMany(s=>Enumerable.Range(1,9).Select(n=>10*s+n)).Concat(Enumerable.Range(41,7)).ToArray();
    public static readonly int[] Jokers={71,72,73,74,76,77,78};
    private static readonly int[] Orphans={11,19,21,29,31,39,41,42,43,44,45,46,47};
    public static bool Represents(int physical,int face) {
        if(physical==face || physical==71) return true;
        switch(physical) {
            case 72:return face>=21 && face<=29;
            case 73:return face>=31 && face<=39;
            case 74:return face>=11 && face<=19;
            case 76:return face>=41 && face<=44;
            case 77:return face>=45 && face<=47;
            case 78:return face<40;
            default:return false;
        }
    }
    public static HashSet<int> Waiting(TingpaiQuery q) {
        var result=new HashSet<int>(); if(q.Hand==null) return result;
        bool pairs=q.DetailedConfig!=null && q.DetailedConfig.TryGetValue("seven_pairs",out var v) && bool.TryParse(v?.ToString(),out bool b) && b;
        foreach(int t in Tiles.Concat(Jokers)) if(IsComplete(q.Hand.Concat(new[]{t}).ToArray(),q.Melds?.Count??0,pairs)) result.Add(t);
        return result;
    }
    public static bool IsComplete(IList<int> hand,int meldCount=0,bool pairs=false) {
        if(hand==null || meldCount<0 || meldCount>4 || hand.Count!=(4-meldCount)*3+2) return false;
        int[] c=new int[79];
        foreach(int t in hand) if(!Tiles.Contains(t) && !Jokers.Contains(t) || ++c[t]>(t<50?4:1)) return false;
        var dead=new HashSet<string>();
        foreach(int t in Tiles) if(Use(c,new[]{t,t},0,()=>Groups(c,4-meldCount,dead))) return true;
        if(meldCount!=0) return false;
        foreach(int t in Orphans) if(Use(c,Orphans.Concat(new[]{t}).ToArray(),0,()=>true)) return true;
        return pairs && Pairs(c);
    }
    private static bool Use(int[] c,int[] faces,int at,Func<bool> done) {
        if(at==faces.Length) return done();
        int face=faces[at];
        if(c[face]>0) {
            c[face]--; bool win=Use(c,faces,at+1,done); c[face]++; if(win)return true;
        }
        foreach(int j in Jokers) if(c[j]>0 && Represents(j,face)) {
            c[j]--; bool win=Use(c,faces,at+1,done); c[j]++; if(win)return true;
        }
        return false;
    }
    private static bool Groups(int[] c,int left,HashSet<string> dead) {
        if(left==0) return c.All(n=>n==0);
        string key=left+":"+string.Join(",",c);if(dead.Contains(key))return false;
        int anchor=Tiles.FirstOrDefault(t=>c[t]>0),before=c[anchor];
        foreach(int face in Tiles) {
            if((anchor==0 || anchor==face) && Use(c,new[]{face,face,face},0,()=> (anchor==0 || c[anchor]<before) && Groups(c,left-1,dead)))return true;
            if(face>=40 || face%10>7 || anchor!=0 && (anchor<face || anchor>face+2))continue;
            if(Use(c,new[]{face,face+1,face+2},0,()=> (anchor==0 || c[anchor]<before) && Groups(c,left-1,dead)))return true;
        }
        dead.Add(key);return false;
    }
    private static bool Pairs(int[] c) {
        if(c.All(n=>n==0))return true;
        int anchor=Tiles.FirstOrDefault(t=>c[t]>0),before=c[anchor];
        foreach(int t in anchor==0?Tiles:new[]{anchor})
            if(Use(c,new[]{t,t},0,()=> (anchor==0 || c[anchor]<before) && Pairs(c)))return true;
        return false;
    }
}
