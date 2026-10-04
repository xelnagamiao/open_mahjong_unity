using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

/// <summary>国标听牌与和牌张提示：按子规则选番表，未起和时改按自摸重算。</summary>
internal static class GuobiaoTips {
    private const int CacheCapacity = 128;
    private static readonly Dictionary<string, int[]> WaitingCache = new Dictionary<string, int[]>();
    private static readonly Queue<string> WaitingKeys = new Queue<string>();
    private static readonly Dictionary<string, HintSnapshot> HintCache = new Dictionary<string, HintSnapshot>();
    private static readonly Queue<string> HintKeys = new Queue<string>();
    private static readonly List<string> NoMelds = new List<string>();
    private static readonly ProfilerMarker WarmupMarker = new ProfilerMarker("GuobiaoTips.Warmup");
    private static readonly ProfilerMarker DescribeMarker = new ProfilerMarker("GuobiaoTips.CalculateHint");

    // WaitTileHint / HashSet are mutable API results. Cache snapshots rather than caller-owned objects.
    private readonly struct HintSnapshot {
        private readonly string label;
        private readonly string kind;
        public HintSnapshot(WaitTileHint hint) { label = hint.Label; kind = hint.Kind; }
        public WaitTileHint Copy() => new WaitTileHint { Label = label, Kind = kind };
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCaches() {
        WaitingCache.Clear();
        WaitingKeys.Clear();
        HintCache.Clear();
        HintKeys.Clear();
    }

    // Mono's first scoring call can JIT dozens of fan checks. Pay that cost before entering a table.
    // No Unity objects or live game state are read by these Guobiao calculators.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Warmup() {
        using (WarmupMarker.Auto()) {
            var hand = new List<int> { 11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 45 };
            Tingpai(new TingpaiQuery { Hand = hand, Melds = NoMelds });
            hand.Add(45);
            var way = new List<string> { "场风东", "自风东", "和单张" };
            var ronWay = new List<string>(way) { "点和" };
            foreach (string subRule in new[] { "guobiao/standard", "guobiao/xiaolin", "guobiao/kshen", "guobiao/lanshi" }) {
                var query = new WaitHintQuery {
                    HandWithWin = hand, Melds = NoMelds, HepaiTile = 45,
                    SubRule = subRule, HepaiLimit = 8, WayToHepai = way,
                    SingleTileWay = NoMelds, MergedWay = ronWay,
                };
                Describe(query);
                query.HepaiLimit = int.MaxValue;
                Describe(query); // Also initialize the self-draw / below-limit path.
            }
        }
    }

    public static HashSet<int> Tingpai(TingpaiQuery q) {
        List<string> melds = q.Melds ?? NoMelds;
        // A drawn hand is not a waiting hand. Kong melds still count as one completed set here.
        if (q.Hand.Count + melds.Count * 3 != 13) return new HashSet<int>();
        string key = q.SubRule + "|" + HandKey(q.Hand, melds);
        if (WaitingCache.TryGetValue(key, out int[] cached)) return new HashSet<int>(cached);
        HashSet<int> waiting = q.SubRule == "guobiao/lanshi"
            ? GBhepaiLanshi.TingpaiCheck(q.Hand, melds)
            : GBtingpai.TingpaiCheck(q.Hand, melds, false);
        var snapshot = new int[waiting.Count];
        waiting.CopyTo(snapshot);
        Remember(WaitingCache, WaitingKeys, key, snapshot);
        return waiting;
    }

    public static WaitTileHint Describe(WaitHintQuery q) {
        string key = HintKey(q);
        if (HintCache.TryGetValue(key, out HintSnapshot cached)) return cached.Copy();
        WaitTileHint hint;
        using (DescribeMarker.Auto()) hint = CalculateHint(q);
        Remember(HintCache, HintKeys, key, new HintSnapshot(hint));
        return hint;
    }

    public static bool TryDescribeCached(WaitHintQuery q, out WaitTileHint hint) {
        if (HintCache.TryGetValue(HintKey(q), out HintSnapshot cached)) {
            hint = cached.Copy();
            return true;
        }
        hint = null;
        return false;
    }

    private static string HintKey(WaitHintQuery q) {
        // Include every input used by both ron and self-draw checks, including table-dependent fans.
        return q.SubRule + "|" + HandKey(q.HandWithWin, q.Melds) + "|"
            + q.HepaiTile + "|" + q.HuapaiCount + "|" + q.HepaiLimit + "|"
            + string.Join(",", q.MergedWay) + "|" + string.Join(",", q.WayToHepai) + "|"
            + string.Join(",", q.SingleTileWay);
    }

    private static string HandKey(List<int> hand, List<string> melds) {
        return string.Join(",", hand) + "|" + string.Join(",", melds ?? NoMelds);
    }

    private static void Remember<T>(Dictionary<string, T> cache, Queue<string> keys, string key, T value) {
        if (cache.Count == CacheCapacity) cache.Remove(keys.Dequeue());
        cache.Add(key, value);
        keys.Enqueue(key);
    }

    private static WaitTileHint CalculateHint(WaitHintQuery q) {
        // The calculators append derived wind conditions. Keep the caller's query and cache key stable.
        var ronWay = new List<string>(q.MergedWay);
        Tuple<int, List<string>> ron = Check(q.SubRule, q.HandWithWin, q.Melds, ronWay, q.HepaiTile);
        if (ron.Item1 - q.HuapaiCount >= q.HepaiLimit) {
            return WaitTileHint.Ron($"{ron.Item1}番");
        }
        Tuple<int, List<string>> zimo = Check(q.SubRule, q.HandWithWin, q.Melds, q.BuildZimoWay(), q.HepaiTile);
        return zimo.Item1 - q.HuapaiCount >= q.HepaiLimit
            ? WaitTileHint.TsumoOnly("仅自摸")
            : WaitTileHint.None("未起和");
    }

    private static Tuple<int, List<string>> Check(string subRule, List<int> hand, List<string> melds, List<string> way, int tile) {
        switch (subRule) {
            case "guobiao/xiaolin": {
                var r = GBhepaiXiaolin.HepaiCheck(hand, melds, way, tile, false);
                return GBhepaiXiaolin.FilterZeroValueFans(r.Item1, r.Item2);
            }
            case "guobiao/kshen":
                return GBhepaiKshen.HepaiCheck(hand, melds, way, tile, false);
            case "guobiao/lanshi":
                return GBhepaiLanshi.HepaiCheck(hand, melds, way, tile, false);
            default:
                return GBhepai.HepaiCheck(hand, melds, way, tile, false);
        }
    }
}
