using System;
using System.Collections.Generic;
using System.Linq;

public static class HongqueWScoring {
    public const string SubRule = "hongque/w-v1.0";
    public static readonly Dictionary<string, int> FanValues = new Dictionary<string, int> {
        {"花龙",3},{"清龙",6},{"双同刻",1},{"三同刻",3},{"四同刻",6},
        {"双同顺",1},{"三同顺",3},{"四同顺",6},{"四连顺",3},
        {"清一色",18},{"混一色",12},{"清双色",9},{"混双色",6},{"三色",3},
        {"清一数",18},{"二数",12},{"三数",6},{"四数",3},{"全带幺",2},
        {"天和",18},{"海底",2},{"门清",1},{"对对和",12},{"碰碰和",2},{"平和",1},
        {"金龙",6},{"二金",3},{"三金",1},{"彩虹",6},{"双虹会",12}
    };
    private static readonly Dictionary<string, List<List<int>>> GroupCache = new Dictionary<string, List<List<int>>>();
    private static readonly Dictionary<string, HongqueWinScore> ScoreCache = new Dictionary<string, HongqueWinScore>();
    private static int Colour(int tile) => (tile - 1000) / 20;
    private static int Number(int tile) => (tile - 1000) % 20;
    public static bool IsTile(int tile) => tile >= 1001 && tile <= 1149 && Number(tile) >= 1 && Number(tile) <= 9;
    private static string Key(IEnumerable<int> tiles) => string.Join(",", tiles.OrderBy(tile => tile));
    public static IEnumerable<int> Faces(bool colourless = true) {
        for (int colour = 0; colour < (colourless ? 8 : 7); colour++)
            for (int number = 1; number <= 9; number++) yield return 1000 + colour * 20 + number;
    }
    public static void ClearCaches() { GroupCache.Clear(); ScoreCache.Clear(); }

    public static HongqueMeldShape Classify(IList<int> tiles) {
        if (tiles == null || tiles.Count == 0 || tiles.Any(tile => !IsTile(tile))) return null;
        var counts = tiles.GroupBy(tile => tile).ToDictionary(group => group.Key, group => group.Count());
        if (tiles.Count == 2 && counts.Count == 1)
            return new HongqueMeldShape {Kind="pair", BaseKind="pair", Tiles=tiles.ToList()};
        bool doubled = counts.Values.All(count => count == 2);
        if (counts.Count < 3 || (!doubled && counts.Values.Any(count => count != 1))) return null;
        var colours = counts.Keys.Select(Colour).Distinct().ToArray();
        var numbers = counts.Keys.Select(Number).OrderBy(number => number).ToArray();
        bool rainbow = colours.Length == 7 && !colours.Contains(7) && counts.Count == 7;
        string baseKind = null;
        int step = numbers[1] - numbers[0];
        if (numbers.Distinct().Count() == 1 && !colours.Contains(7) && colours.Length == counts.Count)
            baseKind = "triplet";
        else if (step > 0 && Enumerable.Range(1, numbers.Length - 1).All(i => numbers[i] - numbers[i - 1] == step)
                 && (colours.Length == 1 || (rainbow && step == 1))) baseKind = "sequence";
        if (baseKind == null) return null;
        return new HongqueMeldShape {Kind=doubled ? "double" : rainbow ? "rainbow" : baseKind,
            BaseKind=baseKind, Tiles=tiles.OrderBy(tile => tile).ToList(), NumberStep=step,
            IsRainbow=rainbow, IsDouble=doubled};
    }

    private static List<int> Subtract(IEnumerable<int> hand, IEnumerable<int> group) {
        var rest = hand.ToList();
        foreach (int tile in group) if (!rest.Remove(tile)) return null;
        rest.Sort();
        return rest;
    }
    private static IEnumerable<int[]> Choose(int[] items, int count, int start=0) {
        if (count == 0) { yield return Array.Empty<int>(); yield break; }
        for (int index = start; index <= items.Length - count; index++)
            foreach (var tail in Choose(items, count - 1, index + 1))
                yield return new[] {items[index]}.Concat(tail).ToArray();
    }
    public static List<List<int>> Groups(IList<int> hand) {
        string key = Key(hand);
        if (GroupCache.TryGetValue(key, out var cached)) return cached;
        var counts = hand.GroupBy(tile => tile).ToDictionary(group => group.Key, group => group.Count());
        var found = new Dictionary<string, List<int>>();
        if (counts.Any(pair => !IsTile(pair.Key) || pair.Value > 2)) return new List<List<int>>();
        foreach (var pair in counts.Where(pair => pair.Value == 2)) {
            var group = new List<int> {pair.Key, pair.Key}; found[Key(group)] = group;
        }
        for (int copies = 1; copies <= 2; copies++) {
            int multiplicity = copies;
            var available = new HashSet<int>(counts.Where(pair => pair.Value >= copies).Select(pair => pair.Key));
            Action<IEnumerable<int>> add = group => {
                var full = group.SelectMany(tile => Enumerable.Repeat(tile, multiplicity)).OrderBy(tile => tile).ToList();
                found[Key(full)] = full;
            };
            for (int rank = 1; rank <= 9; rank++) {
                int[] ranked = available.Where(tile => Number(tile) == rank && Colour(tile) != 7).OrderBy(tile => tile).ToArray();
                for (int size = 3; size <= ranked.Length; size++) foreach (var group in Choose(ranked, size)) add(group);
            }
            for (int colour = 0; colour < 8; colour++) for (int step = 1; step <= 4; step++)
                for (int start = 1; start < 10 - 2 * step; start++) {
                    var group = new List<int>();
                    for (int rank = start; rank <= 9; rank += step) {
                        int tile = 1000 + colour * 20 + rank;
                        if (!available.Contains(tile)) break;
                        group.Add(tile); if (group.Count >= 3) add(group);
                    }
                }
            for (int start = 1; start <= 3; start++) {
                var options = Enumerable.Range(start, 7).Select(rank => available.Where(tile => Number(tile) == rank && Colour(tile) < 7).ToArray()).ToArray();
                Action<int,int,List<int>> rainbow = null;
                rainbow = (index, used, group) => {
                    if (index == 7) { add(group); return; }
                    foreach (int tile in options[index]) {
                        int bit = 1 << Colour(tile); if ((used & bit) != 0) continue;
                        group.Add(tile); rainbow(index + 1, used | bit, group); group.RemoveAt(group.Count - 1);
                    }
                };
                if (options.All(option => option.Length > 0)) rainbow(0, 0, new List<int>());
            }
        }
        var result = found.Values.OrderByDescending(group => group.Count).ThenBy(Key).ToList();
        if (GroupCache.Count >= 8192) GroupCache.Clear();
        GroupCache[key] = result;
        return result;
    }

    private static IEnumerable<List<List<int>>> Partitions(List<int> hand, bool paired=false) {
        if (hand.Count == 0) { yield return new List<List<int>>(); yield break; }
        foreach (var group in Groups(hand)) {
            if (!group.Contains(hand[0]) || (group.Count == 2 && paired)) continue;
            foreach (var tail in Partitions(Subtract(hand, group), paired || group.Count == 2))
                yield return new[] {group}.Concat(tail).ToList();
        }
    }

    private static List<string> BestFamily(List<(int mask, string name)> candidates) {
        var cache = new Dictionary<(int,int), (int score,List<string> names)>();
        Func<int,int,(int score,List<string> names)> solve = null;
        solve = (index, used) => {
            if (index == candidates.Count) return (0,new List<string>());
            if (cache.TryGetValue((index,used), out var memo)) return memo;
            var best = solve(index+1,used); var candidate = candidates[index];
            if ((used & candidate.mask) == 0) {
                var tail = solve(index+1,used|candidate.mask); int score = tail.score + FanValues[candidate.name];
                if (score > best.score) best = (score, new[] {candidate.name}.Concat(tail.names).ToList());
            }
            cache[(index,used)] = best; return best;
        };
        return solve(0,0).names;
    }

    public static HongqueWinScore ScorePartition(List<List<int>> partition, List<List<int>> opened,
            bool allPairs=false, bool selfDraw=false, bool heavenly=false, bool lastTile=false) {
        var groups = partition.Concat(opened).ToList(); var shapes = groups.Select(Classify).ToList();
        if (groups.Count == 0 || shapes.Any(shape => shape == null)) return null;
        var fans = new Dictionary<string,int>();
        Action<string> add = name => fans[name] = fans.TryGetValue(name,out int count) ? count+1 : 1;
        if (!allPairs) {
            var expanded = shapes.Where(shape => shape.BaseKind != "pair").SelectMany(shape => Enumerable.Repeat(shape,shape.IsDouble ? 2 : 1)).ToList();
            var nums = expanded.Select(shape => shape.Tiles.Distinct().Select(Number).OrderBy(number => number).ToArray()).ToList();
            var sameTriplets = new List<(int,string)>(); var sameSequences = new List<(int,string)>();
            var consecutive = new List<(int,string)>(); var dragons = new List<(int,string)>();
            foreach (string kind in new[] {"triplet","sequence"}) {
                int[] indexes = Enumerable.Range(0,expanded.Count).Where(index => expanded[index].BaseKind == kind).ToArray();
                for (int size = 2; size <= 4; size++) foreach (var group in Choose(indexes,size)) {
                    bool same = kind == "triplet" ? group.Select(index => nums[index][0]).Distinct().Count() == 1
                        : group.Select(index => string.Join(",",nums[index])).Distinct().Count() == 1;
                    if (same) (kind == "triplet" ? sameTriplets : sameSequences).Add((group.Sum(index => 1 << index),
                        new[] {"双","三","四"}[size-2]+"同"+(kind == "triplet" ? "刻" : "顺")));
                }
            }
            int[] seq = Enumerable.Range(0,expanded.Count).Where(index => expanded[index].BaseKind == "sequence").ToArray();
            foreach (var group in Choose(seq,4)) {
                int[] starts = group.Select(index => nums[index][0]).OrderBy(number => number).ToArray();
                if (group.Select(index => (nums[index].Length,expanded[index].NumberStep)).Distinct().Count() == 1
                    && starts[1] > starts[0] && Enumerable.Range(1,3).Select(index => starts[index]-starts[index-1]).Distinct().Count() == 1)
                    consecutive.Add((group.Sum(index => 1 << index),"四连顺"));
            }
            for (int size = 1; size <= 3; size++) foreach (var group in Choose(seq,size)) {
                if (group.SelectMany(index => nums[index]).OrderBy(number => number).SequenceEqual(Enumerable.Range(1,9))
                    && group.Select(index => expanded[index].NumberStep).Distinct().Count() == 1) {
                    bool pure = group.SelectMany(index => expanded[index].Tiles).Select(Colour).Distinct().Count() == 1;
                    dragons.Add((group.Sum(index => 1 << index),pure ? "清龙" : "花龙"));
                }
            }
            foreach (var family in new[] {sameTriplets,sameSequences,consecutive,dragons}) foreach (string name in BestFamily(family)) add(name);
        }
        var tiles = groups.SelectMany(group => group).ToList(); var colours = tiles.Select(Colour).Distinct().ToArray();
        var numbers = tiles.Select(Number).Distinct().OrderBy(number => number).ToArray(); int coloured = colours.Count(colour => colour != 7);
        if (colours.Length == 1) add("清一色");
        else if (colours.Contains(7) && coloured == 1) add("混一色");
        else if (!colours.Contains(7) && coloured == 2) add("清双色");
        else if (colours.Contains(7) && coloured == 2) add("混双色");
        else if (colours.Length == 3) add("三色");
        if (numbers.Length == 1) add("清一数"); else if (numbers.Length == 2) add("二数");
        else if ((numbers.Length == 3 || numbers.Length == 4) && Enumerable.Range(1,numbers.Length-1).Select(index => numbers[index]-numbers[index-1]).Distinct().Count() == 1)
            add(numbers.Length == 3 ? "三数" : "四数");
        if (allPairs ? tiles.All(tile => Number(tile) == 1 || Number(tile) == 9) : groups.All(group => group.Any(tile => Number(tile) == 1 || Number(tile) == 9))) add("全带幺");
        if (selfDraw && heavenly && opened.Count == 0) add("天和"); else if (opened.Count == 0) add("门清");
        if (selfDraw && lastTile) add("海底");
        if (allPairs) add("对对和");
        else if (shapes.All(shape => shape.BaseKind == "triplet") && numbers.Length > 2) add("碰碰和");
        else if (shapes.All(shape => shape.BaseKind == "sequence")) add("平和");
        if (groups.Count <= 3) add(new[] {"金龙","二金","三金"}[groups.Count-1]);
        int rainbows = shapes.Where(shape => shape.IsRainbow).Sum(shape => shape.IsDouble ? 2 : 1);
        if (rainbows > 0) add(rainbows >= 2 ? "双虹会" : "彩虹");
        int basis = 3 + groups.Sum(group => Math.Max(0,group.Count-3));
        var entries = FanValues.Where(pair => fans.ContainsKey(pair.Key)).Select(pair => new HongqueFanInfo {
            name=pair.Key, value=pair.Value, count=fans[pair.Key], total=pair.Value*fans[pair.Key]}).ToArray();
        int total = entries.Sum(fan => fan.total);
        return new HongqueWinScore {Base=basis,FanTotal=total,Points=total == 0 ? 1 : basis*total,Fans=entries};
    }

    public static HongqueWinScore BestWinResult(List<int> hand, List<int[]> meldMasks, bool selfDraw=false,
            bool heavenly=false, bool lastTile=false, bool allowKongWin=false, int minimumFan=0, int minimumPoints=0) {
        var opened = (meldMasks ?? new List<int[]>()).Select(mask => Enumerable.Range(0,mask.Length/2).Select(index => mask[index*2+1]).ToList()).ToList();
        return Best(hand,opened,selfDraw,heavenly,lastTile,allowKongWin,minimumFan,minimumPoints);
    }
    private static HongqueWinScore Best(List<int> hand, List<List<int>> opened, bool selfDraw, bool heavenly, bool lastTile, bool allowKongWin, int minimumFan, int minimumPoints) {
        if (hand == null) return null;
        string key = Key(hand)+"/"+string.Join(";",opened.Select(Key))+"/"+selfDraw+heavenly+lastTile+allowKongWin+"/"+minimumFan+"/"+minimumPoints;
        if (ScoreCache.TryGetValue(key,out var cached)) return cached;
        var tiles = hand.Concat(opened.SelectMany(group => group)).ToArray();
        if (tiles.Length == 0 || tiles.Any(tile => !IsTile(tile)) || tiles.GroupBy(tile => tile).Any(group => group.Count() > 2)
            || opened.Any(group => Classify(group) == null || Classify(group).BaseKind == "pair")) return null;
        HongqueWinScore best = null;
        Action<HongqueWinScore> consider = score => {
            if (score != null && score.FanTotal >= minimumFan && score.Points >= minimumPoints && (best == null || score.Points > best.Points || (score.Points == best.Points && score.FanTotal > best.FanTotal)
                || (score.Points == best.Points && score.FanTotal == best.FanTotal && score.Base > best.Base))) best=score;
        };
        foreach (var partition in Partitions(hand.OrderBy(tile => tile).ToList())) consider(ScorePartition(partition,opened,false,selfDraw,heavenly,lastTile));
        if (hand.GroupBy(tile => tile).All(group => group.Count() == 2) && opened.All(group => Classify(group).IsDouble))
            consider(ScorePartition(hand.Distinct().Select(tile => new List<int> {tile,tile}).ToList(),opened,true,selfDraw,heavenly,lastTile));
        if (selfDraw && allowKongWin) for (int index = 0; index < opened.Count; index++) {
            foreach (var group in Groups(hand.Concat(opened[index]).ToList())) {
                if (group.Count <= opened[index].Count) continue;
                var added = Subtract(group,opened[index]); if (added == null) continue;
                var remaining = Subtract(hand,added); if (remaining == null) continue;
                var next = opened.Select(part => part.ToList()).ToList(); next[index]=group;
                consider(Best(remaining,next,true,false,lastTile,true,minimumFan,minimumPoints));
            }
        }
        if (ScoreCache.Count >= 4096) ScoreCache.Clear(); ScoreCache[key]=best;
        return best;
    }
}
