using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

internal sealed class ShangraoLocal {
    private static readonly int[] Tiles = LocalJokerHand.Tiles;
    private static readonly int[] Honors = Enumerable.Range(41, 7).ToArray();
    private static readonly Dictionary<int, int> Index = Tiles.Select((t, i) => (t, i)).ToDictionary(p => p.t, p => p.i);
    private static readonly int[][] Sequences = Tiles.Where(t => t < 40 && t % 10 <= 7).Select(t => new[] {t, t + 1, t + 2}).ToArray();
    private static readonly int[][] Spaced = Enumerable.Range(0, 512).Select(mask => Enumerable.Range(1, 9).Where(n => (mask & (1 << (n - 1))) != 0).ToArray())
        .Where(a => a.Length <= 3 && a.Skip(1).Zip(a, (b, c) => b - c >= 3).All(ok => ok)).ToArray();
    private readonly List<string> melds;
    private readonly JObject context, config;
    private readonly int indicator, bao;
    private readonly Dictionary<string, bool> memo = new Dictionary<string, bool>();

    internal ShangraoLocal(List<string> melds, JObject context) {
        this.melds = melds ?? new List<string>();
        this.context = context ?? new JObject();
        config = this.context["config"] as JObject ?? new JObject();
        indicator = (int?)this.context["indicator"] ?? 0;
        bao = Bao(indicator);
    }
    internal static int Bao(int indicator) {
        if (!Index.ContainsKey(indicator)) return 0;
        if (indicator < 40) return indicator / 10 * 10 + indicator % 10 % 9 + 1;
        int[] cycle = indicator < 45 ? new[] {41, 42, 43, 44} : new[] {45, 47, 46};
        return cycle[(Array.IndexOf(cycle, indicator) + 1) % cycle.Length];
    }
    private bool Option(string key) => (bool?)config[key] ?? false;
    private static bool Terminal(int tile) => tile >= 41 || tile % 10 == 1 || tile % 10 == 9;
    private static int[] Meld(string code) {
        if (code == null || code.Length != 3 || !"skgG".Contains(code[0]) || !int.TryParse(code.Substring(1), out int tile) || !Index.ContainsKey(tile)) return null;
        if (code[0] != 's') return Enumerable.Repeat(tile, "gG".Contains(code[0]) ? 4 : 3).ToArray();
        return tile < 40 && tile % 10 >= 2 && tile % 10 <= 8 ? new[] {tile - 1, tile, tile + 1} : null;
    }
    private bool Valid(List<int> hand, bool complete = true) {
        if (bao == 0 || hand == null || melds.Count > 4 || hand.Count + melds.Count * 3 != (complete ? 14 : 13)) return false;
        var physical = new List<int>(hand);
        foreach (string code in melds) { var group = Meld(code); if (group == null) return false; physical.AddRange(group); }
        return physical.GroupBy(t => t).All(g => Index.ContainsKey(g.Key) && g.Count() <= (g.Key == indicator ? 3 : 4));
    }
    private bool Groups(int[] counts, int wild, int count, bool triplets) {
        if (wild < 0 || count < 0 || counts.Sum() + wild != count * 3) return false;
        int first = Array.FindIndex(counts, n => n > 0);
        if (first < 0) return true;
        string key = string.Concat(counts.Select(n => (char)('0' + n))) + "/" + wild + "/" + count + "/" + triplets;
        if (memo.TryGetValue(key, out bool known)) return known;
        int[] rest = (int[])counts.Clone(); int used = Math.Min(3, rest[first]); rest[first] -= used;
        bool result = Groups(rest, wild - (3 - used), count - 1, triplets);
        if (!result && !triplets) foreach (var sequence in Sequences.Where(s => s.Contains(Tiles[first]))) {
            rest = (int[])counts.Clone(); int need = 0;
            foreach (int tile in sequence) { int i = Index[tile]; if (rest[i] > 0) rest[i]--; else need++; }
            if (Groups(rest, wild - need, count - 1, false)) { result = true; break; }
        }
        if (memo.Count >= 50000) memo.Clear();
        memo[key] = result; return result;
    }
    private int[] Pairs(List<int> hand, bool triplets = false) {
        if (triplets && melds.Any(m => m[0] == 's')) return Array.Empty<int>();
        int wild = hand.Count(t => t == bao);
        int[] counts = Tiles.Select(t => t == bao ? 0 : hand.Count(x => x == t)).ToArray();
        var result = new HashSet<int>();
        if (wild >= 2 && Groups(counts, wild - 2, 4 - melds.Count, triplets)) result.Add(2);
        for (int i = 0; i < counts.Length; i++) {
            int used = wild > 0 ? 1 : 2;
            if (counts[i] < used) continue;
            int[] rest = (int[])counts.Clone(); rest[i] -= used;
            if (Groups(rest, wild > 0 ? wild - 1 : 0, 4 - melds.Count, triplets)) result.Add(wild > 0 ? 1 : 0);
        }
        return result.ToArray();
    }
    private bool Lan(List<int> hand, bool allowBao) {
        if (melds.Count > 0) return false;
        int[] rest = hand.Where(t => !allowBao || t != bao).ToArray();
        if (rest.Distinct().Count() != rest.Length) return false;
        int capacity = 7;
        foreach (int suit in new[] {1, 2, 3}) {
            int[] ranks = rest.Where(t => t / 10 == suit).Select(t => t % 10).ToArray();
            int[] sizes = Spaced.Where(s => ranks.All(s.Contains)).Select(s => s.Length).ToArray();
            if (sizes.Length == 0) return false;
            capacity += sizes.Max();
        }
        return capacity >= 14;
    }
    private bool Stars(IEnumerable<int> hand, bool allowBao, bool flown) {
        int[] physical = hand.Concat(melds.SelectMany(Meld)).ToArray();
        var natural = new HashSet<int>(physical.Where(t => !allowBao || t != bao));
        var missing = new HashSet<int>(Honors.Where(t => !natural.Contains(t)));
        if (flown && Honors.Contains(bao) && !physical.Contains(bao)) missing.Remove(bao);
        return missing.Count <= (allowBao ? hand.Count(t => t == bao) : 0);
    }
    private bool Seven(List<int> hand, int win, bool opening) {
        if (melds.Count > 0) return false;
        var rest = new List<int>(hand);
        if (!opening && rest.Contains(bao)) {
            rest.Remove(win);
            if (!rest.Remove(bao)) return false;
        }
        int wild = rest.Count(t => t == bao), odd = rest.Where(t => t != bao).GroupBy(t => t).Sum(g => g.Count() % 2);
        return odd <= wild && (wild - odd) % 2 == 0;
    }
    internal JObject Score(List<int> hand, int win, int? cut = null, bool opening = false, bool afterKong = false) {
        if (!Valid(hand) || !hand.Contains(win)) return null;
        bool legacy = (string)config["rule_version"] == "shangrao-v1";
        int wild = hand.Count(t => t == bao);
        bool firstBao = !opening && win == bao && wild == 1;
        int[] pairs = firstBao ? Array.Empty<int>() : Pairs(hand), triplets = firstBao ? Array.Empty<int>() : Pairs(hand, true);
        bool seven = !firstBao && Seven(hand, win, opening);
        bool luxury = seven && hand.Where(t => t != bao).GroupBy(t => t).Any(g => g.Count() == 4);
        bool allowBao = Option("allow_special_bao"), nine = hand.All(t => Terminal(t) || allowBao && t == bao) && melds.SelectMany(Meld).All(Terminal);
        bool flown = (bool?)context["flying_seven_stars"] ?? false;
        if (cut == bao && Honors.Contains(bao)) { var before = new List<int>(hand); before.Remove(win); before.Add(cut.Value); flown |= !legacy || Stars(before, false, false); }
        bool stars = Stars(hand, legacy && allowBao, Option("flying_seven_stars") && flown);
        var candidates = new List<(string name, int value, string shape, int[] pairs)>();
        if (pairs.Length > 0) candidates.Add((wild > 0 ? "钓头" : "平和", 1, "standard", pairs));
        if (triplets.Length > 0) candidates.Add((wild > 0 ? "碰碰和" : "清碰", wild > 0 ? 2 : 4, "triplets", triplets));
        int sevenPairBao = wild >= 2 && hand.Where(t => t != bao).GroupBy(t => t).Sum(g => g.Count() % 2) <= wild - 2 ? 2 : Math.Min(1, wild);
        if (seven) candidates.Add(((luxury ? "豪华" : "") + (wild > 0 ? "七对" : "硬七对"), (wild > 0 ? 2 : 4) * (luxury ? 2 : 1), "seven", new[] {sevenPairBao}));
        if (nine) {
            candidates.Add((stars ? "七星九幺" : "九幺", stars ? 2 : 1, "nine", Array.Empty<int>()));
            if (seven && wild == 0) candidates.Add(("九幺硬七", 8, "seven", Array.Empty<int>()));
            if (triplets.Length > 0 && wild == 0) candidates.Add(("九幺清碰", 8, "triplets", triplets));
            if (!legacy && allowBao && wild > 0 && !Terminal(bao) && Terminal(win)) {
                if (seven) candidates.Add(("九幺七对", 4, "seven", new[] {sevenPairBao}));
                if (triplets.Length > 0) candidates.Add(("九幺碰碰和", 4, "triplets", triplets));
            }
        }
        if (Lan(hand, legacy && allowBao)) candidates.Add((stars ? "七星十三烂" : "十三烂", stars ? 4 : 2, "lan", Array.Empty<int>()));
        int[] natural = hand.Where(t => t != bao).Concat(melds.SelectMany(Meld)).ToArray(), suits = natural.Select(t => t / 10).Distinct().ToArray();
        bool qing = Option("qingyise") && suits.Length == 1 && suits[0] >= 1 && suits[0] <= 3 && (wild == 0 || suits.Contains(win / 10));
        bool zi = Option("ziyise") && nine && natural.All(Honors.Contains) && (legacy || wild == 0 || Honors.Contains(win));
        if (!legacy) {
            if (qing) foreach (var candidate in candidates.ToArray()) {
                if (candidate.shape == "nine" || candidate.shape == "lan") continue;
                string label = candidate.shape == "standard" ? (wild > 0 ? "清一色" : "无宝清一色") : "清一色" + candidate.name;
                candidates.Add((label, candidate.shape == "standard" && wild == 0 ? 4 : candidate.value * 2, candidate.shape, candidate.pairs));
            }
            if (zi) {
                candidates.Insert(0, ("字一色", 2, "nine", Array.Empty<int>()));
                if (wild == 0 || Honors.Contains(win)) {
                    if (seven) candidates.Add((wild > 0 ? "字一色七对" : "字一色硬七对", wild > 0 ? 8 : 16, "seven", new[] {sevenPairBao}));
                    if (triplets.Length > 0) candidates.Add((wild > 0 ? "字一色碰碰和" : "字一色清碰", wild > 0 ? 8 : 16, "triplets", triplets));
                }
            }
        }
        int flying = ((int?)context["flying_bao"] ?? 0) + (cut == bao ? 1 : 0);
        if (flying < 0 || flying > 4) return null;
        JObject best = null;
        foreach (var candidate in candidates) {
            var multipliers = new JObject();
            if (legacy && qing && candidate.shape != "nine" && candidate.shape != "lan") multipliers["清一色"] = 2;
            if (legacy && zi) multipliers["字一色"] = 2;
            if (melds.Count == 0 && candidate.shape != "seven" && candidate.shape != "lan") multipliers["门清"] = 2;
            if (melds.Count == 4) multipliers["单钓"] = 2;
            if (afterKong) multipliers["杠上开花"] = 2;
            if (candidate.pairs.Length > 0 && wild > 0 && win == bao) multipliers["宝钓宝"] = 2;
            if (win != bao && candidate.pairs.Contains(2)) multipliers["宝还原"] = 2;
            if (flying > 0) multipliers["飞宝"] = 1 << flying;
            int points = candidate.value;
            foreach (var multiplier in multipliers.Properties()) points *= (int)multiplier.Value;
            if (best != null && ((int)best["points"] > points || (int)best["points"] == points && (int)best["base"] >= candidate.value)) continue;
            best = new JObject { ["points"] = points, ["base"] = candidate.value, ["pattern"] = candidate.name, ["shape"] = candidate.shape,
                ["fan_values"] = new JObject {[candidate.name] = candidate.value}, ["multipliers"] = multipliers,
                ["fan_names"] = new JArray(new[] {candidate.name}.Concat(multipliers.Properties().Select(p => p.Name))) };
        }
        return best;
    }
    internal int[] Waiting(List<int> hand) => !Valid(hand, false) ? Array.Empty<int>() : Tiles.Where(t => Score(hand.Concat(new[] {t}).ToList(), t, opening:true) != null).ToArray();
    internal JObject Describe(List<int> hand, int win, int? cut = null) {
        var score = Score(hand, win, cut);
        bool blocked = (bool?)context["blocked"] ?? false;
        int points = score == null ? 0 : (int)score["points"];
        return new JObject { ["tile"] = win, ["points"] = blocked ? 0 : points,
            ["kind"] = blocked || points == 0 ? "wuyi" : "zimo", ["label"] = blocked || points == 0 ? "未满足" : $"仅自摸\n{points}分" };
    }
}
