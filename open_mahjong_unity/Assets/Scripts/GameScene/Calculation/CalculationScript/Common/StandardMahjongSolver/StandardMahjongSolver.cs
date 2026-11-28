using System;
using System.Collections.Generic;

/// <summary>
/// Suit-independent, canonical-order solver for non-wildcard standard mahjong hands.
///
/// Mirrors Python server's <c>standard_solver.py</c>:
///   1. Zero meld-order permutation duplicates (canonical lowest-tile-first extraction).
///   2. Four-suit independence: each numbered suit (1..9) is packed into a 27-bit int
///      (3 bits per rank, counts 0..7) with bounded static caches (~2 MB max total).
///   3. O(1) mod-3 suit pruning + ±1 neighbor pruning for waiting-tile checks,
///      with zero heap allocations on the hot path (uses stackalloc for honors).
/// </summary>
public static class StandardMahjongSolver
{
    public readonly struct MeldUnit
    {
        public readonly byte Kind;   // 0 = triplet, 1 = sequence (center tile)
        public readonly byte TileId;

        public MeldUnit(byte kind, byte tileId)
        {
            Kind = kind;
            TileId = tileId;
        }
    }

    public readonly struct StandardDecomp
    {
        public readonly int PairTile;
        public readonly MeldUnit[] Melds;

        public StandardDecomp(int pairTile, MeldUnit[] melds)
        {
            PairTile = pairTile;
            Melds = melds;
        }
    }

    private readonly struct SuitPairDecomp
    {
        public readonly byte PairRank;
        public readonly MeldUnit[] Melds;

        public SuitPairDecomp(byte pairRank, MeldUnit[] melds)
        {
            PairRank = pairRank;
            Melds = melds;
        }
    }

    private static readonly int[] SuitBases = { 11, 21, 31 };
    private const int HonorBase = 41;
    private const int RankMask = 0x7;
    private const int FullSuitPairMask = 0x1FF;
    private const int FullHonorPairMask = 0x7F;

    private static readonly int[] Shifts = { 0, 3, 6, 9, 12, 15, 18, 21, 24 };
    private static readonly int[] Inc = {
        1 << 0, 1 << 3, 1 << 6, 1 << 9, 1 << 12, 1 << 15, 1 << 18, 1 << 21, 1 << 24
    };
    private static readonly int[] PairSub = {
        2 << 0, 2 << 3, 2 << 6, 2 << 9, 2 << 12, 2 << 15, 2 << 18, 2 << 21, 2 << 24
    };
    private static readonly int[] TripletSub = {
        3 << 0, 3 << 3, 3 << 6, 3 << 9, 3 << 12, 3 << 15, 3 << 18, 3 << 21, 3 << 24
    };
    private static readonly int[] SeqSub = {
        (1 << 0)  | (1 << 3)  | (1 << 6),
        (1 << 3)  | (1 << 6)  | (1 << 9),
        (1 << 6)  | (1 << 9)  | (1 << 12),
        (1 << 9)  | (1 << 12) | (1 << 15),
        (1 << 12) | (1 << 15) | (1 << 18),
        (1 << 15) | (1 << 18) | (1 << 21),
        (1 << 18) | (1 << 21) | (1 << 24)
    };

    private const int MaxBoolCacheEntries = 16384;
    private const int MaxDecompCacheEntries = 8192;

    private static readonly object CacheLock = new object();
    private static readonly Dictionary<int, bool> SuitCanMeldCache = new Dictionary<int, bool>(4096);
    private static readonly Dictionary<long, bool> SuitCanMeldPairCache = new Dictionary<long, bool>(4096);
    private static readonly Dictionary<int, MeldUnit[][]> SuitMeldDecompCache = new Dictionary<int, MeldUnit[][]>(2048);
    private static readonly Dictionary<long, SuitPairDecomp[]> SuitPairDecompCache = new Dictionary<long, SuitPairDecomp[]>(2048);

    public static void ClearCaches()
    {
        lock (CacheLock)
        {
            SuitCanMeldCache.Clear();
            SuitCanMeldPairCache.Clear();
            SuitMeldDecompCache.Clear();
            SuitPairDecompCache.Clear();
        }
    }

    private static int LowestNonZeroRank(int key)
    {
        for (int r = 0; r < 9; r++)
        {
            if (((key >> Shifts[r]) & RankMask) != 0)
                return r;
        }
        return -1;
    }

    private static bool SuitCanMeld(int key)
    {
        if (key == 0) return true;
        lock (CacheLock)
        {
            if (SuitCanMeldCache.TryGetValue(key, out bool cached))
                return cached;
        }
        bool res = SuitCanMeldUncached(key);
        lock (CacheLock)
        {
            if (SuitCanMeldCache.Count >= MaxBoolCacheEntries)
                SuitCanMeldCache.Clear();
            SuitCanMeldCache[key] = res;
        }
        return res;
    }

    private static bool SuitCanMeldUncached(int key)
    {
        if (key == 0) return true;
        int r = LowestNonZeroRank(key);
        int c = (key >> Shifts[r]) & RankMask;
        if (c >= 3 && SuitCanMeld(key - TripletSub[r]))
            return true;
        if (r <= 6)
        {
            int c1 = (key >> Shifts[r + 1]) & RankMask;
            int c2 = (key >> Shifts[r + 2]) & RankMask;
            if (c <= 2)
            {
                if (c1 >= c && c2 >= c)
                    return SuitCanMeld(key - c * SeqSub[r]);
                return false;
            }
            if (c1 >= 1 && c2 >= 1 && SuitCanMeld(key - SeqSub[r]))
                return true;
        }
        return false;
    }

    private static bool SuitCanMeldWithPair(int key, int pairMask = FullSuitPairMask)
    {
        long ck = ((long)pairMask << 32) | (uint)key;
        lock (CacheLock)
        {
            if (SuitCanMeldPairCache.TryGetValue(ck, out bool cached))
                return cached;
        }
        bool res = false;
        for (int r = 0; r < 9; r++)
        {
            if (((pairMask >> r) & 1) == 0) continue;
            if (((key >> Shifts[r]) & RankMask) >= 2)
            {
                if (SuitCanMeld(key - PairSub[r]))
                {
                    res = true;
                    break;
                }
            }
        }
        lock (CacheLock)
        {
            if (SuitCanMeldPairCache.Count >= MaxBoolCacheEntries)
                SuitCanMeldPairCache.Clear();
            SuitCanMeldPairCache[ck] = res;
        }
        return res;
    }

    private static MeldUnit[][] SuitMeldDecompositions(int key)
    {
        if (key == 0) return EmptyMeldResult;
        lock (CacheLock)
        {
            if (SuitMeldDecompCache.TryGetValue(key, out var cached))
                return cached;
        }
        int r = LowestNonZeroRank(key);
        int c = (key >> Shifts[r]) & RankMask;
        List<MeldUnit[]> results = null;
        if (c >= 3)
        {
            var sub = SuitMeldDecompositions(key - TripletSub[r]);
            if (sub.Length > 0)
            {
                results = new List<MeldUnit[]>(sub.Length * 2);
                var head = new MeldUnit(0, (byte)r);
                for (int i = 0; i < sub.Length; i++)
                {
                    var tail = sub[i];
                    var arr = new MeldUnit[tail.Length + 1];
                    arr[0] = head;
                    Array.Copy(tail, 0, arr, 1, tail.Length);
                    results.Add(arr);
                }
            }
        }
        if (r <= 6)
        {
            int c1 = (key >> Shifts[r + 1]) & RankMask;
            int c2 = (key >> Shifts[r + 2]) & RankMask;
            if (c1 >= 1 && c2 >= 1)
            {
                var sub = SuitMeldDecompositions(key - SeqSub[r]);
                if (sub.Length > 0)
                {
                    if (results == null) results = new List<MeldUnit[]>(sub.Length);
                    var head = new MeldUnit(1, (byte)r);
                    for (int i = 0; i < sub.Length; i++)
                    {
                        var tail = sub[i];
                        var arr = new MeldUnit[tail.Length + 1];
                        arr[0] = head;
                        Array.Copy(tail, 0, arr, 1, tail.Length);
                        results.Add(arr);
                    }
                }
            }
        }
        var finalArr = results == null ? Array.Empty<MeldUnit[]>() : results.ToArray();
        lock (CacheLock)
        {
            if (SuitMeldDecompCache.Count >= MaxDecompCacheEntries)
                SuitMeldDecompCache.Clear();
            SuitMeldDecompCache[key] = finalArr;
        }
        return finalArr;
    }

    private static readonly MeldUnit[][] EmptyMeldResult = { Array.Empty<MeldUnit>() };

    private static SuitPairDecomp[] SuitPairDecompositions(int key, int pairMask = FullSuitPairMask)
    {
        long ck = ((long)pairMask << 32) | (uint)key;
        lock (CacheLock)
        {
            if (SuitPairDecompCache.TryGetValue(ck, out var cached))
                return cached;
        }
        List<SuitPairDecomp> results = null;
        for (int r = 0; r < 9; r++)
        {
            if (((pairMask >> r) & 1) == 0) continue;
            if (((key >> Shifts[r]) & RankMask) >= 2)
            {
                var sub = SuitMeldDecompositions(key - PairSub[r]);
                if (sub.Length > 0)
                {
                    if (results == null) results = new List<SuitPairDecomp>(sub.Length * 2);
                    for (int i = 0; i < sub.Length; i++)
                        results.Add(new SuitPairDecomp((byte)r, sub[i]));
                }
            }
        }
        var finalArr = results == null ? Array.Empty<SuitPairDecomp>() : results.ToArray();
        lock (CacheLock)
        {
            if (SuitPairDecompCache.Count >= MaxDecompCacheEntries)
                SuitPairDecompCache.Clear();
            SuitPairDecompCache[ck] = finalArr;
        }
        return finalArr;
    }

    private static bool HonorsCanMeld(Span<int> hcounts)
    {
        for (int i = 0; i < 7; i++)
        {
            int c = hcounts[i];
            if (c != 0 && c != 3) return false;
        }
        return true;
    }

    private static bool HonorsCanMeldWithPair(Span<int> hcounts, int pairMask = FullHonorPairMask)
    {
        bool pairFound = false;
        for (int r = 0; r < 7; r++)
        {
            int c = hcounts[r];
            if (c == 0 || c == 3) continue;
            if ((c == 2 || c == 5) && !pairFound && ((pairMask >> r) & 1) != 0)
                pairFound = true;
            else
                return false;
        }
        return pairFound;
    }

    private static bool ParseSuits(
        IReadOnlyList<int> handTiles,
        bool allowHonors,
        out int s0, out int s1, out int s2,
        Span<int> hcounts,
        Span<int> totals,
        out int n)
    {
        s0 = 0; s1 = 0; s2 = 0;
        hcounts.Clear();
        totals.Clear();
        n = handTiles.Count;
        for (int i = 0; i < n; i++)
        {
            int t = handTiles[i];
            if (t >= 11 && t <= 19)
            {
                int r = t - 11;
                if (((s0 >> Shifts[r]) & RankMask) >= 6) return false;
                s0 += Inc[r];
                totals[0]++;
            }
            else if (t >= 21 && t <= 29)
            {
                int r = t - 21;
                if (((s1 >> Shifts[r]) & RankMask) >= 6) return false;
                s1 += Inc[r];
                totals[1]++;
            }
            else if (t >= 31 && t <= 39)
            {
                int r = t - 31;
                if (((s2 >> Shifts[r]) & RankMask) >= 6) return false;
                s2 += Inc[r];
                totals[2]++;
            }
            else if (allowHonors && t >= 41 && t <= 47)
            {
                int r = t - 41;
                if (hcounts[r] >= 6) return false;
                hcounts[r]++;
                totals[3]++;
            }
            else
            {
                return false;
            }
        }
        return true;
    }

    public static bool CanStandardWin(
        IReadOnlyList<int> handTiles,
        bool allowHonors = true,
        int suitPairMask = FullSuitPairMask,
        int honorPairMask = FullHonorPairMask)
    {
        if (handTiles == null) return false;
        int n = handTiles.Count;
        if (n < 2 || n % 3 != 2) return false;

        Span<int> hcounts = stackalloc int[7];
        Span<int> totals = stackalloc int[4];
        if (!ParseSuits(handTiles, allowHonors, out int s0, out int s1, out int s2, hcounts, totals, out _))
            return false;

        int pairSuit = -1;
        for (int s = 0; s < 4; s++)
        {
            int rem = totals[s] % 3;
            if (rem == 1) return false;
            if (rem == 2)
            {
                if (pairSuit >= 0) return false;
                pairSuit = s;
            }
        }
        if (pairSuit < 0) return false;

        if (pairSuit == 0) { if (!SuitCanMeldWithPair(s0, suitPairMask)) return false; }
        else if (s0 != 0 && !SuitCanMeld(s0)) return false;

        if (pairSuit == 1) { if (!SuitCanMeldWithPair(s1, suitPairMask)) return false; }
        else if (s1 != 0 && !SuitCanMeld(s1)) return false;

        if (pairSuit == 2) { if (!SuitCanMeldWithPair(s2, suitPairMask)) return false; }
        else if (s2 != 0 && !SuitCanMeld(s2)) return false;

        if (pairSuit == 3) return HonorsCanMeldWithPair(hcounts, honorPairMask);
        if (totals[3] != 0 && !HonorsCanMeld(hcounts)) return false;
        return true;
    }

    public static HashSet<int> StandardWaitingTiles(
        IReadOnlyList<int> handTiles,
        bool allowHonors = true,
        bool allowFifthCopy = false,
        int suitPairMask = FullSuitPairMask,
        int honorPairMask = FullHonorPairMask)
    {
        var waits = new HashSet<int>();
        CollectStandardWaitingTiles(handTiles, waits, allowHonors, allowFifthCopy, suitPairMask, honorPairMask);
        return waits;
    }

    public static void CollectStandardWaitingTiles(
        IReadOnlyList<int> handTiles,
        HashSet<int> waits,
        bool allowHonors = true,
        bool allowFifthCopy = false,
        int suitPairMask = FullSuitPairMask,
        int honorPairMask = FullHonorPairMask)
    {
        if (handTiles == null) return;
        int n = handTiles.Count;
        if (n < 1 || n % 3 != 1) return;

        Span<int> hcounts = stackalloc int[7];
        Span<int> totals = stackalloc int[4];
        if (!ParseSuits(handTiles, allowHonors, out int s0, out int s1, out int s2, hcounts, totals, out _))
            return;

        Span<int> skeys = stackalloc int[3];
        skeys[0] = s0; skeys[1] = s1; skeys[2] = s2;

        int mod1Count = 0, mod1Suit = -1;
        int mod2Count = 0, mod2A = -1, mod2B = -1;
        for (int s = 0; s < 4; s++)
        {
            int rem = totals[s] % 3;
            if (rem == 1) { mod1Suit = s; mod1Count++; }
            else if (rem == 2) { if (mod2Count == 0) mod2A = s; else mod2B = s; mod2Count++; }
        }

        int maxExisting = allowFifthCopy ? 4 : 3;

        // Case 1: one suit has mod 3 == 1, other three have mod 3 == 0.
        if (mod1Count == 1 && mod2Count == 0)
        {
            for (int s = 0; s < 3; s++)
                if (s != mod1Suit && skeys[s] != 0 && !SuitCanMeld(skeys[s]))
                    return;
            if (mod1Suit != 3 && totals[3] != 0 && !HonorsCanMeld(hcounts))
                return;

            if (mod1Suit == 3)
            {
                for (int r = 0; r < 7; r++)
                {
                    if (hcounts[r] > 0 && hcounts[r] <= maxExisting)
                    {
                        hcounts[r]++;
                        if (HonorsCanMeldWithPair(hcounts, honorPairMask))
                            waits.Add(HonorBase + r);
                        hcounts[r]--;
                    }
                }
            }
            else
            {
                int key = skeys[mod1Suit];
                int baseId = SuitBases[mod1Suit];
                for (int r = 0; r < 9; r++)
                {
                    int c = (key >> Shifts[r]) & RankMask;
                    if (c > maxExisting) continue;
                    bool hasNeighbor = c > 0
                        || (r > 0 && ((key >> Shifts[r - 1]) & RankMask) > 0)
                        || (r < 8 && ((key >> Shifts[r + 1]) & RankMask) > 0);
                    if (hasNeighbor && SuitCanMeldWithPair(key + Inc[r], suitPairMask))
                        waits.Add(baseId + r);
                }
            }
            return;
        }

        // Case 2: two suits have mod 3 == 2, other two have mod 3 == 0.
        if (mod1Count == 0 && mod2Count == 2)
        {
            for (int s = 0; s < 3; s++)
                if (s != mod2A && s != mod2B && skeys[s] != 0 && !SuitCanMeld(skeys[s]))
                    return;
            if (mod2A != 3 && mod2B != 3 && totals[3] != 0 && !HonorsCanMeld(hcounts))
                return;

            bool aHasPair = mod2A == 3
                ? HonorsCanMeldWithPair(hcounts, honorPairMask)
                : SuitCanMeldWithPair(skeys[mod2A], suitPairMask);
            bool bHasPair = mod2B == 3
                ? HonorsCanMeldWithPair(hcounts, honorPairMask)
                : SuitCanMeldWithPair(skeys[mod2B], suitPairMask);

            if (bHasPair)
                TryCompleteSuitToAllMelds(mod2A, skeys, hcounts, maxExisting, waits);
            if (aHasPair)
                TryCompleteSuitToAllMelds(mod2B, skeys, hcounts, maxExisting, waits);
        }
    }

    private static void TryCompleteSuitToAllMelds(
        int suit,
        Span<int> skeys,
        Span<int> hcounts,
        int maxExisting,
        HashSet<int> waits)
    {
        if (suit == 3)
        {
            for (int r = 0; r < 7; r++)
            {
                if (hcounts[r] > 0 && hcounts[r] <= maxExisting)
                {
                    hcounts[r]++;
                    if (HonorsCanMeld(hcounts))
                        waits.Add(HonorBase + r);
                    hcounts[r]--;
                }
            }
            return;
        }
        int key = skeys[suit];
        int baseId = SuitBases[suit];
        for (int r = 0; r < 9; r++)
        {
            int c = (key >> Shifts[r]) & RankMask;
            if (c > maxExisting) continue;
            bool hasNeighbor = c > 0
                || (r > 0 && ((key >> Shifts[r - 1]) & RankMask) > 0)
                || (r < 8 && ((key >> Shifts[r + 1]) & RankMask) > 0);
            if (hasNeighbor && SuitCanMeld(key + Inc[r]))
                waits.Add(baseId + r);
        }
    }

    public static List<StandardDecomp> StandardDecompositions(
        IReadOnlyList<int> handTiles,
        bool allowHonors = true,
        int suitPairMask = FullSuitPairMask,
        int honorPairMask = FullHonorPairMask)
    {
        var output = new List<StandardDecomp>();
        if (handTiles == null) return output;
        int n = handTiles.Count;
        if (n < 2 || n % 3 != 2) return output;

        Span<int> hcounts = stackalloc int[7];
        Span<int> totals = stackalloc int[4];
        if (!ParseSuits(handTiles, allowHonors, out int s0, out int s1, out int s2, hcounts, totals, out _))
            return output;

        int pairSuit = -1;
        for (int s = 0; s < 4; s++)
        {
            int rem = totals[s] % 3;
            if (rem == 1) return output;
            if (rem == 2)
            {
                if (pairSuit >= 0) return output;
                pairSuit = s;
            }
        }
        if (pairSuit < 0) return output;

        int[] skeys = { s0, s1, s2 };
        var suitParts = new MeldUnit[4][][];

        for (int s = 0; s < 3; s++)
        {
            if (s == pairSuit) continue;
            if (skeys[s] == 0)
            {
                suitParts[s] = EmptyMeldResult;
            }
            else
            {
                var raw = SuitMeldDecompositions(skeys[s]);
                if (raw.Length == 0) return output;
                int baseId = SuitBases[s];
                var mapped = new MeldUnit[raw.Length][];
                for (int i = 0; i < raw.Length; i++)
                {
                    var src = raw[i];
                    var dst = new MeldUnit[src.Length];
                    for (int j = 0; j < src.Length; j++)
                        dst[j] = new MeldUnit(src[j].Kind, (byte)(baseId + src[j].TileId + src[j].Kind));
                    mapped[i] = dst;
                }
                suitParts[s] = mapped;
            }
        }

        if (pairSuit != 3)
        {
            if (totals[3] == 0)
            {
                suitParts[3] = EmptyMeldResult;
            }
            else
            {
                var hList = new List<MeldUnit>(4);
                for (int r = 0; r < 7; r++)
                {
                    int c = hcounts[r];
                    if (c == 0) continue;
                    if (c == 3) hList.Add(new MeldUnit(0, (byte)(HonorBase + r)));
                    else return output;
                }
                suitParts[3] = new[] { hList.ToArray() };
            }
        }

        var pairOptions = new List<StandardDecomp>();
        if (pairSuit == 3)
        {
            int pairTile = -1;
            var hList = new List<MeldUnit>(4);
            for (int r = 0; r < 7; r++)
            {
                int c = hcounts[r];
                if (c == 0) continue;
                if (c == 3) hList.Add(new MeldUnit(0, (byte)(HonorBase + r)));
                else if (c == 5 && pairTile < 0 && ((honorPairMask >> r) & 1) != 0)
                {
                    pairTile = HonorBase + r;
                    hList.Add(new MeldUnit(0, (byte)(HonorBase + r)));
                }
                else if (c == 2 && pairTile < 0 && ((honorPairMask >> r) & 1) != 0)
                {
                    pairTile = HonorBase + r;
                }
                else return output;
            }
            if (pairTile < 0) return output;
            pairOptions.Add(new StandardDecomp(pairTile, hList.ToArray()));
        }
        else
        {
            var rawPairs = SuitPairDecompositions(skeys[pairSuit], suitPairMask);
            if (rawPairs.Length == 0) return output;
            int baseId = SuitBases[pairSuit];
            for (int i = 0; i < rawPairs.Length; i++)
            {
                var rp = rawPairs[i];
                var mapped = new MeldUnit[rp.Melds.Length];
                for (int j = 0; j < rp.Melds.Length; j++)
                    mapped[j] = new MeldUnit(rp.Melds[j].Kind, (byte)(baseId + rp.Melds[j].TileId + rp.Melds[j].Kind));
                pairOptions.Add(new StandardDecomp(baseId + rp.PairRank, mapped));
            }
        }

        MeldUnit[][] o0 = null, o1 = null, o2 = null;
        int oi = 0;
        for (int s = 0; s < 4; s++)
        {
            if (s == pairSuit) continue;
            if (oi == 0) o0 = suitParts[s];
            else if (oi == 1) o1 = suitParts[s];
            else o2 = suitParts[s];
            oi++;
        }

        for (int pi = 0; pi < pairOptions.Count; pi++)
        {
            var po = pairOptions[pi];
            for (int i0 = 0; i0 < o0.Length; i0++)
            {
                var m0 = o0[i0];
                for (int i1 = 0; i1 < o1.Length; i1++)
                {
                    var m1 = o1[i1];
                    for (int i2 = 0; i2 < o2.Length; i2++)
                    {
                        var m2 = o2[i2];
                        var combined = new MeldUnit[po.Melds.Length + m0.Length + m1.Length + m2.Length];
                        int pos = 0;
                        if (po.Melds.Length > 0) { Array.Copy(po.Melds, 0, combined, pos, po.Melds.Length); pos += po.Melds.Length; }
                        if (m0.Length > 0) { Array.Copy(m0, 0, combined, pos, m0.Length); pos += m0.Length; }
                        if (m1.Length > 0) { Array.Copy(m1, 0, combined, pos, m1.Length); pos += m1.Length; }
                        if (m2.Length > 0) { Array.Copy(m2, 0, combined, pos, m2.Length); }
                        output.Add(new StandardDecomp(po.PairTile, combined));
                    }
                }
            }
        }
        return output;
    }

    public static List<List<string>> StandardCombinationCodes(
        IReadOnlyList<int> handTiles,
        IReadOnlyList<string> baseCombinations,
        bool allowHonors = true,
        char keziPrefix = 'K',
        char shunziPrefix = 'S',
        char quetouPrefix = 'q')
    {
        var decomps = StandardDecompositions(handTiles, allowHonors);
        var results = new List<List<string>>(decomps.Count);
        if (decomps.Count == 0) return results;

        int baseCount = baseCombinations != null ? baseCombinations.Count : 0;
        for (int i = 0; i < decomps.Count; i++)
        {
            var d = decomps[i];
            var comb = new List<string>(baseCount + 1 + d.Melds.Length);
            for (int b = 0; b < baseCount; b++)
                comb.Add(baseCombinations[b]);
            comb.Add(quetouPrefix + d.PairTile.ToString());
            for (int m = 0; m < d.Melds.Length; m++)
            {
                var unit = d.Melds[m];
                comb.Add((unit.Kind == 1 ? shunziPrefix : keziPrefix) + unit.TileId.ToString());
            }
            comb.Sort(StringComparer.Ordinal);
            results.Add(comb);
        }
        return results;
    }
}
