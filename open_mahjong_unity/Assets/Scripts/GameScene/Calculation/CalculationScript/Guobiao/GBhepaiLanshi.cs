using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>蓝十第4版四人规则。独立拆分、和张归属和无环番种组合。</summary>
public static class GBhepaiLanshi {
    public const string RuleVersion = "lanshi-v4-2026";
    internal static readonly string[] FanKeys = new string[] {
        "qixingdui", "sitongshun", "jiulianbaodeng", "sigang", "dasixi", "qingyaojiu",
        "sianke", "shisanyao", "ziyise", "silianshun", "silianke", "xiaosixi",
        "sangang", "dasanyuan", "shunwang", "santongshun", "shunlian", "hunyaojiu",
        "quanda", "quanzhong", "quanxiao", "quandaiwu", "santongke", "xiaosanyuan",
        "quanbukao", "sanfengke", "sananke", "sanlianke", "qingyise", "sanlianshun",
        "sanselianke", "qingquandaiyao", "shuanggang", "dayuwu", "xiaoyuwu", "qiduizi",
        "shunhuan", "shuangjianke", "qinglong", "miaoshouhuichun", "haidilaoyue", "gangshangkaihua",
        "qiangganghe", "tianhe", "dihe", "hualong", "sansetongshun", "pengpenghe",
        "hunquandaiyao", "hunyise", "sanselianshun", "angang", "shuanganke", "wumenqi",
        "shuangtongke", "quanqiuren", "siguiyi", "yibangao", "hejuezhang", "jianke",
        "quanfengke", "menfengke", "menqianqing", "minggang", "duanyao", "xixiangfeng",
        "lianliu", "laoshaofu", "yaojiuke", "zimo",
    };
    internal static readonly string[] FanNames = new string[] {
        "七星对", "四同顺", "九莲宝灯", "四杠", "大四喜", "清幺九",
        "四暗刻", "十三幺", "字一色", "四连顺", "四连刻", "小四喜",
        "三杠", "大三元", "顺网", "三同顺", "顺链", "混幺九",
        "全大", "全中", "全小", "全带五", "三同刻", "小三元",
        "全不靠", "三风刻", "三暗刻", "三连刻", "清一色", "三连顺",
        "三色连刻", "清全带幺", "双杠", "大于五", "小于五", "七对",
        "顺环", "双箭刻", "清龙", "妙手回春", "海底捞月", "杠上开花",
        "抢杠和", "天和", "地和", "花龙", "三色同顺", "碰碰和",
        "混全带幺", "混一色", "三色连顺", "暗杠", "双暗刻", "五门齐",
        "双同刻", "全求人", "四归一", "一般高", "和绝张", "箭刻",
        "圈风刻", "门风刻", "门前清", "明杠", "断幺", "喜相逢",
        "连六", "老少副", "幺九刻", "自摸",
    };
    internal static readonly int[] FanValues = new int[] {
        100, 100, 100, 100, 72, 72, 48, 48, 40, 40,
        40, 32, 32, 24, 24, 24, 24, 16, 16, 16,
        16, 16, 16, 16, 16, 12, 12, 12, 12, 12,
        8, 8, 8, 8, 8, 8, 8, 6, 6, 5,
        5, 5, 5, 5, 5, 4, 4, 3, 3, 3,
        3, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        2, 2, 1, 1, 1, 1, 1, 1, 1, 1,
    };
    private static readonly HashSet<string> Repeatable = new HashSet<string> { "siguiyi", "shuangtongke", "yibangao", "xixiangfeng", "lianliu", "laoshaofu", "yaojiuke" };
    private static readonly string[] Occasional = { "miaoshouhuichun", "haidilaoyue", "gangshangkaihua", "qiangganghe", "tianhe", "dihe" };
    private static readonly int[] Tiles = Enumerable.Range(1, 3).SelectMany(s => Enumerable.Range(1, 9).Select(r => s * 10 + r)).Concat(Enumerable.Range(41, 7)).ToArray();
    private static readonly HashSet<int> Valid = new HashSet<int>(Tiles);
    private static readonly HashSet<int> Honors = new HashSet<int>(Enumerable.Range(41, 7));
    private static readonly HashSet<int> Terminals = new HashSet<int> { 11, 19, 21, 29, 31, 39 };
    private static readonly HashSet<int> Orphans = new HashSet<int>(Terminals.Concat(Honors));
    private static readonly Dictionary<string, int> FanIndex = FanKeys.Select((key, index) => new { key, index }).ToDictionary(x => x.key, x => x.index);

    private readonly struct Meld {
        public readonly char Kind;
        public readonly int Tile;
        public Meld(char kind, int tile) { Kind = kind; Tile = tile; }
        public string Token => Kind.ToString() + Tile;
        public bool Sequence => Kind == 's' || Kind == 'S';
        public bool Triplet => "kKgG".IndexOf(Kind) >= 0;
        public int[] Expand() => Sequence ? new[] { Tile - 1, Tile, Tile + 1 } : Enumerable.Repeat(Tile, "gG".IndexOf(Kind) >= 0 ? 4 : Kind == 'q' ? 2 : 3).ToArray();
    }
    private sealed class Relation {
        public readonly string Fan;
        public readonly int[] Nodes;
        public Relation(string fan, int[] nodes) { Fan = fan; Nodes = nodes; }
    }
    private sealed class Shape {
        public string Name;
        public List<Meld> Groups;
        public Shape(string name, List<Meld> groups) { Name = name; Groups = groups; }
    }
    private sealed class Scored {
        public int Score, Raw;
        public List<string> Fans;
        public List<string> Keys;
        public string Identity;
    }

    public static Tuple<int, List<string>> HepaiCheck(List<int> handList, List<string> tilesCombination, List<string> wayToHepai, int getTile, bool debug = false) {
        if (!Validate(handList, tilesCombination, getTile, out List<Meld> fixedGroups)) return Tuple.Create(0, new List<string>());
        var way = new HashSet<string>(wayToHepai ?? new List<string>());
        if (way.Contains("last_deal")) way.Add("妙手回春");
        if (way.Contains("last_cut")) way.Add("海底捞月");
        bool selfDraw = way.Overlaps(new[] { "自摸", "妙手回春", "杠上开花", "天和" });
        string occasional = Occasional.FirstOrDefault(key => way.Contains(FanNames[FanIndex[key]]));
        var common = new List<string>();
        if (selfDraw) common.Add("zimo");
        if (way.Contains("和绝张") || way.Contains("抢杠和")) common.Add("hejuezhang");
        Scored best = null;
        foreach (Shape shape in Decompose(handList, fixedGroups, getTile, selfDraw)) {
            var fans = new List<string>(common);
            fans.AddRange(TileFans(handList, fixedGroups, shape.Name));
            List<Meld> relationGroups = new List<Meld>();
            if (shape.Name != "standard") fans.Add(shape.Name);
            else {
                if (!fixedGroups.Any(g => "skg".IndexOf(g.Kind) >= 0)) fans.Add("menqianqing");
                fans.AddRange(StandardFans(handList, fixedGroups, shape.Groups, way, selfDraw));
                relationGroups = shape.Groups.Where(g => g.Kind != 'q').ToList();
                if (fixedGroups.Count == 0 && handList.All(t => t < 40 && t / 10 == handList[0] / 10)) {
                    var before = new List<int>(handList);
                    before.Remove(getTile);
                    if (before.Select(t => t % 10).OrderBy(t => t).SequenceEqual(new[] { 1,1,1,2,3,4,5,6,7,8,9,9,9 })) fans.Add("jiulianbaodeng");
                }
            }
            Scored bestShape = null;
            VisitRelations(relationGroups, selected => {
                var regular = Exclude(fans.Concat(selected.Select(r => r.Fan)).ToList());
                int raw = regular.Sum(key => FanValues[FanIndex[key]]);
                var final = occasional != null && raw < 5 ? new List<string> { occasional } : regular;
                Scored scored = Score(final);
                scored.Raw = raw;
                scored.Identity = string.Join("\u0001", shape.Groups.Select(g => g.Token));
                if (bestShape == null || scored.Score > bestShape.Score ||
                    (scored.Score == bestShape.Score && (scored.Raw > bestShape.Raw ||
                    (scored.Raw == bestShape.Raw && PreferTableOrder(scored.Keys, bestShape.Keys))))) bestShape = scored;
            });
            if (best == null || bestShape.Score > best.Score || (bestShape.Score == best.Score &&
                (CompareKeys(bestShape.Keys, best.Keys) < 0 || (CompareKeys(bestShape.Keys, best.Keys) == 0 &&
                    string.CompareOrdinal(bestShape.Identity, best.Identity) < 0)))) best = bestShape;
        }
        return best == null ? Tuple.Create(0, new List<string>()) : Tuple.Create(best.Score, best.Fans);
    }

    public static HashSet<int> TingpaiCheck(List<int> hand, List<string> melds) {
        var waits = new HashSet<int>();
        if (hand == null || melds == null || hand.Count + 3 * melds.Count != 13) return waits;
        foreach (int tile in Tiles) {
            var complete = new List<int>(hand) { tile };
            if (Validate(complete, melds, tile, out List<Meld> fixedGroups) && Decompose(complete, fixedGroups, tile, true).Any()) waits.Add(tile);
        }
        return waits;
    }

    private static bool Validate(List<int> hand, List<string> tokens, int win, out List<Meld> fixedGroups) {
        fixedGroups = new List<Meld>();
        if (hand == null || tokens == null || tokens.Count > 4 || hand.Count != 14 - 3 * tokens.Count || !hand.Contains(win) || hand.Any(t => !Valid.Contains(t))) return false;
        int[] physical = new int[48];
        foreach (int t in hand) if (++physical[t] > 4) return false;
        foreach (string token in tokens) {
            if (token == null || token.Length != 3 || "skgG".IndexOf(token[0]) < 0 || !int.TryParse(token.Substring(1), out int tile) || !Valid.Contains(tile)) return false;
            if (token[0] == 's' && (tile >= 40 || tile % 10 < 2 || tile % 10 > 8)) return false;
            var meld = new Meld(token[0], tile);
            foreach (int t in meld.Expand()) if (++physical[t] > 4) return false;
            fixedGroups.Add(meld);
        }
        return true;
    }

    private static IEnumerable<Shape> Decompose(List<int> hand, List<Meld> fixedGroups, int win, bool selfDraw) {
        var kinds = new HashSet<int>(hand);
        int[] counts = new int[48];
        foreach (int tile in hand) counts[tile]++;
        if (fixedGroups.Count == 0) {
            if (kinds.Count == 7 && kinds.All(t => counts[t] == 2)) yield return new Shape(kinds.SetEquals(Honors) ? "qixingdui" : "qiduizi", new List<Meld>());
            if (kinds.SetEquals(Orphans) && kinds.Count(t => counts[t] == 2) == 1) yield return new Shape("shisanyao", new List<Meld>());
            if (kinds.Count == 14 && Unrelated(kinds)) yield return new Shape("quanbukao", new List<Meld>());
        }
        foreach (int pair in Tiles) {
            if (counts[pair] < 2) continue;
            counts[pair] -= 2;
            foreach (List<Meld> groups in ClosedGroups(counts)) {
                var closed = new List<Meld>(groups) { new Meld('q', pair) };
                for (int i = 0; i < closed.Count; i++) {
                    if (!closed[i].Expand().Contains(win)) continue;
                    var completed = new List<Meld>(closed);
                    if (!selfDraw && completed[i].Kind == 'K') completed[i] = new Meld('k', completed[i].Tile);
                    yield return new Shape("standard", fixedGroups.Concat(completed).ToList());
                }
            }
            counts[pair] += 2;
        }
    }

    private static IEnumerable<List<Meld>> ClosedGroups(int[] counts) {
        int first = 0;
        foreach (int t in Tiles) if (counts[t] > 0) { first = t; break; }
        if (first == 0) { yield return new List<Meld>(); yield break; }
        var choices = new List<Meld>();
        if (counts[first] >= 3) choices.Add(new Meld('K', first));
        if (first < 40 && first % 10 <= 7 && counts[first + 1] > 0 && counts[first + 2] > 0) choices.Add(new Meld('S', first + 1));
        foreach (Meld group in choices) {
            foreach (int tile in group.Expand()) counts[tile]--;
            foreach (List<Meld> tail in ClosedGroups(counts)) yield return new[] { group }.Concat(tail).ToList();
            foreach (int tile in group.Expand()) counts[tile]++;
        }
    }

    private static bool Unrelated(HashSet<int> hand) {
        for (int a = 1; a <= 3; a++) for (int b = 1; b <= 3; b++) {
            if (a == b) continue;
            int[] offsets = { a, b, 6 - a - b };
            if (hand.All(t => t >= 40 || (t % 10 - offsets[t / 10 - 1]) % 3 == 0)) return true;
        }
        return false;
    }

    private static string LowRelation((int suit, int rank) a, (int suit, int rank) b) {
        if (a == b) return "yibangao";
        if (a.suit != b.suit && a.rank == b.rank) return "xixiangfeng";
        if (a.suit == b.suit && Math.Abs(a.rank - b.rank) == 3) return "lianliu";
        if (a.suit == b.suit && Math.Min(a.rank, b.rank) == 1 && Math.Max(a.rank, b.rank) == 7) return "laoshaofu";
        return null;
    }

    private static IEnumerable<int[]> Subsets(int n, int size) {
        for (int mask = 0; mask < (1 << n); mask++) {
            var values = new List<int>();
            for (int i = 0; i < n; i++) if ((mask & (1 << i)) != 0) values.Add(i);
            if (values.Count == size) yield return values.ToArray();
        }
    }

    private static List<Relation> RelationCandidates(List<Meld> groups) {
        var result = new List<Relation>();
        for (int size = 2; size <= 4; size++) foreach (int[] nodes in Subsets(groups.Count, size)) {
            var selected = nodes.Select(i => groups[i]).ToArray();
            bool sequences = selected.All(g => g.Sequence);
            bool triplets = selected.All(g => g.Triplet && g.Tile < 40);
            if (!sequences && !triplets) continue;
            var values = selected.Select(g => (suit: g.Tile / 10, rank: g.Tile % 10 - (sequences ? 1 : 0))).ToArray();
            var suits = values.Select(v => v.suit).Distinct().ToArray();
            var ranks = values.Select(v => v.rank).OrderBy(r => r).ToArray();
            bool sameSuit = suits.Length == 1, allSuits = suits.Length == 3;
            bool consecutive = ranks.SequenceEqual(Enumerable.Range(ranks[0], size));
            var names = new List<string>();
            if (size == 2) {
                string name = sequences ? LowRelation(values[0], values[1]) : (suits.Length == 2 && ranks[0] == ranks[1] ? "shuangtongke" : null);
                if (name != null) names.Add(name);
            } else if (size == 3) {
                if (sameSuit && consecutive) names.Add(sequences ? "sanlianshun" : "sanlianke");
                if (allSuits && consecutive) names.Add(sequences ? "sanselianshun" : "sanselianke");
                if (ranks.Distinct().Count() == 1) {
                    if (allSuits) names.Add(sequences ? "sansetongshun" : "santongke");
                    else if (sequences && sameSuit) names.Add("santongshun");
                }
                if (sequences && ranks.SequenceEqual(new[] { 1, 4, 7 })) {
                    if (sameSuit) names.Add("qinglong");
                    else if (allSuits) names.Add("hualong");
                }
            } else {
                if (sameSuit && consecutive) names.Add(sequences ? "silianshun" : "silianke");
                if (sequences) {
                    var frequencies = values.GroupBy(v => v).ToArray();
                    if (frequencies.Length == 1) names.Add("sitongshun");
                    if (sameSuit && ranks.SequenceEqual(new[] { 1,3,5,7 })) names.Add("shunlian");
                    if (frequencies.Length == 2 && frequencies.All(g => g.Count() == 2)) {
                        string low = LowRelation(frequencies[0].Key, frequencies[1].Key);
                        if (low == "xixiangfeng" || low == "lianliu" || low == "laoshaofu") names.Add("shunwang");
                    }
                    if (suits.Length == 2 && frequencies.Length == 4) {
                        var left = values.Where(v => v.suit == suits[0]).Select(v => v.rank).OrderBy(r => r).ToArray();
                        var right = values.Where(v => v.suit == suits[1]).Select(v => v.rank).OrderBy(r => r).ToArray();
                        if (left.Length == 2 && left.SequenceEqual(right) && (left[1] - left[0] == 3 || left.SequenceEqual(new[] { 1,7 }))) names.Add("shunhuan");
                    }
                }
            }
            result.AddRange(names.Select(name => new Relation(name, nodes)));
        }
        return result;
    }

    private static void VisitRelations(List<Meld> groups, Action<List<Relation>> visit) {
        List<Relation> candidates = RelationCandidates(groups);
        void Search(int start, int[] components, List<Relation> selected) {
            visit(selected);
            for (int i = start; i < candidates.Count; i++) {
                Relation relation = candidates[i];
                var roots = new HashSet<int>(relation.Nodes.Select(n => components[n]));
                if (roots.Count != relation.Nodes.Length) continue;
                int root = roots.Min();
                int[] joined = components.Select(n => roots.Contains(n) ? root : n).ToArray();
                selected.Add(relation);
                Search(i + 1, joined, selected);
                selected.RemoveAt(selected.Count - 1);
            }
        }
        Search(0, Enumerable.Range(0, groups.Count).ToArray(), new List<Relation>());
    }

    private static List<string> TileFans(List<int> hand, List<Meld> fixedGroups, string shape) {
        var tiles = hand.Concat(fixedGroups.SelectMany(g => g.Expand())).ToArray();
        var kinds = new HashSet<int>(tiles);
        var suits = kinds.Where(t => t < 40).Select(t => t / 10).Distinct().ToArray();
        var fans = new List<string>();
        if (suits.Length == 1) fans.Add(kinds.Overlaps(Honors) ? "hunyise" : "qingyise");
        if (suits.Length == 3 && kinds.Any(t => t >= 41 && t <= 44) && kinds.Any(t => t >= 45) && shape != "shisanyao" && shape != "quanbukao") fans.Add("wumenqi");
        if (kinds.All(t => t < 40 && t % 10 >= 2 && t % 10 <= 8)) fans.Add("duanyao");
        if (!kinds.Overlaps(Honors)) {
            var ranks = kinds.Select(t => t % 10).ToArray();
            if (ranks.All(r => r >= 7)) fans.Add("quanda");
            else if (ranks.All(r => r >= 6)) fans.Add("dayuwu");
            if (ranks.All(r => r <= 3)) fans.Add("quanxiao");
            else if (ranks.All(r => r <= 4)) fans.Add("xiaoyuwu");
            if (ranks.All(r => r >= 4 && r <= 6)) fans.Add("quanzhong");
        }
        var kongs = new HashSet<int>(fixedGroups.Where(g => "gG".IndexOf(g.Kind) >= 0).Select(g => g.Tile));
        foreach (var group in tiles.GroupBy(t => t)) if (group.Count() == 4 && !kongs.Contains(group.Key)) fans.Add("siguiyi");
        return fans;
    }

    private static List<string> HonorFans(List<Meld> groups, int pair, HashSet<string> way) {
        var trips = new HashSet<int>(groups.Where(g => g.Triplet).Select(g => g.Tile));
        var winds = new HashSet<int>(trips.Where(t => t >= 41 && t <= 44));
        var dragons = new HashSet<int>(trips.Where(t => t >= 45));
        var fans = new List<string>();
        var covered = new HashSet<int>();
        if (winds.Count == 4) { fans.Add("dasixi"); covered.UnionWith(winds); }
        else if (winds.Count == 3) { fans.Add(pair >= 41 && pair <= 44 ? "xiaosixi" : "sanfengke"); covered.UnionWith(winds); }
        if (dragons.Count == 3) { fans.Add("dasanyuan"); covered.UnionWith(dragons); }
        else if (dragons.Count == 2) { fans.Add(pair >= 45 ? "xiaosanyuan" : "shuangjianke"); covered.UnionWith(dragons); }
        else if (dragons.Count == 1) { fans.Add("jianke"); covered.UnionWith(dragons); }
        if (winds.Count != 4) for (int i = 0; i < 4; i++) {
            int tile = 41 + i;
            if (!winds.Contains(tile)) continue;
            if (way.Contains("场风" + "东南西北"[i])) { fans.Add("quanfengke"); covered.Add(tile); }
            if (way.Contains("自风" + "东南西北"[i])) { fans.Add("menfengke"); covered.Add(tile); }
        }
        foreach (int tile in trips) if (Orphans.Contains(tile) && !covered.Contains(tile)) fans.Add("yaojiuke");
        return fans;
    }

    private static List<string> StandardFans(List<int> hand, List<Meld> fixedGroups, List<Meld> groups, HashSet<string> way, bool selfDraw) {
        int pair = groups.First(g => g.Kind == 'q').Tile;
        var melds = groups.Where(g => g.Kind != 'q').ToList();
        var trips = melds.Where(g => g.Triplet).ToList();
        var kongs = fixedGroups.Where(g => "gG".IndexOf(g.Kind) >= 0).ToList();
        int concealed = trips.Count(g => g.Kind == 'K' || g.Kind == 'G');
        var fans = HonorFans(melds, pair, way);
        if (trips.Count == 4) {
            fans.Add("pengpenghe");
            var kinds = new HashSet<int>(trips.Select(g => g.Tile)) { pair };
            if (kinds.IsSubsetOf(Honors)) fans.Add("ziyise");
            else if (kinds.IsSubsetOf(Terminals)) fans.Add("qingyaojiu");
            else if (kinds.IsSubsetOf(Orphans)) fans.Add("hunyaojiu");
        }
        if (concealed >= 2) fans.Add(concealed == 2 ? "shuanganke" : concealed == 3 ? "sananke" : "sianke");
        if (kongs.Count >= 2) fans.Add(kongs.Count == 2 ? "shuanggang" : kongs.Count == 3 ? "sangang" : "sigang");
        else if (kongs.Count == 1) fans.Add(kongs[0].Kind == 'G' ? "angang" : "minggang");
        if (groups.All(g => g.Expand().Any(t => t < 40 && t % 10 == 5))) fans.Add("quandaiwu");
        if (groups.All(g => g.Expand().Any(Orphans.Contains))) fans.Add(groups.Any(g => g.Tile >= 40) ? "hunquandaiyao" : "qingquandaiyao");
        if (!selfDraw && fixedGroups.Count == 4 && fixedGroups.All(g => "skg".IndexOf(g.Kind) >= 0)) fans.Add("quanqiuren");
        if (fixedGroups.Count == 0 && selfDraw && way.Contains("庄家起手") && hand.All(t => t < 40 && t / 10 == hand[0] / 10)) {
            int[] ranks = new int[10];
            foreach (int tile in hand) ranks[tile % 10]++;
            if (ranks[1] >= 3 && ranks[9] >= 3 && Enumerable.Range(2, 7).All(r => ranks[r] >= 1)) fans.Add("jiulianbaodeng");
        }
        return fans;
    }

    private static List<string> Exclude(List<string> fans) {
        string limit = FanKeys.FirstOrDefault(key => FanValues[FanIndex[key]] == 100 && fans.Contains(key));
        if (limit != null) return new List<string> { limit };
        if (fans.Any(f => f == "dasixi" || f == "qingyaojiu" || f == "sianke" || f == "ziyise" || f == "silianke" || f == "hunyaojiu")) fans.RemoveAll(f => f == "pengpenghe");
        if (fans.Any(f => f == "qingyaojiu" || f == "ziyise" || f == "hunyaojiu")) fans.RemoveAll(f => f == "yaojiuke");
        if (fans.Contains("sianke")) fans.RemoveAll(f => f == "menqianqing");
        if (fans.Contains("quanzhong") || fans.Contains("quandaiwu")) fans.RemoveAll(f => f == "duanyao");
        return fans.OrderBy(key => FanIndex[key]).ToList();
    }

    private static Scored Score(List<string> fans) {
        var counts = fans.GroupBy(key => key).ToDictionary(g => g.Key, g => g.Count());
        var result = new Scored { Score = Math.Min(100, fans.Sum(key => FanValues[FanIndex[key]])), Fans = new List<string>(), Keys = new List<string>() };
        foreach (string key in FanKeys) if (counts.TryGetValue(key, out int count)) {
            string suffix = Repeatable.Contains(key) ? "*" + count : "";
            result.Fans.Add(FanNames[FanIndex[key]] + suffix);
            result.Keys.Add(key + suffix);
        }
        return result;
    }
    private static int CompareKeys(List<string> left, List<string> right) {
        for (int i = 0; i < Math.Min(left.Count, right.Count); i++) { int value = string.CompareOrdinal(left[i], right[i]); if (value != 0) return value; }
        return left.Count.CompareTo(right.Count);
    }
    private static bool PreferTableOrder(List<string> left, List<string> right) {
        var a = ExpandKeys(left); var b = ExpandKeys(right);
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++) if (a[i] != b[i]) return a[i] < b[i];
        return a.Count > b.Count;
    }
    private static List<int> ExpandKeys(List<string> keys) {
        var result = new List<int>();
        foreach (string key in keys) { string[] parts = key.Split('*'); int count = parts.Length == 2 ? int.Parse(parts[1]) : 1; for (int i = 0; i < count; i++) result.Add(FanIndex[parts[0]]); }
        return result;
    }
}

// 兼容现有独立计算器调用；实现只由第4版核心持有。
public sealed class Lanshi_Hepai_Check {
    public Lanshi_Hepai_Check(bool debug = false) { }
    public Tuple<int, List<string>> HepaiCheck(List<int> hand, List<string> melds, List<string> way, int win) => GBhepaiLanshi.HepaiCheck(hand, melds, way, win);
}
