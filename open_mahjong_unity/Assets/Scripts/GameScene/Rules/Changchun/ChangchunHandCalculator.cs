using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Client hints only; the server remains authoritative. No ordinary wildcard.</summary>
internal static class ChangchunHandCalculator {
    private static readonly int[] Tiles=Enumerable.Range(1,3).SelectMany(s=>Enumerable.Range(1,9).Select(n=>s*10+n)).Concat(Enumerable.Range(41,7)).ToArray();
    private sealed class Shape {public int Pair;public List<int> Sequences=new List<int>();public int Pungs;public bool Seven;}
    public static int[] MeldTiles(string code,bool logical=false) {
        if(string.IsNullOrEmpty(code)) return null;
        if(code[0]=='C') {
            string[] bits=code.Split(':');
            if(bits.Length!=3) return null;
            var domains=new Dictionary<string,int[]> { {"Cyao",new[]{11,21,31}}, {"Cjiu",new[]{19,29,39}}, {"Cwind",new[]{41,42,43,44}}, {"Cdragon",new[]{45,46,47}} };
            if(!domains.TryGetValue(bits[0],out var domain)) return null;
            try {
                var physical=bits[1].Split(',').Select(int.Parse).ToArray();var declared=bits[2].Split(',').Select(int.Parse).ToArray();
                if(physical.Length<3 || physical.Length!=declared.Length || declared.Take(3).Distinct().Count()!=3
                    || declared.Any(t=>!domain.Contains(t)) || physical.Any(t=>!Tiles.Contains(t))
                    || physical.Where((t,i)=>t!=31 && t!=declared[i]).Any() || physical.GroupBy(t=>t).Any(g=>g.Count()>4)) return null;
                return logical?declared:physical;
            } catch(FormatException) {return null;} catch(OverflowException) {return null;}
        }
        if(code.Length!=3 || !"skgG".Contains(code[0]) || !int.TryParse(code.Substring(1),out int normalTile) || !Tiles.Contains(normalTile)) return null;
        if(code[0]=='s' && (normalTile>=40 || normalTile%10<2 || normalTile%10>8)) return null;
        return code[0]=='s' ? new[]{normalTile-1,normalTile,normalTile+1} : Enumerable.Repeat(normalTile,code[0]=='k'?3:4).ToArray();
    }
    private static List<Shape> Shapes(List<int> hand,List<string> codes,bool requireConditions=true) {
        var result=new List<Shape>();
        if(hand==null || codes.Count>4 || codes.Where(c=>c!=null && c.StartsWith("C")).GroupBy(c=>c.Split(':')[0]).Any(g=>g.Count()>1) || hand.Count+codes.Count*3!=14 || hand.Any(t=>!Tiles.Contains(t))) return result;
        var physical=new List<int>(hand);var logical=new List<int>(hand);
        foreach(string c in codes) {
            var p=MeldTiles(c);var l=MeldTiles(c,true);
            if(p==null || l==null) return result;
            physical.AddRange(p);logical.AddRange(l);
        }
        if(physical.GroupBy(t=>t).Any(g=>g.Count()>4)) return result;
        if(requireConditions && (logical.Where(t=>t<40).Select(t=>t/10).Distinct().Count()!=3
            || !logical.Any(t=>t>=41 || t%10==1 || t%10==9))) return result;
        if(codes.Count==0 && hand.GroupBy(t=>t).All(g=>g.Count()%2==0)) result.Add(new Shape{Seven=true});
        bool externalPung = codes.Any(c=>c[0]!='s');
        var decomps = StandardMahjongSolver.StandardDecompositions(hand, allowHonors: true);
        for (int i = 0; i < decomps.Count; i++) {
            var d = decomps[i];
            int pungs = 0;
            var seqs = new List<int>();
            for (int j = 0; j < d.Melds.Length; j++) {
                var u = d.Melds[j];
                if (u.Kind == 0) pungs++;
                else seqs.Add(u.TileId);
            }
            if (!requireConditions || externalPung || pungs > 0 || d.PairTile >= 45)
                result.Add(new Shape { Pair = d.PairTile, Pungs = pungs, Sequences = seqs });
        }
        return result;
    }
    private static readonly Dictionary<string,HashSet<int>> Cache=new Dictionary<string,HashSet<int>>();
    /// <summary>Show structural waits even when three suits, yaojiu or the pung requirement is unmet.</summary>
    public static HashSet<int> BasicWaits(List<int> hand,List<string> codes) => Waits(hand,codes,false);
    public static bool IsQualified(List<int> hand,List<string> codes) => Shapes(hand,codes??new List<string>()).Count>0;
    public static HashSet<int> Waits(List<int> hand,List<string> codes,bool requireConditions=true) {
        codes=codes??new List<string>();
        if(hand==null || codes.Count>4 || hand.Count+codes.Count*3!=13) return new HashSet<int>();
        string key=(requireConditions?"Q|":"B|")+string.Join(",",hand.OrderBy(t=>t))+"|"+string.Join("|",codes);
        if(Cache.TryGetValue(key,out var cached)) return new HashSet<int>(cached);
        var result=new HashSet<int>();
        if(!codes.Where(c=>c!=null && c.StartsWith("C")).GroupBy(c=>c.Split(':')[0]).Any(g=>g.Count()>1)) {
            int[] phys = new int[48];
            bool valid = true;
            for(int i=0;i<hand.Count;i++){ int t=hand[i]; if((uint)t>=48 || !Tiles.Contains(t) || ++phys[t]>4){ valid=false; break; } }
            if(valid) {
                foreach(string c in codes) {
                    var p=MeldTiles(c); var l=MeldTiles(c,true);
                    if(p==null || l==null){ valid=false; break; }
                    foreach(int t in p) if(++phys[t]>4){ valid=false; break; }
                    if(!valid) break;
                }
            }
            if(valid) {
                var candidates = StandardMahjongSolver.StandardWaitingTiles(hand, allowHonors: true, allowFifthCopy: false);
                candidates.RemoveWhere(t => (uint)t >= 48 || phys[t] >= 4);
                if(codes.Count==0) {
                    int[] hc = new int[48];
                    for(int i=0;i<hand.Count;i++) hc[hand[i]]++;
                    int oddCount = 0, oddTile = -1;
                    for(int i=0;i<Tiles.Length;i++){ int t=Tiles[i]; if((hc[t]&1)!=0){ oddCount++; oddTile=t; } }
                    if(oddCount==1 && phys[oddTile]<4) candidates.Add(oddTile);
                }
                if(!requireConditions) {
                    foreach(int tile in candidates) result.Add(tile);
                } else {
                    foreach(int tile in candidates) {
                        var candidate = new List<int>(hand.Count + 1);
                        candidate.AddRange(hand);
                        candidate.Add(tile);
                        if(Shapes(candidate,codes,true).Count>0) result.Add(tile);
                    }
                }
            }
        }
        if(Cache.Count>=2048) Cache.Clear();
        Cache[key]=result;return new HashSet<int>(result);
    }
    public static int Score(List<int> hand,List<string> codes,int win) {
        codes=codes??new List<string>();
        if(hand==null || !hand.Contains(win)) return 0;
        var before=new List<int>(hand);before.Remove(win);
        bool single=Waits(before,codes).Count==1;
        int best=0;
        foreach(var shape in Shapes(hand,codes)) {
            int fan=0;
            if(shape.Seven) fan=before.Count(t=>t==win)==3?4:3;
            else {
                // Public special kongs preserve menzen, including later additions.
                if(codes.All(c=>c[0]=='G' || c[0]=='C')) fan++;
                if(shape.Sequences.Count==0 && codes.All(c=>c[0]!='s')) fan+=2;
                else if(single || shape.Pair==win || shape.Sequences.Any(m=>m==win || m%10==2 && win==m+1 || m%10==8 && win==m-1)) fan++;
            }
            best=Math.Max(best,fan);
        }
        return best;
    }
}
