using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

internal static class SanhuaHand {
    internal sealed class Variant {
        public bool Pure, Pung, Jiang, Eye, Orphans;
        public int PairsFan;
    }
    internal sealed class Price {
        public int Points;
        public bool Qualifies;
        public bool Orphans;
    }
    private const int HintCacheCapacity = 256;
    private static readonly object CacheLock = new object();
    private static readonly Dictionary<string, List<Variant>> Cache = new Dictionary<string, List<Variant>>();
    private static readonly Dictionary<(int skin, int joker), LocalJokerHand.Policy> Policies = new Dictionary<(int, int), LocalJokerHand.Policy>();
    private static readonly Dictionary<string, Task<WaitTileHint>> HintTasks = new Dictionary<string, Task<WaitTileHint>>();
    private static readonly Dictionary<string, WaitTileHint> CompletedHints = new Dictionary<string, WaitTileHint>();
#if !UNITY_WEBGL || UNITY_EDITOR
    private static readonly ConcurrentExclusiveSchedulerPair Scheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 1);
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void ClearCaches() {
        lock (CacheLock) {
            Cache.Clear();
            Policies.Clear();
        }
        HintTasks.Clear();
        CompletedHints.Clear();
    }

    private static LocalJokerHand.Policy GetPolicy(int skin, int joker) {
        var key = (skin, joker);
        lock (CacheLock) {
            if (Policies.TryGetValue(key, out var existing)) return existing;
            var policy = new LocalJokerHand.Policy {
                Jokers = new[] { joker },
                SevenPairs = true,
                ClosedCap = 4,
                Logical = LocalJokerHand.Tiles.Where(t => t != 45 && t != skin).ToArray()
            };
            Policies[key] = policy;
            return policy;
        }
    }

    private static bool HasTooManyCopies(List<int> tiles) {
        for (int i = 0; i < tiles.Count; i++) {
            int tile = tiles[i];
            int count = 1;
            for (int j = i + 1; j < tiles.Count; j++) {
                if (tiles[j] == tile && ++count > 4) return true;
            }
        }
        return false;
    }

    private static bool IsOrphanTile(int t) => Array.IndexOf(LocalJokerHand.Orphans, t) >= 0;

    private static bool IsOrphansHand(List<int> tiles, List<string> melds, int joker) {
        if (melds.Count != 0) return false;
        int plainCount = 0;
        int duplicates = 0;
        for (int i = 0; i < tiles.Count; i++) {
            int tile = tiles[i];
            if (tile == joker) continue;
            if (!IsOrphanTile(tile)) return false;
            plainCount++;
            for (int j = 0; j < i; j++) {
                if (tiles[j] == tile) {
                    duplicates++;
                    break;
                }
            }
            if (duplicates > 1) return false;
        }
        return plainCount >= 0 && duplicates <= 1;
    }

    private static int CountQuads(List<int> tiles) {
        int quads = 0;
        for (int i = 0; i < tiles.Count; i++) {
            int tile = tiles[i];
            bool seenBefore = false;
            for (int j = 0; j < i; j++) {
                if (tiles[j] == tile) { seenBefore = true; break; }
            }
            if (seenBefore) continue;
            int count = 1;
            for (int j = i + 1; j < tiles.Count; j++) {
                if (tiles[j] == tile) count++;
            }
            if (count == 4) quads++;
        }
        return quads;
    }

    private static bool CanFormAnyWin(List<int> tiles, List<string> melds, SanhuaInfo info) {
        if (info == null || tiles == null) return false;
        melds = melds ?? new List<string>();
        if (tiles.Count + melds.Count * 3 != 14 || HasTooManyCopies(tiles)) return false;
        if (IsOrphansHand(tiles, melds, info.joker)) return true;
        for (int i = 0; i < tiles.Count; i++) {
            if (tiles[i] == 45 || tiles[i] == info.skin) return false;
        }
        var hand = LocalJokerHand.Prepare(tiles, melds, GetPolicy(info.skin, info.joker));
        if (hand == null) return false;
        bool allJiang = true;
        for (int i = 0; i < tiles.Count && allJiang; i++) {
            if (tiles[i] != info.joker && !IsJiang(tiles[i])) allJiang = false;
        }
        for (int m = 0; m < melds.Count && allJiang; m++) {
            int[] meldTiles = LocalJokerHand.MeldTiles(melds[m]);
            if (meldTiles == null) { allJiang = false; break; }
            for (int k = 0; k < meldTiles.Length; k++) {
                if (!IsJiang(meldTiles[k])) { allJiang = false; break; }
            }
        }
        if (allJiang) return true;
        return LocalJokerHand.CanWin(hand);
    }

    internal static List<Variant> Read(List<int> tiles, List<string> melds, SanhuaInfo info) {
        if (info == null || tiles == null) return new List<Variant>();
        melds = melds ?? new List<string>();
        string key = info.indicator + ":" + string.Join(",", tiles.OrderBy(t => t)) + ":" + string.Join(",", melds);
        lock (CacheLock) {
            if (Cache.TryGetValue(key, out var found)) return found;
        }
        var result = new List<Variant>();
        if (tiles.Count + melds.Count * 3 != 14 || HasTooManyCopies(tiles)) return StoreVariants(key, result);
        if (IsOrphansHand(tiles, melds, info.joker)) result.Add(new Variant { Orphans = true });
        if (tiles.Contains(45) || tiles.Contains(info.skin)) return StoreVariants(key, result);
        var hand = LocalJokerHand.Prepare(tiles, melds, GetPolicy(info.skin, info.joker));
        if (hand == null) return StoreVariants(key, result);
        var plain = tiles.Where(t => t != info.joker).ToArray();
        var external = melds.SelectMany(code => LocalJokerHand.MeldTiles(code)).ToArray();
        var ordinary = plain.Concat(external).ToArray();
        if (ordinary.All(IsJiang)) {
            result.Add(new Variant { Jiang = true, Pure = ordinary.Length == 0 || ordinary.All(t => t / 10 == ordinary[0] / 10) });
        }
        if (!LocalJokerHand.CanWin(hand)) return StoreVariants(key, result);
        int pairsFanValue = 3 + CountQuads(tiles);
        bool externalAllPung = melds.All(c => c[0] != 's');
        var seen = new HashSet<(bool, bool, bool, bool, int)>();
        foreach (var shape in LocalJokerHand.Shapes(hand)) {
            var logical = shape.Tiles.Concat(external).ToArray();
            bool pairs = shape.Kind == "seven_pairs";
            var variant = new Variant {
                Pure = logical.All(t => t < 40 && t / 10 == logical[0] / 10),
                Jiang = logical.All(IsJiang),
                Pung = !pairs && externalAllPung && shape.Closed.All(c => c[0] != 'S'),
                Eye = shape.Closed.Any(c => c[0] == 'q' && IsJiang(int.Parse(c.Substring(1)))),
                PairsFan = pairs ? pairsFanValue : 0,
            };
            if (seen.Add((variant.Pure, variant.Jiang, variant.Pung, variant.Eye, variant.PairsFan))) result.Add(variant);
        }
        return StoreVariants(key, result);
    }

    private static List<Variant> StoreVariants(string key, List<Variant> result) {
        lock (CacheLock) {
            if (Cache.Count >= 8192) Cache.Clear();
            Cache[key] = result;
        }
        return result;
    }

    private static bool IsJiang(int t) => t < 40 && (t % 10 == 2 || t % 10 == 5 || t % 10 == 8);

    internal static Price Evaluate(List<int> tiles, List<string> melds, SanhuaInfo info, int seat, int? payer,
                                   bool replacement = false, bool rob = false) {
        var result = new Price();
        if (info?.table_fans?.Length != 4 || info.declared?.Length != 4 || info.opened?.Length != 4 || seat < 0 || seat > 3)
            return result;
        melds = melds ?? new List<string>();
        int clearPayer = info.liable != null && seat < info.liable.Length ? info.liable[seat] : -1;
        if (info.declared[seat] && clearPayer < 0 && melds.Count == 2 && !rob)
            clearPayer = payer ?? -1;
        int jokerCount = 0;
        for (int i = 0; i < tiles.Count; i++) if (tiles[i] == info.joker) jokerCount++;
        bool meldsNoConcealedKong = melds.Count == 4 && melds.All(c => c[0] != 'G');
        foreach (var shape in Read(tiles, melds, info)) {
            if (info.declared[seat] && !shape.Pure || rob && !shape.Orphans) continue;
            if (shape.Orphans) return new Price { Points = payer.HasValue ? 240 + 2 * 180 : 3 * 180, Qualifies = true, Orphans = true };
            double fan = shape.PairsFan;
            if (shape.Pung && info.opened[seat]) fan += 3;
            if (shape.Pure && (info.declared[seat] || melds.Count < 2)) fan += 3;
            if (shape.Jiang) fan += 3;
            bool allCalled = payer.HasValue && meldsNoConcealedKong && (shape.Pung || shape.Eye);
            if (allCalled) fan += 3;
            bool big = fan > 0;
            if (!big && (!info.opened[seat] || !shape.Eye || jokerCount > 1)) continue;
            result.Qualifies = true;
            fan += info.table_fans[seat] + (jokerCount > 0 ? 0 : 1) + (replacement && !payer.HasValue ? 1 : 0);
            if (payer.HasValue && fan + info.table_fans[payer.Value] < 4) continue;
            bool gold = big || !payer.HasValue;
            if (gold) {
                for (int i = 0; i < 4; i++) {
                    if (i != seat && fan + info.table_fans[i] < 6) { gold = false; break; }
                }
            }
            int total = 0;
            for (int i = 0; i < 4; i++) {
                if (i == seat) continue;
                if (payer.HasValue && !big && i != payer) continue;
                double pair = fan + info.table_fans[i];
                int points = gold ? 60 : pair >= 6 ? 40 : pair >= 5 ? 20 : pair >= 4 ? 10 : 0;
                int multiplier = 1;
                if (info.declared[seat] && clearPayer == i) multiplier++;
                if (allCalled && payer == i) multiplier++;
                total += points * multiplier;
            }
            result.Points = Math.Max(result.Points, total);
        }
        return result;
    }

    internal static HashSet<int> Waiting(TingpaiQuery q) {
        var result = new HashSet<int>();
        var info = SanhuaGameState.Context(q?.RecordPlayerIndex.HasValue == true || GameRecordManager.Instance?.ShouldShowRecordTips() == true);
        if (info == null || q?.Hand == null) return result;
        var melds = q.Melds ?? new List<string>();
        var hand = new List<int>(q.Hand.Count + 1);
        hand.AddRange(q.Hand);
        hand.Add(0);
        int last = hand.Count - 1;
        foreach (int tile in LocalJokerHand.Tiles) {
            hand[last] = tile;
            if (CanFormAnyWin(hand, melds, info)) result.Add(tile);
        }
        return result;
    }

    private static WaitTileHint CopyHint(WaitTileHint h) => h == null ? null : new WaitTileHint { Kind = h.Kind, Label = h.Label };

    private static SanhuaInfo SnapshotInfo(SanhuaInfo src) {
        if (src == null) return null;
        return new SanhuaInfo {
            indicator = src.indicator,
            skin = src.skin,
            joker = src.joker,
            phase = src.phase,
            payer = src.payer,
            liable = src.liable != null ? (int[])src.liable.Clone() : null,
            table_fans = src.table_fans != null ? (double[])src.table_fans.Clone() : null,
            opened = src.opened != null ? (bool[])src.opened.Clone() : null,
            declared = src.declared != null ? (bool[])src.declared.Clone() : null,
            after_kong = src.after_kong != null ? (bool[])src.after_kong.Clone() : null,
        };
    }

    private static string HintKey(List<int> handWithWin, List<string> melds, int seat, SanhuaInfo info) {
        var sb = new StringBuilder(128);
        sb.Append(info.indicator).Append('|').Append(info.skin).Append('|').Append(info.joker)
          .Append('|').Append(seat).Append('|').Append(info.phase ?? "").Append('|').Append(info.payer?.ToString() ?? "");
        sb.Append('|');
        if (info.table_fans != null) for (int i = 0; i < info.table_fans.Length; i++) sb.Append(info.table_fans[i]).Append(',');
        sb.Append('|');
        if (info.opened != null) for (int i = 0; i < info.opened.Length; i++) sb.Append(info.opened[i] ? '1' : '0');
        sb.Append('|');
        if (info.declared != null) for (int i = 0; i < info.declared.Length; i++) sb.Append(info.declared[i] ? '1' : '0');
        sb.Append('|');
        if (info.liable != null) for (int i = 0; i < info.liable.Length; i++) sb.Append(info.liable[i]).Append(',');
        sb.Append('|');
        if (info.after_kong != null) for (int i = 0; i < info.after_kong.Length; i++) sb.Append(info.after_kong[i] ? '1' : '0');
        sb.Append('|').Append(string.Join(",", handWithWin.OrderBy(t => t)))
          .Append('|').Append(string.Join(",", (melds ?? new List<string>()).OrderBy(m => m, StringComparer.Ordinal)));
        return sb.ToString();
    }

    private static bool TryResolveContext(WaitHintQuery q, out SanhuaInfo info, out int seat, out string key) {
        info = SanhuaGameState.Context(q?.Record != null);
        seat = q?.Record?.SelfPlayerIndex ?? q?.SelfIndex ?? -1;
        if (info == null || q?.HandWithWin == null || seat < 0 || seat >= 4) {
            key = null;
            return false;
        }
        key = HintKey(q.HandWithWin, q.Melds, seat, info);
        return true;
    }

    internal static bool TryDescribeCached(WaitHintQuery q, out WaitTileHint hint) {
        if (!TryResolveContext(q, out _, out _, out string key)) {
            hint = WaitTileHint.None("未满足");
            return true;
        }
        if (CompletedHints.TryGetValue(key, out var cached)) {
            hint = CopyHint(cached);
            return true;
        }
        if (HintTasks.TryGetValue(key, out var task) && task.Status == TaskStatus.RanToCompletion) {
            CompletedHints[key] = task.Result;
            hint = CopyHint(task.Result);
            return true;
        }
        hint = null;
        return false;
    }

    internal static IEnumerator ResolveAsync(WaitHintQuery q, Action<WaitTileHint> done) {
        if (!TryResolveContext(q, out var liveInfo, out int seat, out string key)) {
            done(WaitTileHint.None("未满足"));
            yield break;
        }
        if (CompletedHints.TryGetValue(key, out var cached)) {
            done(CopyHint(cached));
            yield break;
        }
        var handCopy = new List<int>(q.HandWithWin);
        var meldsCopy = new List<string>(q.Melds ?? new List<string>());
        var infoCopy = SnapshotInfo(liveInfo);
#if UNITY_WEBGL && !UNITY_EDITOR
        var computed = ComputeDescribe(handCopy, meldsCopy, seat, infoCopy);
        StoreCompletedHint(key, computed);
        done(CopyHint(computed));
        yield break;
#else
        if (!HintTasks.TryGetValue(key, out var task)) {
            if (HintTasks.Count >= HintCacheCapacity) {
                string old = HintTasks.FirstOrDefault(p => p.Value.IsCompleted).Key;
                if (old != null) HintTasks.Remove(old);
            }
            task = Task.Factory.StartNew(
                () => ComputeDescribe(handCopy, meldsCopy, seat, infoCopy),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                Scheduler.ConcurrentScheduler);
            HintTasks[key] = task;
        }
        while (!task.IsCompleted) yield return null;
        if (task.IsFaulted) {
            HintTasks.Remove(key);
            Debug.LogException(task.Exception.GetBaseException());
            done(null);
            yield break;
        }
        StoreCompletedHint(key, task.Result);
        done(CopyHint(task.Result));
#endif
    }

    private static void StoreCompletedHint(string key, WaitTileHint hint) {
        if (CompletedHints.Count >= HintCacheCapacity) CompletedHints.Clear();
        CompletedHints[key] = hint;
    }

    internal static WaitTileHint Describe(WaitHintQuery q) => Describe(q, SanhuaGameState.Context(q?.Record != null));

    internal static WaitTileHint Describe(WaitHintQuery q, SanhuaInfo info) {
        if (info == null || q?.HandWithWin == null) return WaitTileHint.None("未满足");
        int seat = q.Record?.SelfPlayerIndex ?? q.SelfIndex;
        if (seat < 0 || seat >= 4) return WaitTileHint.None("未满足");
        return ComputeDescribe(q.HandWithWin, q.Melds, seat, info);
    }

    private static WaitTileHint ComputeDescribe(List<int> handWithWin, List<string> melds, int seat, SanhuaInfo info) {
        var self = Evaluate(handWithWin, melds, info, seat, null, info.after_kong?[seat] == true);
        bool qualifies = self.Qualifies;
        var points = new List<int>();
        for (int i = 0; i < 4; i++) {
            if (i == seat || info.payer.HasValue && info.payer != i) continue;
            var ron = Evaluate(handWithWin, melds, info, seat, i, rob: info.phase == "waiting_action_qianggang");
            qualifies |= ron.Qualifies;
            if (ron.Points > 0) points.Add(ron.Points);
        }
        if (self.Orphans && points.Count > 0)
            return WaitTileHint.Ron($"点和{points[0]}点\n自摸{self.Points}点");
        if (points.Count > 0) return WaitTileHint.Ron(points.Min() == points.Max() ? $"{points[0]}点" : $"{points.Min()}–{points.Max()}点");
        if (self.Points > 0) return WaitTileHint.TsumoOnly($"仅自摸\n{self.Points}点");
        return WaitTileHint.None(qualifies ? "未起和" : "未满足");
    }
}
