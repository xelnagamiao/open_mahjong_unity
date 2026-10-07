using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 牌谱的广东 MIL 基本听牌：精确消耗鬼牌，闭手逻辑同牌不超过四张。
/// 只计算普通点和/自摸及起和门槛；实战行动资格仍由服务端裁定。
/// </summary>
internal static class GuangdongBaseHints {
    private static readonly int[] Tiles = Enumerable.Range(1, 3)
        .SelectMany(s => Enumerable.Range(1, 9).Select(r => s * 10 + r))
        .Concat(Enumerable.Range(41, 7)).ToArray();
    private static readonly Dictionary<int, int> Index = Tiles.Select((tile, index) => new { tile, index })
        .ToDictionary(item => item.tile, item => item.index);
    private static readonly int[] Orphans = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
    private static readonly HashSet<int> OrphanSet = new HashSet<int>(Orphans);
    private static readonly Dictionary<string, HashSet<int>> waitCache = new Dictionary<string, HashSet<int>>();
    private static readonly Dictionary<string, FanShape> fanCache = new Dictionary<string, FanShape>();
    private sealed class FanShape { internal int ordinary = -1; internal bool fourGhosts; }
    private sealed class Hand {
        internal int[] counts = new int[34], physical = new int[38];
        internal int ghosts, suitMask;
        internal ulong triplets;
        internal bool concealed = true, honors, terminals = true;
    }
    private static bool Ghost(int tile) => tile >= 55 && tile <= 58;
    private static string Key(IEnumerable<int> hand, IEnumerable<string> melds) =>
        string.Join(",", (hand ?? Array.Empty<int>()).OrderBy(t => t)) + "|"
        + string.Join(",", (melds ?? Array.Empty<string>()).OrderBy(t => t, StringComparer.Ordinal));
    internal static void Clear() { waitCache.Clear(); fanCache.Clear(); }

    private static bool Prepare(List<int> hand, List<string> melds, int nominal, out Hand result) {
        result = null;
        int meldCount = melds?.Count ?? 0;
        if (hand == null || meldCount > 4 || hand.Count + 3 * meldCount != nominal) return false;
        var data = new Hand();
        foreach (int tile in hand) {
            if (Ghost(tile)) { data.ghosts++; if (++data.physical[34 + tile - 55] > 1) return false; }
            else {
                if (!Index.TryGetValue(tile, out int index) || ++data.physical[index] > 4) return false;
                data.counts[index]++;
            }
        }
        foreach (string code in melds ?? new List<string>()) {
            if (code == null || code.Length != 3 || (code[0] != 'k' && code[0] != 'g' && code[0] != 'G')
                || code[1] < '0' || code[1] > '9' || code[2] < '0' || code[2] > '9') return false;
            int tile = (code[1] - '0') * 10 + code[2] - '0';
            if (!Index.TryGetValue(tile, out int index)) return false;
            data.physical[index] += code[0] == 'k' ? 3 : 4;
            if (data.physical[index] > 4) return false;
            data.concealed &= code[0] == 'G';
            data.triplets |= 1UL << index;
            if (index < 27) data.suitMask |= 1 << (index / 9);
            else data.honors = true;
            data.terminals &= OrphanSet.Contains(index);
        }
        result = data; return true;
    }

    // 每个花色的 rank DP 用位集记录精确鬼牌成本；不枚举 34^鬼数 的实体替换。
    private sealed class GroupEngine {
        internal readonly int offset;
        private readonly int[] counts;
        private readonly bool sequences;
        private readonly int budget, mask;
        private readonly Dictionary<int, int> resources = new Dictionary<int, int>();
        private readonly Dictionary<int, List<Pattern>> patterns = new Dictionary<int, List<Pattern>>();
        private struct Step { internal int pair, pung, sequence, cost; }
        internal sealed class Pattern {
            internal int[] logical;
            internal ulong triplets;
            internal int pair;
            internal bool sequence;
        }
        internal GroupEngine(int[] natural, int start, int length, int ghostCount) {
            offset = start; counts = natural.Skip(start).Take(length).ToArray();
            sequences = length == 9; budget = ghostCount; mask = (1 << (budget + 1)) - 1;
        }
        private IEnumerable<Step> Steps(int rank, int prev1, int prev2, int pairLeft) {
            int minimum = counts[rank], pending = prev1 + prev2, maximum = Math.Min(minimum + budget, 4);
            for (int pair = 0; pair <= pairLeft; pair++) {
                int room = maximum - pending - 2 * pair;
                for (int pung = 0; pung <= room / 3 && room >= 0; pung++) {
                    int basis = pending + 2 * pair + 3 * pung;
                    int maxSequence = sequences && rank < counts.Length - 2 ? maximum - basis : 0;
                    for (int sequence = 0; sequence <= maxSequence; sequence++) {
                        int cost = basis + sequence - minimum;
                        if (cost >= 0 && cost <= budget)
                            yield return new Step { pair = pair, pung = pung, sequence = sequence, cost = cost };
                    }
                }
            }
        }
        private int Resources(int rank, int prev1, int prev2, int pairLeft) {
            if (rank == counts.Length) return prev1 == 0 && prev2 == 0 && pairLeft == 0 ? 1 : 0;
            int key = (((rank * 5 + prev1) * 5 + prev2) * 2) + pairLeft;
            if (resources.TryGetValue(key, out int value)) return value;
            foreach (var step in Steps(rank, prev1, prev2, pairLeft))
                value |= Resources(rank + 1, step.sequence, prev1, pairLeft - step.pair) << step.cost;
            return resources[key] = value & mask;
        }
        internal int Profile(int pair) => Resources(0, 0, 0, pair);
        internal List<Pattern> Patterns(int pair, int cost) {
            int key = pair * 5 + cost;
            if (patterns.TryGetValue(key, out var found)) return found;
            found = new List<Pattern>();
            var logical = new int[counts.Length];
            void Walk(int rank, int prev1, int prev2, int pairLeft, int remaining,
                      ulong triplets, int pairIndex, bool anySequence) {
                if (rank == counts.Length) {
                    if (remaining == 0 && prev1 == 0 && prev2 == 0 && pairLeft == 0)
                        found.Add(new Pattern { logical = (int[])logical.Clone(), triplets = triplets,
                            pair = pairIndex, sequence = anySequence });
                    return;
                }
                foreach (var step in Steps(rank, prev1, prev2, pairLeft)) {
                    int next = remaining - step.cost;
                    if (next < 0 || (Resources(rank + 1, step.sequence, prev1, pairLeft - step.pair) & (1 << next)) == 0) continue;
                    logical[rank] = counts[rank] + step.cost;
                    Walk(rank + 1, step.sequence, prev1, pairLeft - step.pair, next,
                        triplets | (step.pung > 0 ? 1UL << (offset + rank) : 0),
                        step.pair > 0 ? offset + rank : pairIndex, anySequence || step.sequence > 0);
                }
            }
            if ((Profile(pair) & (1 << cost)) != 0) Walk(0, 0, 0, pair, cost, 0, -1, false);
            return patterns[key] = found;
        }
    }
    private static GroupEngine[] Engines(Hand hand) => new[] {
        new GroupEngine(hand.counts, 0, 9, hand.ghosts), new GroupEngine(hand.counts, 9, 9, hand.ghosts),
        new GroupEngine(hand.counts, 18, 9, hand.ghosts), new GroupEngine(hand.counts, 27, 7, hand.ghosts)
    };
    private static bool Standard(Hand hand, GroupEngine[] groups) {
        var states = new bool[hand.ghosts + 1, 2]; states[0, 0] = true;
        foreach (var group in groups) {
            var next = new bool[hand.ghosts + 1, 2];
            for (int used = 0; used <= hand.ghosts; used++) for (int eye = 0; eye <= 1; eye++) {
                if (!states[used, eye]) continue;
                for (int pair = 0; pair <= 1 - eye; pair++) for (int cost = 0; cost <= hand.ghosts - used; cost++)
                    if ((group.Profile(pair) & (1 << cost)) != 0) next[used + cost, eye + pair] = true;
            }
            states = next;
        }
        return states[hand.ghosts, 1];
    }
    private static int[] PairResources(Hand hand) {
        int mask = (1 << (hand.ghosts + 1)) - 1;
        var bits = new int[35]; bits[34] = 1;
        for (int i = 33; i >= 0; i--) {
            int n = hand.counts[i];
            for (int total = n + n % 2; total <= Math.Min(n + hand.ghosts, 4); total += 2)
                bits[i] |= bits[i + 1] << (total - n);
            bits[i] &= mask;
        }
        return bits;
    }
    private static bool Orphan(Hand hand) {
        if (hand.counts.Where((n, i) => n > 0 && !OrphanSet.Contains(i)).Any()) return false;
        foreach (int pair in Orphans) {
            int cost = 0; bool valid = true;
            foreach (int i in Orphans) {
                int deficit = (i == pair ? 2 : 1) - hand.counts[i];
                if (deficit < 0) { valid = false; break; }
                cost += deficit;
            }
            if (valid && cost == hand.ghosts) return true;
        }
        return false;
    }
    private static bool Complete(Hand hand, bool closed) {
        if (hand.ghosts == 4) return true;
        return Standard(hand, Engines(hand)) || closed &&
            (((PairResources(hand)[0] & (1 << hand.ghosts)) != 0) || Orphan(hand));
    }
    internal static HashSet<int> Waiting(List<int> hand, List<string> melds) {
        string key = Key(hand, melds);
        if (waitCache.TryGetValue(key, out var cached)) return new HashSet<int>(cached);
        var waits = new HashSet<int>();
        if (Prepare(hand, melds, 13, out var data)) {
            foreach (int tile in Tiles.Concat(Enumerable.Range(55, 4))) {
                int i = Ghost(tile) ? 34 + tile - 55 : Index[tile];
                if (data.physical[i] >= (Ghost(tile) ? 1 : 4)) continue;
                var complete = new List<int>(hand) { tile };
                if (Prepare(complete, melds, 14, out var candidate) && Complete(candidate, (melds?.Count ?? 0) == 0)) waits.Add(tile);
            }
        }
        if (waitCache.Count >= 128) waitCache.Clear();
        waitCache[key] = waits; return new HashSet<int>(waits);
    }

    private static int Flush(Hand hand, int[] logical) {
        int suits = hand.suitMask; bool honors = hand.honors;
        for (int i = 0; i < 34; i++) if (logical[i] > 0) {
            if (i < 27) suits |= 1 << (i / 9); else honors = true;
        }
        return suits != 0 && (suits & (suits - 1)) == 0 ? (honors ? 4 : 8) : 0;
    }
    private static bool NineGates(Hand hand, int[] logical, int winningTile) {
        for (int suit = 0; suit < 3; suit++) {
            int extra = -1; bool valid = true;
            for (int i = 0; i < 34; i++) {
                int expected = i / 9 == suit && i < 27 ? (i % 9 == 0 || i % 9 == 8 ? 3 : 1) : 0;
                int difference = logical[i] - expected;
                if (difference == 1 && extra < 0) extra = i;
                else if (difference != 0) { valid = false; break; }
            }
            if (valid && extra >= 0 && (Ghost(winningTile) ? logical[extra] > hand.counts[extra] : Tiles[extra] == winningTile)) return true;
        }
        return false;
    }
    private static int StandardFan(Hand hand, int[] logical, ulong triplets, int pair, bool sequence, bool closed, int winningTile) {
        int concealed = hand.concealed ? 1 : 0, flush = Flush(hand, logical);
        int fan = concealed + flush;
        bool bigWinds = (triplets & (15UL << 27)) == (15UL << 27);
        if (!sequence) {
            bool terminals = hand.terminals && !logical.Where((n, i) => n > 0 && !OrphanSet.Contains(i)).Any();
            fan += terminals ? 12 : (bigWinds ? 0 : 4);
        }
        int dragons = 0, winds = 0;
        for (int i = 31; i <= 33; i++) if ((triplets & (1UL << i)) != 0) dragons++;
        for (int i = 27; i <= 30; i++) if ((triplets & (1UL << i)) != 0) winds++;
        fan += dragons == 3 ? 16 : dragons == 2 && pair >= 31 && pair <= 33 && (triplets & (1UL << pair)) == 0 ? 12 : 0;
        fan += winds == 4 ? 16 : winds == 3 && pair >= 27 && pair <= 30 && (triplets & (1UL << pair)) == 0 ? 12 : 0;
        if (closed && NineGates(hand, logical, winningTile)) fan += 16 - concealed - flush;
        return fan;
    }
    private static FanShape Fans(Hand hand, bool closed, int winningTile) {
        var best = new FanShape { fourGhosts = hand.ghosts == 4 };
        var groups = Engines(hand);
        var logical = new int[34];
        void Combine(int groupIndex, int remaining, int pairLeft, ulong triplets, int pairIndex, bool sequence) {
            if (groupIndex == 4) {
                if (remaining == 0 && pairLeft == 0)
                    best.ordinary = Math.Max(best.ordinary, StandardFan(hand, logical, triplets, pairIndex, sequence, closed, winningTile));
                return;
            }
            var group = groups[groupIndex];
            for (int pair = 0; pair <= pairLeft; pair++) for (int cost = 0; cost <= remaining; cost++) {
                if ((group.Profile(pair) & (1 << cost)) == 0) continue;
                foreach (var pattern in group.Patterns(pair, cost)) {
                    Array.Copy(pattern.logical, 0, logical, group.offset, pattern.logical.Length);
                    Combine(groupIndex + 1, remaining - cost, pairLeft - pair, triplets | pattern.triplets,
                        pattern.pair >= 0 ? pattern.pair : pairIndex, sequence || pattern.sequence);
                }
            }
        }
        if (Standard(hand, groups)) Combine(0, hand.ghosts, 1, hand.triplets, -1, false);
        if (closed) {
            var pairBits = PairResources(hand);
            void Pairs(int index, int remaining) {
                if (index == 34) {
                    if (remaining == 0) best.ordinary = Math.Max(best.ordinary, 6 + Flush(hand, logical));
                    return;
                }
                int n = hand.counts[index];
                for (int total = n + n % 2; total <= Math.Min(n + remaining, 4); total += 2) {
                    int next = remaining - (total - n);
                    if ((pairBits[index + 1] & (1 << next)) == 0) continue;
                    logical[index] = total; Pairs(index + 1, next);
                }
            }
            if ((pairBits[0] & (1 << hand.ghosts)) != 0) Pairs(0, hand.ghosts);
            if (Orphan(hand)) best.ordinary = Math.Max(best.ordinary, 16);
        }
        return best;
    }
    internal static GuangdongWait Evaluate(List<int> complete, List<string> melds, int winningTile, bool minimum, int discardedGhosts) {
        var wait = new GuangdongWait { tile = winningTile, ron_fan = 0, self_draw_fan = 0, ron_score = 0, self_draw_score = 0 };
        if (!Prepare(complete, melds, 14, out var hand) || !complete.Contains(winningTile)
            || discardedGhosts < 0 || discardedGhosts > 4 || hand.ghosts + discardedGhosts > 4) return wait;
        string key = Key(complete, melds) + ":" + winningTile;
        if (!fanCache.TryGetValue(key, out var shapes)) {
            shapes = Fans(hand, (melds?.Count ?? 0) == 0, winningTile);
            if (fanCache.Count >= 512) fanCache.Clear();
            fanCache[key] = shapes;
        }
        int coefficient = (hand.ghosts == 0 ? 2 : 1) * (1 << discardedGhosts);
        int ron = Math.Max(shapes.ordinary, shapes.fourGhosts ? 12 : -1);
        int self = Math.Max(shapes.ordinary >= 0 ? shapes.ordinary + 1 : -1, shapes.fourGhosts ? 12 : -1);
        wait.ron = !Ghost(winningTile) && ron >= 2 && (!minimum || ron * coefficient >= 4);
        wait.self_draw = self >= 2 && (!minimum || self * coefficient >= 4);
        if (wait.ron) { wait.ron_fan = ron; wait.ron_score = ron * coefficient; }
        if (wait.self_draw) { wait.self_draw_fan = self; wait.self_draw_score = self * coefficient; }
        wait.fan = Math.Max(wait.ron_fan.Value, wait.self_draw_fan.Value);
        wait.score = Math.Max(wait.ron_score.Value, wait.self_draw_score.Value);
        return wait;
    }
    internal static WaitTileHint Describe(WaitHintQuery query) {
        var config = query.DetailedConfig ?? query.Record?.DetailedConfig;
        bool minimum = config == null || !config.TryGetValue("require_minimum_score", out var value)
            || !bool.TryParse(value?.ToString(), out bool configured) || configured;
        int discarded = 0;
        if (query.Record?.PlayersByPosition != null && query.Record.PlayersByPosition.TryGetValue("self", out var player))
            discarded = player?.DiscardTiles?.Count(Ghost) ?? 0;
        var wait = Evaluate(query.HandWithWin, query.Melds, query.HepaiTile, minimum, discarded);
        string ron = $"点和{wait.ron_fan}番{wait.ron_score}分", self = $"自摸{wait.self_draw_fan}番{wait.self_draw_score}分";
        if (wait.ron) return WaitTileHint.Ron(wait.self_draw ? ron + "\n" + self : ron);
        return wait.self_draw ? WaitTileHint.TsumoOnly(self) : WaitTileHint.None("未起和");
    }
}
