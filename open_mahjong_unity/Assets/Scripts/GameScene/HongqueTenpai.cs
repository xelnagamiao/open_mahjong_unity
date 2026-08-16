using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

/// <summary>杠和（杠完即和）的杠后状态：摸到的听牌、杠后剩余手牌与扩展后的副露。</summary>
public sealed class HongqueKongWinOption {
    public int TileId;
    public List<int> HandAfterKong;
    public List<int[]> MeldsAfterKong;
    public HongqueWinScore Score;
}

/// <summary>
/// 虹雀听牌计算：与服务端 game_hongque/group_index.py 对应的位掩码实现。
/// 126 张唯一牌，用 BigInteger 表示 126 位手牌掩码，静态预生成全部合法组。
/// 用于本地“打出某张即可听牌”的切牌预测提示。
/// </summary>
public static class HongqueTenpai {
    private static readonly string[] ColourCodes = {
        "AX", "AY", "BX", "BY", "CX", "CY", "DX",
        "DY", "EX", "EY", "FX", "FY", "GX", "GY"
    };

    private static readonly string[] Deck;
    private static readonly Dictionary<string, int> TileIndex;
    private static readonly BigInteger FullDeckMask;
    private static readonly BigInteger[] GroupMasks;
    private static readonly List<BigInteger>[] GroupsByTile;
    private static readonly Dictionary<BigInteger, bool> CanPartitionCache = new Dictionary<BigInteger, bool>();

    static HongqueTenpai() {
        Deck = new string[14 * 9];
        TileIndex = new Dictionary<string, int>(14 * 9);
        int index = 0;
        for (int colour = 0; colour < 14; colour++) {
            for (int number = 1; number <= 9; number++) {
                string code = ColourCodes[colour] + number;
                Deck[index] = code;
                TileIndex[code] = index;
                index++;
            }
        }
        FullDeckMask = (BigInteger.One << (14 * 9)) - 1;
        GroupMasks = GenerateGroupMasks();
        GroupsByTile = new List<BigInteger>[14 * 9];
        for (int i = 0; i < GroupsByTile.Length; i++) GroupsByTile[i] = new List<BigInteger>();
        foreach (BigInteger group in GroupMasks) {
            BigInteger remaining = group;
            while (remaining != 0) {
                BigInteger bit = remaining & -remaining;
                GroupsByTile[LowestBitIndex(bit)].Add(group);
                remaining ^= bit;
            }
        }
    }

    private static BigInteger TileMask(int colour, int number) {
        return BigInteger.One << TileIndex[ColourCodes[colour] + number];
    }

    /// <summary>生成全部合法组：同数循环花色（刻）与等差数字序列（顺），与 group_index.py 一致。</summary>
    private static BigInteger[] GenerateGroupMasks() {
        HashSet<BigInteger> groups = new HashSet<BigInteger>();
        for (int number = 1; number <= 9; number++) {
            foreach (var pair in new[] { (step: 1, max: 14), (step: 2, max: 7) }) {
                for (int length = 3; length <= pair.max; length++) {
                    for (int startColour = 0; startColour < 14; startColour++) {
                        BigInteger mask = 0;
                        for (int offset = 0; offset < length; offset++) {
                            mask |= TileMask((startColour + offset * pair.step) % 14, number);
                        }
                        groups.Add(mask);
                    }
                }
            }
        }
        foreach (int numberStep in new[] { -4, -3, -2, -1, 1, 2, 3, 4 }) {
            for (int startNumber = 1; startNumber <= 9; startNumber++) {
                List<int> numbers = new List<int>();
                int number = startNumber;
                while (number >= 1 && number <= 9) {
                    numbers.Add(number);
                    number += numberStep;
                }
                for (int length = 3; length <= numbers.Count; length++) {
                    foreach (int colourStep in new[] { 0, 1, 2 }) {
                        for (int startColour = 0; startColour < 14; startColour++) {
                            BigInteger mask = 0;
                            for (int offset = 0; offset < length; offset++) {
                                mask |= TileMask((startColour + offset * colourStep) % 14, numbers[offset]);
                            }
                            groups.Add(mask);
                        }
                    }
                }
            }
        }
        return groups.OrderBy(group => (BitCount(group), group)).ToArray();
    }

    private static int LowestBitIndex(BigInteger bit) {
        int index = 0;
        while ((bit & BigInteger.One) == 0) {
            bit >>= 1;
            index++;
        }
        return index;
    }

    private static int BitCount(BigInteger value) {
        int count = 0;
        while (value != 0) {
            value &= value - 1;
            count++;
        }
        return count;
    }

    private static BigInteger MaskFromCodes(IEnumerable<string> codes) {
        BigInteger mask = 0;
        foreach (string source in codes) {
            string code = source == null ? null : source.Trim().ToUpperInvariant();
            if (code == null || code.Length != 3 || !TileIndex.TryGetValue(code, out int bitIndex)) {
                continue;
            }
            BigInteger bit = BigInteger.One << bitIndex;
            if ((mask & bit) != 0) continue; // 唯一牌：重复输入时忽略，不抛错
            mask |= bit;
        }
        return mask;
    }

    private static bool CanPartitionMask(BigInteger mask) {
        if (mask == 0) return true;
        if (CanPartitionCache.TryGetValue(mask, out bool cached)) return cached;
        BigInteger anchor = mask & -mask;
        bool result = false;
        foreach (BigInteger group in GroupsByTile[LowestBitIndex(anchor)]) {
            if ((group & mask) == group && CanPartitionMask(mask ^ group)) {
                result = true;
                break;
            }
        }
        CanPartitionCache[mask] = result;
        return result;
    }

    private static readonly Dictionary<BigInteger, List<List<BigInteger>>> PartitionCache =
        new Dictionary<BigInteger, List<List<BigInteger>>>();
    // 切牌悬停预览：同一手牌状态被反复悬停时，全量听牌枚举只算一次。
    private static readonly Dictionary<(BigInteger, BigInteger, bool), Dictionary<BigInteger, BigInteger>>
        WaitingMasksCache = new Dictionary<(BigInteger, BigInteger, bool), Dictionary<BigInteger, BigInteger>>();
    // 杠和候选：同一副明牌的可扩展合法组只扫描一次。
    private static readonly Dictionary<BigInteger, List<(BigInteger Group, BigInteger Extra)>>
        MeldExtensionsCache = new Dictionary<BigInteger, List<(BigInteger Group, BigInteger Extra)>>();
    private const int TenpaiCacheLimit = 512;

    /// <summary>新对局/新牌局开始时清空听牌缓存，避免跨局残留。</summary>
    public static void ClearCaches() {
        WaitingMasksCache.Clear();
        MeldExtensionsCache.Clear();
        CanPartitionCache.Clear();
        PartitionCache.Clear();
    }

    /// <summary>枚举掩码的全部合法组划分（对应 group_index.partition_masks）。</summary>
    private static List<List<BigInteger>> PartitionMasks(BigInteger mask) {
        if (mask == 0) return new List<List<BigInteger>> { new List<BigInteger>() };
        if (PartitionCache.TryGetValue(mask, out List<List<BigInteger>> cached)) return cached;
        List<List<BigInteger>> results = new List<List<BigInteger>>();
        BigInteger anchor = mask & -mask;
        foreach (BigInteger group in GroupsByTile[LowestBitIndex(anchor)]) {
            if ((group & mask) != group) continue;
            foreach (List<BigInteger> tail in PartitionMasks(mask ^ group)) {
                List<BigInteger> partition = new List<BigInteger> { group };
                partition.AddRange(tail);
                results.Add(partition);
            }
        }
        PartitionCache[mask] = results;
        return results;
    }

    /// <summary>掩码 → 牌 ID 列表。</summary>
    private static List<int> TileIdsFromMask(BigInteger mask) {
        List<int> ids = new List<int>();
        BigInteger bits = mask;
        while (bits != 0) {
            BigInteger bit = bits & -bits;
            int tileId = HongqueTileVisual.FromCode(Deck[LowestBitIndex(bit)]);
            if (tileId != 0) ids.Add(tileId);
            bits ^= bit;
        }
        return ids;
    }

    /// <summary>
    /// 手牌的全部合法暗组划分（对应 group_index.partitions_from_codes）。
    /// 供和牌分解与计分使用。
    /// </summary>
    public static List<List<List<int>>> PartitionsFromCodes(List<int> handTileIds) {
        List<List<List<int>>> result = new List<List<List<int>>>();
        if (handTileIds == null || handTileIds.Count == 0) return result;
        BigInteger mask = 0;
        foreach (int tileId in handTileIds) {
            string code = HongqueTileVisual.ToCode(tileId);
            if (code == null || !TileIndex.TryGetValue(code, out int bitIndex)) return result;
            BigInteger bit = BigInteger.One << bitIndex;
            if ((mask & bit) != 0) return result; // 唯一牌：重复输入无合法划分
            mask |= bit;
        }
        foreach (List<BigInteger> partition in PartitionMasks(mask)) {
            result.Add(partition.Select(TileIdsFromMask).ToList());
        }
        return result;
    }

    /// <summary>构建容器掩码子集的快速分区检查器（对应 _partition_checker_within）。</summary>
    private static Func<BigInteger, bool> BuildPartitionCheckerWithin(BigInteger containerMask) {
        Dictionary<BigInteger, List<BigInteger>> byAnchor = new Dictionary<BigInteger, List<BigInteger>>();
        int containerSize = BitCount(containerMask);
        foreach (BigInteger group in GroupMasks) {
            if (BitCount(group) > containerSize) break;
            if ((group & containerMask) != group) continue;
            BigInteger bits = group;
            while (bits != 0) {
                BigInteger bit = bits & -bits;
                if (!byAnchor.TryGetValue(bit, out List<BigInteger> list)) {
                    list = new List<BigInteger>();
                    byAnchor[bit] = list;
                }
                list.Add(group);
                bits ^= bit;
            }
        }
        Dictionary<BigInteger, bool> memo = new Dictionary<BigInteger, bool>();
        bool CanPartitionSubset(BigInteger mask) {
            if (mask == 0) return true;
            if (memo.TryGetValue(mask, out bool cached)) return cached;
            bool ok = false;
            if (byAnchor.TryGetValue(mask & -mask, out List<BigInteger> candidates)) {
                foreach (BigInteger group in candidates) {
                    if ((group & mask) == group && CanPartitionSubset(mask ^ group)) {
                        ok = true;
                        break;
                    }
                }
            }
            memo[mask] = ok;
            return ok;
        }
        return CanPartitionSubset;
    }

    /// <summary>
    /// 返回每张可弃牌对应的听牌掩码（对应 group_index.waiting_masks_after_discards）。
    /// handMask 为弃牌前完整手牌；usedMask 需包含弃牌前手牌与副露（被弃的牌成为可见牌，不可再摸）。
    /// </summary>
    public static Dictionary<BigInteger, BigInteger> WaitingMasksAfterDiscards(
        BigInteger handMask,
        BigInteger usedMask,
        bool hasOpenGroup) {
        var cacheKey = (handMask, usedMask, hasOpenGroup);
        if (WaitingMasksCache.TryGetValue(cacheKey, out Dictionary<BigInteger, BigInteger> cached)) {
            return new Dictionary<BigInteger, BigInteger>(cached);
        }
        List<BigInteger> discardBits = new List<BigInteger>();
        BigInteger remaining = handMask;
        while (remaining != 0) {
            BigInteger bit = remaining & -remaining;
            discardBits.Add(bit);
            remaining ^= bit;
        }
        Dictionary<BigInteger, BigInteger> waitsByDiscard = new Dictionary<BigInteger, BigInteger>();
        foreach (BigInteger bit in discardBits) waitsByDiscard[bit] = 0;
        if (discardBits.Count == 0) return waitsByDiscard;

        BigInteger availableMask = FullDeckMask & ~usedMask;
        Func<BigInteger, bool> canPartitionSubset = BuildPartitionCheckerWithin(handMask);
        Dictionary<BigInteger, bool> acceptsCache = new Dictionary<BigInteger, bool>();
        Func<BigInteger, bool> AcceptsGroupRemainder = null;
        AcceptsGroupRemainder = mask => {
            if (acceptsCache.TryGetValue(mask, out bool cached)) return cached;
            bool result = canPartitionSubset(mask);
            acceptsCache[mask] = result;
            return result;
        };

        int handSize = BitCount(handMask);
        foreach (BigInteger groupMask in GroupMasks) {
            int groupSize = BitCount(groupMask);
            if (groupSize > handSize) break;
            BigInteger overlap = groupMask & handMask;
            if (BitCount(overlap) != groupSize - 1 || BitCount(overlap) < 2) continue;
            BigInteger missing = groupMask ^ overlap;
            if ((missing & availableMask) == 0) continue;
            BigInteger eligibleDiscards = handMask & ~overlap;
            while (eligibleDiscards != 0) {
                BigInteger discardBit = eligibleDiscards & -eligibleDiscards;
                BigInteger remainder = handMask ^ discardBit ^ overlap;
                if (AcceptsGroupRemainder(remainder)) {
                    waitsByDiscard[discardBit] |= missing;
                }
                eligibleDiscards ^= discardBit;
            }
        }

        foreach (BigInteger discardBit in discardBits) {
            waitsByDiscard[discardBit] &= availableMask;
        }
        if (WaitingMasksCache.Count >= TenpaiCacheLimit) WaitingMasksCache.Clear();
        WaitingMasksCache[cacheKey] = waitsByDiscard;
        return waitsByDiscard;
    }

    /// <summary>
    /// 切牌预测：fullHandTileIds 为弃牌前完整手牌，discardTileId 为鼠标悬停的牌；
    /// 返回打出该牌后仍能听的所有和牌张（虹雀牌 ID）。
    /// </summary>
    public static HashSet<int> WaitingTilesAfterDiscard(
        List<int> fullHandTileIds,
        List<int[]> meldMasks,
        int discardTileId) {
        if (fullHandTileIds == null || discardTileId == 0) return new HashSet<int>();
        List<int> handAfterDiscard = new List<int>(fullHandTileIds);
        if (!handAfterDiscard.Remove(discardTileId)) return new HashSet<int>();
        return WaitingTiles(handAfterDiscard, meldMasks, discardTileId);
    }

    /// <summary>
    /// 对当前手牌计算普通听牌；悬停预测与实际出牌后的右侧提示共用本入口。
    /// excludedTileId 用于把假想弃牌计入已使用牌，避免唯一牌重新成为进张。
    /// </summary>
    public static HashSet<int> WaitingTiles(
        List<int> handTileIds,
        List<int[]> meldMasks,
        int? excludedTileId = null) {
        HashSet<int> result = new HashSet<int>();
        if (handTileIds == null) return result;
        List<string> handCodes = handTileIds
            .Select(HongqueTileVisual.ToCode)
            .Where(code => code != null)
            .ToList();
        if (handCodes.Count != handTileIds.Count || handCodes.Distinct().Count() != handCodes.Count) {
            return result;
        }
        List<string> usedCodes = new List<string>(handCodes);
        if (excludedTileId.HasValue) {
            string excluded = HongqueTileVisual.ToCode(excludedTileId.Value);
            if (excluded != null) usedCodes.Add(excluded);
        }
        if (meldMasks != null) {
            foreach (int[] mask in meldMasks) {
                if (mask == null) continue;
                for (int i = 1; i < mask.Length; i += 2) {
                    string code = HongqueTileVisual.ToCode(mask[i]);
                    if (code != null) usedCodes.Add(code);
                }
            }
        }
        BigInteger handMask = MaskFromCodes(handCodes);
        BigInteger availableMask = FullDeckMask & ~MaskFromCodes(usedCodes);
        Func<BigInteger, bool> canPartitionSubset = BuildPartitionCheckerWithin(handMask);
        BigInteger waitMask = 0;
        foreach (BigInteger groupMask in GroupMasks) {
            BigInteger overlap = groupMask & handMask;
            if (BitCount(overlap) != BitCount(groupMask) - 1 || BitCount(overlap) < 2) continue;
            if (canPartitionSubset(handMask ^ overlap)) waitMask |= groupMask ^ overlap;
        }
        BigInteger bits = waitMask & availableMask;
        while (bits != 0) {
            BigInteger bit = bits & -bits;
            int tileId = HongqueTileVisual.FromCode(Deck[LowestBitIndex(bit)]);
            if (tileId != 0) result.Add(tileId);
            bits ^= bit;
        }
        return result;
    }

    /// <summary>用统一 C# 路径计算听牌张与直接分值，供悬停预测及出牌后提示共同调用。</summary>
    public static HongqueScoreHintInfo[] BuildScoreHints(
        List<int> handTileIds,
        List<int[]> meldMasks,
        int? excludedTileId = null) {
        List<int> hand = handTileIds != null ? new List<int>(handTileIds) : new List<int>();
        List<int[]> melds = meldMasks ?? new List<int[]>();
        HashSet<int> normalWaits = WaitingTiles(hand, melds, excludedTileId);
        Dictionary<int, HongqueKongWinOption> kongWaits =
            BestKongWinOptions(hand, melds, excludedTileId);
        List<int> allWaits = normalWaits.Concat(kongWaits.Keys).Distinct().OrderBy(id => id).ToList();
        List<HongqueScoreHintInfo> hints = new List<HongqueScoreHintInfo>();
        foreach (int tileId in allWaits) {
            bool selfDrawOnly = !normalWaits.Contains(tileId);
            HongqueWinScore score;
            if (selfDrawOnly) {
                score = kongWaits[tileId].Score;
            } else {
                List<int> winningHand = new List<int>(hand) { tileId };
                score = HongqueScoring.BestWinResult(
                    winningHand, melds, false, false, false);
            }
            if (score == null) continue;
            hints.Add(new HongqueScoreHintInfo {
                tile = HongqueTileVisual.ToCode(tileId),
                @base = score.Base,
                fan_total = score.FanTotal,
                points = score.Points,
                fans = Array.Empty<HongqueFanInfo>(),
                self_draw_only = selfDrawOnly,
            });
        }
        return hints.ToArray();
    }

    private static List<int> TilesFromMeldMask(int[] mask) {
        List<int> tiles = new List<int>();
        if (mask == null) return tiles;
        for (int i = 1; i < mask.Length; i += 2) {
            if (mask[i] != 0) tiles.Add(mask[i]);
        }
        return tiles;
    }

    private static int[] MaskFromTileIds(List<int> tiles) {
        int[] mask = new int[tiles.Count * 2];
        for (int i = 0; i < tiles.Count; i++) {
            mask[i * 2] = 0;
            mask[i * 2 + 1] = tiles[i];
        }
        return mask;
    }

    private static List<(BigInteger Group, BigInteger Extra)> MeldExtensions(BigInteger meldMask) {
        if (MeldExtensionsCache.TryGetValue(
                meldMask, out List<(BigInteger Group, BigInteger Extra)> cached)) {
            return cached;
        }
        BigInteger anchorBit = meldMask & -meldMask;
        int anchorIndex = LowestBitIndex(anchorBit);
        List<(BigInteger, BigInteger)> extensions = new List<(BigInteger, BigInteger)>();
        foreach (BigInteger group in GroupsByTile[anchorIndex]) {
            if ((group & meldMask) != meldMask) continue;
            BigInteger extra = group ^ meldMask;
            if (extra == 0) continue;
            extensions.Add((group, extra));
        }
        if (MeldExtensionsCache.Count >= TenpaiCacheLimit) MeldExtensionsCache.Clear();
        MeldExtensionsCache[meldMask] = extensions;
        return extensions;
    }

    /// <summary>
    /// 对应 rules.kong_win_candidates：每次只把 1 张手牌并入一副明牌。
    /// 供 BestWinResult(allowKongWin) 与服务端自摸结算对齐。
    /// </summary>
    public static List<HongqueKongWinOption> KongWinCandidates(
        List<int> handTileIds,
        List<int[]> meldMasks) {
        List<HongqueKongWinOption> results = new List<HongqueKongWinOption>();
        if (handTileIds == null || meldMasks == null || meldMasks.Count == 0) {
            return results;
        }
        List<string> handCodes = handTileIds
            .Select(HongqueTileVisual.ToCode)
            .Where(code => code != null)
            .ToList();
        if (handCodes.Count != handTileIds.Count) return results;
        BigInteger handMask = MaskFromCodes(handCodes);

        for (int meldIndex = 0; meldIndex < meldMasks.Count; meldIndex++) {
            int[] mask = meldMasks[meldIndex];
            List<int> meldTiles = TilesFromMeldMask(mask);
            if (meldTiles.Count < 3) continue;
            List<string> meldCodes = meldTiles
                .Select(HongqueTileVisual.ToCode)
                .Where(code => code != null)
                .ToList();
            if (meldCodes.Count != meldTiles.Count) continue;
            BigInteger meldMask = MaskFromCodes(meldCodes);
            foreach ((BigInteger group, BigInteger extra) in MeldExtensions(meldMask)) {
                if (BitCount(extra) != 1) continue;
                if ((extra & handMask) != extra) continue;
                BigInteger rest = handMask ^ extra;
                if (rest != 0 && !CanPartitionMask(rest)) continue;

                results.Add(new HongqueKongWinOption {
                    TileId = HongqueTileVisual.FromCode(Deck[LowestBitIndex(extra)]),
                    HandAfterKong = TileIdsFromMask(rest),
                    MeldsAfterKong = BuildMeldsAfter(meldMasks, meldIndex, group),
                });
            }
        }
        return results;
    }

    private static List<int[]> BuildMeldsAfter(List<int[]> meldMasks, int meldIndex, BigInteger group) {
        List<int[]> meldsAfter = new List<int[]>();
        for (int i = 0; i < meldMasks.Count; i++) {
            if (i != meldIndex) {
                if (meldMasks[i] != null) meldsAfter.Add(meldMasks[i]);
                continue;
            }
            meldsAfter.Add(MaskFromTileIds(TileIdsFromMask(group)));
        }
        return meldsAfter;
    }

    /// <summary>
    /// 杠和听牌：摸到后可把若干手牌并入明牌并立即和牌（仅自摸）。
    /// 超集比明牌多的牌中恰好一张不在手里，其余在手里，杠完剩余可成和牌型。
    /// </summary>
    public static Dictionary<int, HongqueKongWinOption> BestKongWinOptions(
        List<int> handTileIds,
        List<int[]> meldMasks,
        int? excludedTileId = null) {
        Dictionary<int, HongqueKongWinOption> bestByTile =
            new Dictionary<int, HongqueKongWinOption>();
        Dictionary<int, HongqueWinScore> bestScoreByTile =
            new Dictionary<int, HongqueWinScore>();
        if (handTileIds == null || meldMasks == null || meldMasks.Count == 0) {
            return bestByTile;
        }
        List<string> handCodes = handTileIds
            .Select(HongqueTileVisual.ToCode)
            .Where(code => code != null)
            .ToList();
        if (handCodes.Count != handTileIds.Count) return bestByTile;
        BigInteger handMask = MaskFromCodes(handCodes);
        List<string> usedCodes = new List<string>(handCodes);
        if (excludedTileId.HasValue) {
            string excludedCode = HongqueTileVisual.ToCode(excludedTileId.Value);
            if (excludedCode != null) usedCodes.Add(excludedCode);
        }
        foreach (int[] mask in meldMasks) {
            foreach (int tileId in TilesFromMeldMask(mask)) {
                string code = HongqueTileVisual.ToCode(tileId);
                if (code != null) usedCodes.Add(code);
            }
        }
        BigInteger availableMask = FullDeckMask & ~MaskFromCodes(usedCodes);

        for (int meldIndex = 0; meldIndex < meldMasks.Count; meldIndex++) {
            int[] mask = meldMasks[meldIndex];
            List<int> meldTiles = TilesFromMeldMask(mask);
            if (meldTiles.Count < 3) continue;
            List<string> meldCodes = meldTiles
                .Select(HongqueTileVisual.ToCode)
                .Where(code => code != null)
                .ToList();
            if (meldCodes.Count != meldTiles.Count) continue;
            BigInteger meldMask = MaskFromCodes(meldCodes);
            foreach ((BigInteger group, BigInteger extra) in MeldExtensions(meldMask)) {
                BigInteger missing = extra & ~handMask;
                if (BitCount(missing) != 1) continue;
                if ((missing & availableMask) == 0) continue;
                BigInteger rest = handMask ^ (extra & handMask);
                if (rest != 0 && !CanPartitionMask(rest)) continue;
                int tileId = HongqueTileVisual.FromCode(Deck[LowestBitIndex(missing)]);
                if (tileId == 0) continue;

                List<int> restTileIds = TileIdsFromMask(rest);
                List<int[]> meldsAfter = BuildMeldsAfter(meldMasks, meldIndex, group);
                HongqueWinScore score = HongqueScoring.BestWinResult(
                    restTileIds, meldsAfter, true, false, false);
                if (score == null) continue;
                bool better = !bestScoreByTile.TryGetValue(
                    tileId, out HongqueWinScore current)
                    || score.Points > current.Points
                    || (score.Points == current.Points
                        && score.FanTotal > current.FanTotal)
                    || (score.Points == current.Points
                        && score.FanTotal == current.FanTotal
                        && score.Base > current.Base);
                if (!better) continue;
                bestScoreByTile[tileId] = score;
                bestByTile[tileId] = new HongqueKongWinOption {
                    TileId = tileId,
                    HandAfterKong = restTileIds,
                    MeldsAfterKong = meldsAfter,
                    Score = score,
                };
            }
        }
        return bestByTile;
    }
}
