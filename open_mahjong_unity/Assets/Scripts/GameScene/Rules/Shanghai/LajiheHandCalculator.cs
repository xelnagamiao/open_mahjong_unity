using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>垃圾和提示：七对可含四张同牌，全风向不限结构，花分按正文阶梯。</summary>
internal static class LajiheHandCalculator {
    public const string SubRule = "shanghai/lajihe";
    private static readonly int[] Tiles = Enumerable.Range(1, 3)
        .SelectMany(s => Enumerable.Range(1, 9).Select(r => s * 10 + r))
        .Concat(Enumerable.Range(41, 7)).ToArray();

    public static int FlowerPoints(int count) => count < 3 ? 0 : count < 5 ? 5 : count < 7 ? 10 : (count - 4) * 5;

    public static HashSet<int> Waits(List<int> hand, List<string> melds) {
        var result = new HashSet<int>();
        melds = melds ?? new List<string>();
        if (hand == null || hand.Count + 3 * melds.Count != 13) return result;
        foreach (int tile in Tiles)
            if (Score(new List<int>(hand) { tile }, melds, 0, true) > 0) result.Add(tile);
        return result;
    }

    public static int Score(List<int> hand, List<string> melds, int flowers, bool selfDraw,
        bool replacement = false, bool robKong = false) =>
        Evaluate(hand, melds, flowers, selfDraw, replacement, robKong).Points;

    private static (int Points, int Fan) Evaluate(List<int> hand, List<string> melds, int flowers, bool selfDraw,
        bool replacement = false, bool robKong = false) {
        melds = melds ?? new List<string>();
        if (hand == null || hand.Count + 3 * melds.Count != 14 || melds.Count > 4) return (0, 0);
        foreach (string code in melds) {
            if (string.IsNullOrEmpty(code) || code.Length < 3 || "skgG".IndexOf(code[0]) < 0
                || !int.TryParse(code.Substring(1), out int tile) || !Tiles.Contains(tile)
                || (code[0] == 's' && (tile >= 40 || tile % 10 < 2 || tile % 10 > 8))) return (0, 0);
        }
        var all = hand.Concat(melds.SelectMany(QinghunpengHandCalculator.MeldTiles)).ToList();
        if (all.Any(t => !Tiles.Contains(t)) || all.GroupBy(t => t).Any(g => g.Count() > 4)) return (0, 0);
        bool honors = all.All(t => t >= 41);
        int suits = all.Where(t => t < 40).Select(t => t / 10).Distinct().Count();
        bool mixed = all.Any(t => t >= 41);
        int best = 0, bestFan = 0, bestBasic = 0;
        void Candidate(int basic, int fan, IEnumerable<string> sets) {
            int physical = flowers;
            foreach (string code in melds.Concat(sets)) {
                if (code[0] == 's') continue;
                physical += int.Parse(code.Substring(1)) >= 41 ? 1 : 0;
                physical += code[0] == 'G' ? 2 : code[0] == 'g' ? 1 : 0;
            }
            if (basic == 5 && physical < 2 && !selfDraw && !robKong) return;
            int extra = (selfDraw || robKong) ? (basic == 5 ? 5 : 10) : 0;
            if (melds.Count == 4) extra += 5;
            if (selfDraw && replacement && !robKong) extra += 5;
            int points = (basic + extra + FlowerPoints(physical)) * (1 << fan);
            int basicPoints = basic * (1 << fan);
            // 与服务端采用同一个择高结果，提示番数不能从总分反推。
            if (points > best || (points == best && basicPoints > bestBasic)) {
                best = points;
                bestFan = fan;
                bestBasic = basicPoints;
            }
        }
        foreach (var sets in QinghunpengHandCalculator.Decompositions(hand, melds)) {
            bool pungs = sets.Concat(melds).All(c => c[0] != 's');
            if (honors) Candidate(20, 2, sets);
            else if (suits == 1 && pungs) Candidate(mixed ? 25 : 20, mixed ? 0 : 1, sets);
            else if (suits == 1) Candidate(mixed ? 20 : 25, 0, sets);
            else Candidate(pungs ? 20 : 5, 0, sets);
        }
        if (melds.Count == 0 && hand.GroupBy(t => t).All(g => g.Count() % 2 == 0)) Candidate(20, 0, Array.Empty<string>());
        if (honors) Candidate(20, 1, hand.GroupBy(t => t).Where(g => g.Count() >= 3).Select(g => "k" + g.Key));
        return (best, bestFan);
    }

    public static WaitTileHint Describe(WaitHintQuery query) {
        bool blocked = query.Record != null ? query.Record.SelfTags?.Contains("lajihe_passed_win") == true
            : (RuleRegistry.ActiveGameState as LajiheGameState)?.PassedWin == true;
        if (blocked) return WaitTileHint.None("0番0分");
        int flowers = query.SelfFlowers?.Count ?? query.HuapaiCount;
        var ron = Evaluate(query.HandWithWin, query.Melds, flowers, false);
        if (ron.Points > 0) return WaitTileHint.Ron($"{ron.Fan}番{ron.Points}分");
        var tsumo = Evaluate(query.HandWithWin, query.Melds, flowers, true);
        return tsumo.Points > 0 ? WaitTileHint.TsumoOnly($"{tsumo.Fan}番{tsumo.Points}分") : WaitTileHint.None("0番0分");
    }
}
