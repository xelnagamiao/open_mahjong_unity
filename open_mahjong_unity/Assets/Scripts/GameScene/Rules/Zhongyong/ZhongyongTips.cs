using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>中庸 v3.3 静态和牌分数；与服务端 scoring.py 同口径，不预加海底、抢杠等事件分。</summary>
internal static class ZhongyongTips {
    private sealed class Meld {
        public char Kind;
        public int Head;
        public bool Declared, Concealed;
        public bool Pung => Kind == 'k' || Kind == 'g';
        public IEnumerable<int> Tiles => Kind == 's' ? Enumerable.Range(Head, 3)
            : Enumerable.Repeat(Head, Kind == 'g' ? 4 : Kind == 'q' ? 2 : 3);
    }
    private static bool Suited(int t) => t / 10 >= 1 && t / 10 <= 3 && t % 10 >= 1 && t % 10 <= 9;
    private static bool Honor(int t) => t >= 41 && t <= 47;
    private static bool Terminal(int t) => Suited(t) && (t % 10 == 1 || t % 10 == 9);
    private static bool Dragon(int t) => t >= 45 && t <= 47;
    private static bool Wind(int t) => t >= 41 && t <= 44;

    public static WaitTileHint Describe(WaitHintQuery q) {
        int points = Points(q.HandWithWin, q.Melds, q.HepaiTile, 41 + q.SelfIndex);
        if (points == 0) return WaitTileHint.None("无和牌");
        int tsumo = Points(q.HandWithWin, q.Melds, q.HepaiTile, 41 + q.SelfIndex, true);
        return WaitTileHint.Ron($"{points}分" + (tsumo != points ? $"\n自摸{tsumo}分" : ""));
    }

    internal static int Points(List<int> hand, List<string> codes, int win, int seatWind, bool selfDraw = false) {
        if (hand == null) return 0;
        var declared = new List<Meld>();
        foreach (string code in codes ?? new List<string>()) {
            if (string.IsNullOrEmpty(code) || code.Length < 2 || !int.TryParse(code.Substring(1), out int tile)) return 0;
            char kind = char.ToLowerInvariant(code[0]);
            if (kind != 's' && kind != 'k' && kind != 'g') return 0;
            if (kind == 's' && (!Suited(tile) || tile % 10 > 7)) return 0;
            declared.Add(new Meld { Kind = kind, Head = tile, Declared = true, Concealed = char.IsUpper(code[0]) });
        }
        var physical = hand.Concat(declared.SelectMany(m => m.Tiles)).ToList();
        if (physical.Any(t => !Suited(t) && !Honor(t)) || physical.GroupBy(t => t).Any(g => g.Count() > 4)) return 0;
        int best = 0;
        if (declared.Count == 0 && hand.Count == 14) {
            if (hand.GroupBy(t => t).Sum(g => g.Count() / 2) == 7)
                best = Score(physical, null, 0, win, seatWind, hand, "seven_pairs", selfDraw);
            if (hand.All(t => Terminal(t) || Honor(t)) && hand.Distinct().Count() == 13)
                best = Math.Max(best, Score(physical, null, 0, win, seatWind, hand, "thirteen_orphans", selfDraw));
        }
        int needed = 4 - declared.Count;
        if (needed < 0 || hand.Count != needed * 3 + 2) return best;
        var counts = hand.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        foreach (int pair in counts.Keys.ToArray()) {
            if (counts[pair] < 2) continue;
            counts[pair] -= 2;
            Search(counts, needed, new List<Meld>(declared), melds =>
                best = Math.Max(best, Score(physical, melds, pair, win, seatWind, hand, null, selfDraw)));
            counts[pair] += 2;
        }
        return best;
    }

    private static void Search(Dictionary<int, int> counts, int needed, List<Meld> melds, Action<List<Meld>> accept) {
        if (needed == 0) { if (counts.Values.All(n => n == 0)) accept(melds); return; }
        int tile = counts.Where(p => p.Value > 0).Select(p => p.Key).DefaultIfEmpty(0).Min();
        if (tile == 0) return;
        if (counts[tile] >= 3) {
            counts[tile] -= 3;
            melds.Add(new Meld { Kind = 'k', Head = tile, Concealed = true });
            Search(counts, needed - 1, melds, accept);
            melds.RemoveAt(melds.Count - 1); counts[tile] += 3;
        }
        if (Suited(tile) && tile % 10 <= 7 && counts.TryGetValue(tile + 1, out int a) && a > 0
            && counts.TryGetValue(tile + 2, out int b) && b > 0) {
            counts[tile]--; counts[tile + 1]--; counts[tile + 2]--;
            melds.Add(new Meld { Kind = 's', Head = tile, Concealed = true });
            Search(counts, needed - 1, melds, accept);
            melds.RemoveAt(melds.Count - 1);
            counts[tile]++; counts[tile + 1]++; counts[tile + 2]++;
        }
    }

    private static int Score(List<int> tiles, List<Meld> melds, int pair, int win, int seatWind, List<int> hand, string special, bool selfDraw) {
        var series = new Dictionary<string, int>();
        void Add(string group, int value) { if (!series.TryGetValue(group, out int old) || value > old) series[group] = value; }
        if (tiles.All(t => Suited(t) && !Terminal(t))) Add("1.3", 5);
        bool honors = tiles.Any(Honor);
        int suits = tiles.Where(Suited).Select(t => t / 10).Distinct().Count();
        if (suits == 1) Add("2.1", honors ? 40 : 80);
        if (suits == 0) Add("3.4", 320);
        if (tiles.All(Terminal)) Add("8.1", 400);
        else if (special != "thirteen_orphans" && tiles.All(t => Terminal(t) || Honor(t))) Add("8.1", 100);
        if (special != null) Add("10", special == "seven_pairs" ? 30 : 160);
        int valueHonors = 0;
        if (melds != null) {
            var sequences = melds.Where(m => m.Kind == 's').ToList();
            var pungs = melds.Where(m => m.Pung).ToList();
            if (melds.All(m => !m.Declared || (m.Kind == 'g' && m.Concealed))) Add("1.2", 5);
            if (sequences.Count == 4) Add("1.1", 5);
            if (pungs.Count == 4) Add("4.1", 30);
            valueHonors = 10 * pungs.Count(m => Dragon(m.Head) || m.Head == seatWind);
            int dragons = pungs.Count(m => Dragon(m.Head)), winds = pungs.Count(m => Wind(m.Head));
            if (dragons == 3) Add("3.2", 130);
            else if (dragons == 2 && Dragon(pair)) Add("3.2", 40);
            if (winds == 4) Add("3.3", 400);
            else if (winds == 3) Add("3.3", Wind(pair) ? 320 : 120);
            else if (winds == 2 && Wind(pair)) Add("3.3", 30);
            int concealed = pungs.Count(m => m.Concealed);
            if (!selfDraw && win != pair && !sequences.Any(m => !m.Declared && m.Tiles.Contains(win))
                && pungs.Any(m => !m.Declared && m.Head == win)) concealed--;
            if (concealed >= 2) Add("4.2", new[] { 0, 0, 5, 30, 125 }[concealed]);
            int kongs = melds.Count(m => m.Kind == 'g');
            if (kongs > 0) Add("4.3", new[] { 0, 5, 20, 120, 480 }[kongs]);
            var identical = sequences.GroupBy(m => m.Head).Select(g => g.Count()).ToList();
            int maximum = identical.DefaultIfEmpty(0).Max();
            if (maximum >= 2) Add("5.1", maximum == 4 ? 480 : maximum == 3 ? 120 : identical.Count(n => n == 2) == 2 ? 60 : 10);
            var seqHeads = new HashSet<int>(sequences.Select(m => m.Head));
            var pungHeads = new HashSet<int>(pungs.Select(m => m.Head));
            for (int rank = 1; rank <= 9; rank++) {
                if (Enumerable.Range(1, 3).All(s => seqHeads.Contains(s * 10 + rank))) Add("6.1", 35);
                var matches = Enumerable.Range(1, 3).Where(s => pungHeads.Contains(s * 10 + rank)).ToList();
                if (matches.Count == 3) Add("6.2", 120);
                else if (matches.Count == 2 && Suited(pair) && pair % 10 == rank && !matches.Contains(pair / 10)) Add("6.2", 30);
            }
            for (int suit = 1; suit <= 3; suit++) {
                if (new[] { 1, 4, 7 }.All(r => seqHeads.Contains(suit * 10 + r))) Add("7.1", 40);
                if (Enumerable.Range(1, 6).Any(r => Enumerable.Range(r, 4).All(t => pungHeads.Contains(suit * 10 + t)))) Add("7.2", 200);
                else if (Enumerable.Range(1, 7).Any(r => Enumerable.Range(r, 3).All(t => pungHeads.Contains(suit * 10 + t)))) Add("7.2", 100);
            }
            var units = melds.Select(m => m.Tiles).Concat(new[] { new[] { pair }.AsEnumerable() });
            if (units.All(u => u.Any(t => Terminal(t) || Honor(t)))) Add("8.1", 40);
            if (units.All(u => u.Any(Terminal))) Add("8.1", 50);
            var before = new List<int>(hand); before.Remove(win);
            if (melds.All(m => !m.Declared) && Suited(win) && before.OrderBy(t => t).SequenceEqual(
                new[] { 1, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9 }.Select(n => win / 10 * 10 + n))) Add("2.2", 480);
        }
        int listed = series.Values.Where(v => v >= 320).DefaultIfEmpty(0).Max();
        return listed > 0 ? listed : Math.Min(320, Math.Max(1, series.Values.Sum() + valueHonors));
    }
}
