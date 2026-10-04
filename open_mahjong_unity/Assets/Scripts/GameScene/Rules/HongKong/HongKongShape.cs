using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>仅做四/五面子及港式特殊牌形听牌。番数由服务端同一计分器提供。</summary>
public static class HongKongShape {
    public static readonly int[] Tiles = Enumerable.Range(1, 3).SelectMany(s => Enumerable.Range(1, 9).Select(n => 10*s+n))
        .Concat(Enumerable.Range(41, 7)).ToArray();
    private static readonly HashSet<int> Orphans = new HashSet<int>(new[] {11,19,21,29,31,39,41,42,43,44,45,46,47});

    public static HashSet<int> Waiting(TingpaiQuery query) => Waiting(query.Hand, query.Melds,
        HongKong_Create_RoomConfig.IsLianhuise(query.SubRule,query.DetailedConfig) ? HongKongGameState.Qingzhang : query.SubRule);

    public static HashSet<int> Waiting(IList<int> hand, IList<string> melds, string profile) {
        var result = new HashSet<int>();
        melds = melds ?? Array.Empty<string>();
        if (!ValidatePhysical(hand,melds,profile,false,out _,out var physical)) return result;
        foreach (int tile in Tiles) {
            if (physical[tile]>=4) continue;
            var complete = hand.Concat(new[] {tile}).ToArray();
            if (IsComplete(complete, melds, profile)) result.Add(tile);
        }
        return result;
    }

    public static bool IsComplete(IList<int> hand, IList<string> melds, string profile) {
        melds = melds ?? Array.Empty<string>();
        int groups = profile==HongKongGameState.New16 ? 5 : 4;
        if (!ValidatePhysical(hand,melds,profile,true,out var counts,out _)) return false;
        if (Standard((int[])counts.Clone(), groups-melds.Count)) return true;
        if (profile==HongKongGameState.Qingzhang)
            return melds.Count==0 && IsOrphans(counts);
        if (profile==HongKongGameState.New13 || profile==HongKongGameState.New13Gametower)
            return melds.Count==0 && (IsOrphans(counts) || counts.All(n=>n%2==0));
        if (profile==HongKongGameState.New13Lianhuise)
            return melds.Count==0 && IsOrphans(counts);
        if (profile==HongKongGameState.QingzhangRemix)
            return melds.Count==0 && (IsOrphans(counts) || counts.All(n=>n%2==0) || Knitted(counts));
        if (melds.Count==0) {
            // 八对加一张，可将一组三张解释成将眼加多出的一张。
            if (counts.Contains(3) && counts.Sum(n=>n/2)==8) return true;
            if (Separated(counts)) return true;
            foreach (int t in Tiles) {
                if (counts[t]>=3) {
                    counts[t]-=3; bool ok=IsOrphans(counts); counts[t]+=3;
                    if (ok) return true;
                }
                if (t<40 && t%10<=7 && counts[t]>0 && counts[t+1]>0 && counts[t+2]>0) {
                    counts[t]--; counts[t+1]--; counts[t+2]--;
                    bool ok=IsOrphans(counts);
                    counts[t]++; counts[t+1]++; counts[t+2]++;
                    if (ok) return true;
                }
            }
        }
        return melds.Count==1 && (melds[0].StartsWith("g") || melds[0].StartsWith("G")) && IsOrphans(counts);
    }

    private static bool ValidatePhysical(IList<int> hand,IList<string> melds,string profile,bool complete,
                                         out int[] concealed,out int[] physical) {
        concealed=new int[48]; physical=new int[48];
        int groups=profile==HongKongGameState.New16 ? 5 :
            profile==HongKongGameState.New13 || profile==HongKongGameState.Qingzhang ||
            profile==HongKongGameState.New13Gametower || profile==HongKongGameState.New13Lianhuise || profile==HongKongGameState.QingzhangRemix ? 4 : -1;
        if (groups<0 || hand==null || melds.Count>groups || hand.Count!=(groups-melds.Count)*3+(complete?2:1)) return false;
        foreach(int tile in hand) {
            if (!Tiles.Contains(tile) || ++concealed[tile]>4) return false;
            physical[tile]++;
        }
        foreach(string code in melds) {
            if (string.IsNullOrEmpty(code) || code.Length<3 || !"skgG".Contains(code[0]) ||
                !int.TryParse(code.Substring(1),out int tile) || !Tiles.Contains(tile)) return false;
            if (code[0]=='s' && (tile>=40 || tile%10<2 || tile%10>8)) return false;
            var tiles=code[0]=='s' ? new[]{tile-1,tile,tile+1} : Enumerable.Repeat(tile,code[0]=='k'?3:4);
            foreach(int value in tiles) if (++physical[value]>4) return false;
        }
        return true;
    }

    private static bool Standard(int[] counts,int groups) {
        foreach (int tile in Tiles) {
            if (counts[tile]<2) continue;
            counts[tile]-=2;
            bool ok=Groups(counts,groups);
            counts[tile]+=2;
            if (ok) return true;
        }
        return false;
    }

    private static bool Groups(int[] counts,int left) {
        int tile=Array.FindIndex(counts,n=>n>0);
        if (tile<0) return left==0;
        if (left<=0) return false;
        if (counts[tile]>=3) {
            counts[tile]-=3;
            bool ok=Groups(counts,left-1);
            counts[tile]+=3;
            if (ok) return true;
        }
        if (tile<40 && tile%10<=7 && counts[tile+1]>0 && counts[tile+2]>0) {
            counts[tile]--; counts[tile+1]--; counts[tile+2]--;
            bool ok=Groups(counts,left-1);
            counts[tile]++; counts[tile+1]++; counts[tile+2]++;
            if (ok) return true;
        }
        return false;
    }

    private static bool IsOrphans(int[] counts) => counts.Sum()==14 && Orphans.All(t=>counts[t]>=1)
        && Tiles.Where(t=>counts[t]>0).All(Orphans.Contains);

    private static bool Separated(int[] counts) {
        if (counts.Sum()!=17 || counts.Count(n=>n>0)!=16 || counts.Any(n=>n>2)) return false;
        for (int suit=1;suit<=3;suit++) {
            int previous=-9;
            for (int n=1;n<=9;n++) {
                if (counts[suit*10+n]==0) continue;
                if (n-previous<3) return false;
                previous=n;
            }
        }
        return true;
    }

    private static bool Knitted(int[] counts) {
        if (counts.Count(n=>n>0)!=14) return false;
        foreach(var order in new[]{new[]{1,2,3},new[]{1,3,2},new[]{2,1,3},new[]{2,3,1},new[]{3,1,2},new[]{3,2,1}}) {
            var knitted=new HashSet<int>(Enumerable.Range(1,3).SelectMany(s=>new[]{s*10+order[s-1],s*10+order[s-1]+3,s*10+order[s-1]+6}));
            if (Tiles.Where(t=>counts[t]>0).All(t=>t>=41 || knitted.Contains(t)) &&
                (knitted.All(t=>counts[t]>0) || Enumerable.Range(41,7).All(t=>counts[t]>0))) return true;
        }
        return false;
    }
}
