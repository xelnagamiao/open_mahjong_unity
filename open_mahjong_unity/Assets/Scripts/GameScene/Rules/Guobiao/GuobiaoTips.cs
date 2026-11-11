using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

/// <summary>国标听牌与和牌张提示：按子规则选番表，分别判定点和与自摸资格。</summary>
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
        if (GuobiaoGameState.UsesAsyncLocalTips(q.SubRule)) return FourJokersTips.Waiting(q);
        List<string> melds = q.Melds ?? NoMelds;
        // A drawn hand is not a waiting hand. Kong melds still count as one completed set here.
        if (q.Hand.Count + melds.Count * 3 != 13) return new HashSet<int>();
        string key = q.SubRule + "|" + HandKey(q.Hand, melds);
        if (WaitingCache.TryGetValue(key, out int[] cached)) return new HashSet<int>(cached);
        var hand = q.Hand.ConvertAll(WashizuTile.Face);
        HashSet<int> waiting = q.SubRule == "guobiao/lanshi"
            ? GBhepaiLanshi.TingpaiCheck(hand, melds)
            : GBtingpai.TingpaiCheck(hand, melds, false);
        if (q.SubRule == GuobiaoGameState.SanmaSubRule) waiting.RemoveWhere(tile => tile >= 12 && tile <= 18);
        var snapshot = new int[waiting.Count];
        waiting.CopyTo(snapshot);
        Remember(WaitingCache, WaitingKeys, key, snapshot);
        return waiting;
    }

    public static WaitTileHint Describe(WaitHintQuery q) {
        if (GuobiaoGameState.UsesAsyncLocalTips(q.SubRule)) return FourJokersTips.Describe(q);
        string key = HintKey(q);
        if (HintCache.TryGetValue(key, out HintSnapshot cached)) return cached.Copy();
        WaitTileHint hint;
        using (DescribeMarker.Auto()) hint = CalculateHint(q);
        Remember(HintCache, HintKeys, key, new HintSnapshot(hint));
        return hint;
    }

    public static bool TryDescribeCached(WaitHintQuery q, out WaitTileHint hint) {
        if (GuobiaoGameState.UsesAsyncLocalTips(q.SubRule)) return FourJokersTips.TryDescribeCached(q, out hint);
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
            + q.JinluoMode + "|" + q.JinluoWeather + "|" + q.SelfIndex + "|" + q.CurrentRound + "|"
            + q.GuobiaoTiming?.CacheKey + "|"
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
        if (q.SubRule == GuobiaoGameState.SanmaSubRule && q.HepaiTile >= 12 && q.HepaiTile <= 18) return WaitTileHint.None("不在牌组内");
        // The calculators append derived wind conditions. Keep the caller's query and cache key stable.
        var ronWay = new List<string>(q.MergedWay);
        var zimoWay = q.BuildZimoWay();
        q.GuobiaoTiming?.AddTo(ronWay, q.HepaiTile, false);
        q.GuobiaoTiming?.AddTo(zimoWay, q.HepaiTile, true);
        bool jinluo = q.SubRule == "guobiao/standard" && q.JinluoMode;
        int seatWind = 41 + q.SelfIndex, roundWind = 41 + (Math.Max(1, q.CurrentRound) - 1) / 4;
        Tuple<int, List<string>> CheckWay(List<string> way) => jinluo
            ? JinluoRules.Check(q.HandWithWin, q.Melds ?? NoMelds, way, q.HepaiTile, q.JinluoWeather, seatWind, roundWind)
            : Check(q.SubRule, q.HandWithWin, q.Melds, way, q.HepaiTile);
        bool Allowed(Tuple<int, List<string>> result) => jinluo
            ? JinluoRules.IsAllowed(result, q.HandWithWin, q.Melds ?? NoMelds, q.JinluoWeather, seatWind, roundWind)
            : result.Item1 - q.HuapaiCount >= q.HepaiLimit;
        var ron = CheckWay(ronWay);
        var zimo = CheckWay(zimoWay);
        bool canZimo = Allowed(zimo);
        string Opening(Tuple<int, List<string>> result) => result.Item2.Find(fan => fan == "天和" || fan == "地和" || fan == "人和");
        string ronOpening = Opening(ron), zimoOpening = Opening(zimo);
        string NotAllowed() {
            if (!jinluo) return "未起和";
            // 资格筛选器可能剔除所有拆分并返回空番表；普通计番只用来解释拒绝原因，不能决定金锣资格。
            var ordinary = Check(q.SubRule, q.HandWithWin, q.Melds, new List<string>(ronWay), q.HepaiTile);
            return ordinary.Item2.Contains("七对") ? "天气不符" : "未达金锣";
        }
        // 现手真实摸入牌只展示此刻的自摸结果，不把点和分数与天/人和番数混为一行。
        if (q.GuobiaoTiming?.DrawTile > 0 && WashizuTile.Face(q.GuobiaoTiming.DrawTile) == WashizuTile.Face(q.HepaiTile))
            return canZimo ? WaitTileHint.TsumoOnly($"{zimoOpening}{zimo.Item1}番") : WaitTileHint.None(NotAllowed());
        if (Allowed(ron)) {
            return canZimo ? WaitTileHint.Ron($"{ronOpening}{ron.Item1}番")
                : WaitTileHint.RonOnly(string.IsNullOrEmpty(ronOpening) ? "仅点和" : $"{ronOpening}{ron.Item1}番");
        }
        return canZimo
            ? WaitTileHint.TsumoOnly(string.IsNullOrEmpty(zimoOpening) ? "仅自摸" : $"{zimoOpening}{zimo.Item1}番")
            : WaitTileHint.None(NotAllowed());
    }

    private static Tuple<int, List<string>> Check(string subRule, List<int> hand, List<string> melds, List<string> way, int tile) {
        hand = hand.ConvertAll(WashizuTile.Face);
        tile = WashizuTile.Face(tile);
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
