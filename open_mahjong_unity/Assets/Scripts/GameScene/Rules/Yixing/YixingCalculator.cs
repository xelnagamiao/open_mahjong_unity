using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>客户端花数提示。独立枚举牌形与荣和牌归属；裁定仍以服务端为准。</summary>
public static class YixingCalculator {
    private sealed class Group {
        public int Tile, Size;
        public bool Sequence, Closed, Declared;
        public IEnumerable<int> Tiles => Sequence ? new[] {Tile, Tile+1, Tile+2} : Enumerable.Repeat(Tile, Size);
    }
    private sealed class Shape {
        public int Pair;
        public bool Seven;
        public List<Group> Groups;
    }
    public static bool SevenPairs(IDictionary<string, object> config) =>
        config != null && config.TryGetValue("seven_pairs", out var enabled) && enabled is bool value && value;
    private static bool Valid(int tile) => (tile >= 11 && tile <= 39 && tile % 10 >= 1 && tile % 10 <= 9) || (tile >= 41 && tile <= 47);
    private static readonly int[] Tiles = Enumerable.Range(11,37).Where(Valid).ToArray();
    private static bool Read(IList<int> hand, IList<string> codes, bool complete, out int[] counts, out int[] physical, out List<Group> groups) {
        counts = new int[48]; physical = new int[48]; groups = new List<Group>();
        if (hand == null || codes == null || codes.Count > 4 || hand.Count + 3*codes.Count != (complete ? 14 : 13)) return false;
        foreach (int tile in hand) {
            if (!Valid(tile) || ++physical[tile] > 4) return false;
            counts[tile]++;
        }
        foreach (string code in codes) {
            if (code == null || code.Length != 3 || "skgG".IndexOf(code[0]) < 0 || !int.TryParse(code.Substring(1), out int tile) || !Valid(tile)) return false;
            bool sequence = code[0] == 's';
            if (sequence && (tile >= 40 || tile%10 < 2 || tile%10 > 8)) return false;
            var group = new Group { Tile = sequence ? tile-1 : tile, Sequence = sequence,
                Size = code[0] == 'G' || code[0] == 'g' ? 4 : 3, Closed = code[0] == 'G', Declared = true };
            foreach (int t in group.Tiles) if (++physical[t] > 4) return false;
            groups.Add(group);
        }
        return true;
    }
    private static void Decompose(int[] counts, int needed, List<Group> groups, int pair, List<Shape> result) {
        int tile = Array.FindIndex(counts, n => n > 0);
        if (tile < 0) {
            if (needed == 0) result.Add(new Shape {Pair=pair, Groups=new List<Group>(groups)});
            return;
        }
        if (needed <= 0) return;
        if (counts[tile] >= 3) {
            counts[tile] -= 3; groups.Add(new Group { Tile=tile, Size=3, Closed=true });
            Decompose(counts,needed-1,groups,pair,result);
            groups.RemoveAt(groups.Count-1); counts[tile] += 3;
        }
        if (tile < 40 && tile%10 <= 7 && counts[tile+1] > 0 && counts[tile+2] > 0) {
            counts[tile]--; counts[tile+1]--; counts[tile+2]--;
            groups.Add(new Group {Tile=tile, Size=3, Sequence=true, Closed=true});
            Decompose(counts,needed-1,groups,pair,result);
            groups.RemoveAt(groups.Count-1); counts[tile]++; counts[tile+1]++; counts[tile+2]++;
        }
    }
    private static List<Shape> Shapes(int[] counts, List<Group> melds, bool sevenPairs) {
        var result = new List<Shape>();
        if (sevenPairs && melds.Count == 0 && counts.All(n => n%2 == 0))
            result.Add(new Shape {Seven=true, Groups=new List<Group>()});
        foreach (int tile in Tiles) {
            if (counts[tile] < 2) continue;
            counts[tile] -= 2;
            Decompose(counts,4-melds.Count,new List<Group>(melds),tile,result);
            counts[tile] += 2;
        }
        return result;
    }
    public static HashSet<int> Waiting(IList<int> hand, IList<string> codes, bool sevenPairs=false) {
        var result = new HashSet<int>();
        codes = codes ?? Array.Empty<string>();
        if (!Read(hand,codes,false,out var counts,out var physical,out var melds)) return result;
        foreach (int tile in Tiles) {
            if (physical[tile] >= 4) continue;
            counts[tile]++;
            if (Shapes(counts,melds,sevenPairs).Count > 0) result.Add(tile);
            counts[tile]--;
        }
        return result;
    }
    private static YixingFlowerItem Item(string id, string name, int value) => new YixingFlowerItem {id=id,name=name,flowers=value};
    public static YixingScoreInfo Score(IList<int> hand, IList<string> codes, IList<int> flowers,
        int winningTile, bool selfDraw=false, int seatWind=41, bool afterKong=false,
        bool seaBottom=false, bool robKong=false, bool sevenPairs=false) {
        codes = codes ?? Array.Empty<string>(); flowers = flowers ?? Array.Empty<int>();
        if (seatWind < 41 || seatWind > 44 || (selfDraw && robKong) || flowers.Any(t => t < 51 || t > 58)
            || flowers.Distinct().Count() != flowers.Count || !Read(hand,codes,true,out var counts,out var physical,out var melds)
            || !hand.Contains(winningTile)) return null;
        YixingScoreInfo best = null;
        foreach (var shape in Shapes(counts,melds,sevenPairs)) {
            var patterns = new List<YixingFlowerItem>();
            int suits = Tiles.Where(t => t < 40 && physical[t] > 0).Select(t => t/10).Distinct().Count();
            if (suits == 0) patterns.Add(Item("全风板","全风板",13));
            else if (suits == 1) {
                bool honors = Tiles.Any(t => t >= 41 && physical[t] > 0);
                patterns.Add(Item(honors ? "混一色" : "清一色",honors ? "混一色" : "清一色",honors ? 6 : 8));
            }
            if (shape.Seven) patterns.Add(Item("七小对","七小对",10));
            else if (shape.Groups.All(g => !g.Sequence)) patterns.Add(Item("碰碰胡","碰碰胡",6));
            if (shape.Groups.All(g => g.Closed)) patterns.Add(Item("门清","门清",4));
            var attributions = new HashSet<int>();
            if (selfDraw || shape.Seven || shape.Pair == winningTile) attributions.Add(-1);
            if (!selfDraw && !shape.Seven) for (int i=0;i<shape.Groups.Count;i++) {
                var g=shape.Groups[i];
                if (!g.Declared && g.Tiles.Contains(winningTile)) attributions.Add(g.Sequence ? -1 : i);
            }
            foreach (int winningTriplet in attributions) {
                var extras = new List<YixingFlowerItem>();
                if (flowers.Count > 0) extras.Add(Item("flower_tiles","花牌",flowers.Count));
                for (int i=0;i<shape.Groups.Count;i++) {
                    var g=shape.Groups[i];
                    if (g.Sequence) continue;
                    bool closed=g.Closed && i!=winningTriplet;
                    int value;
                    if (g.Tile >= 41) value=(g.Tile >= 45 || g.Tile == seatWind ? 2 : 1)+(closed ? 1 : 0)+(g.Size == 4 ? 2 : 0);
                    else if (g.Size == 4) value=closed ? 2 : 1;
                    else continue;
                    string kind=(closed ? "暗" : "明")+(g.Size == 4 ? "杠" : "刻");
                    string name=(g.Tile >= 45 ? "箭牌" : g.Tile == seatWind ? "门风" : g.Tile >= 41 ? "客风" : "数牌")+kind;
                    extras.Add(Item($"{kind}:{g.Tile}:{i}",name,value));
                }
                int fixedFlowers=patterns.Count == 0 ? 1 : patterns.Sum(p => p.flowers)-patterns.Count+1;
                int baseFlowers=fixedFlowers+extras.Sum(p => p.flowers);
                bool single=!shape.Seven && codes.Count == 4 && hand.Count == 2;
                if (baseFlowers-1 < (single ? 1 : selfDraw ? 2 : 3)) continue;
                var factors=new List<object[]>();
                if (single) factors.Add(new object[] {"大吊车",2});
                if (selfDraw && afterKong) factors.Add(new object[] {"杠开",2});
                if (seaBottom) factors.Add(new object[] {selfDraw ? "海底捞月" : "海底放冲包三家",selfDraw ? 2 : 3});
                if (robKong) factors.Add(new object[] {"抢杠包三家",3});
                int multiplier=factors.Aggregate(1,(value,pair) => value*(int)pair[1]);
                int points=baseFlowers*multiplier;
                if (best != null && best.points >= points) continue;
                best=new YixingScoreInfo {shape=shape.Seven ? "seven_pairs" : "standard",patterns=patterns.ToArray(),extras=extras.ToArray(),
                    qualifying_flowers=baseFlowers-1,base_flowers=baseFlowers,multiplier=multiplier,multipliers=factors.ToArray(),
                    points=points,pair=shape.Pair,winning_triplet=winningTriplet >= 0 ? winningTriplet : (int?)null};
            }
        }
        return best;
    }
    public static WaitTileHint Describe(WaitHintQuery q) {
        bool seven=SevenPairs(q.DetailedConfig);
        var ron=Score(q.HandWithWin,q.Melds,q.SelfFlowers,q.HepaiTile,seatWind:41+q.SelfIndex,sevenPairs:seven);
        if (ron != null) return WaitTileHint.Ron($"{ron.base_flowers}花起");
        var tsumo=Score(q.HandWithWin,q.Melds,q.SelfFlowers,q.HepaiTile,selfDraw:true,seatWind:41+q.SelfIndex,sevenPairs:seven);
        return tsumo != null ? WaitTileHint.TsumoOnly($"自摸{tsumo.base_flowers}花起") : WaitTileHint.None("花数不足");
    }
}
