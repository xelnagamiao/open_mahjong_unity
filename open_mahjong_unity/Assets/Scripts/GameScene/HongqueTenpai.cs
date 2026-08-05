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
        HashSet<int> result = new HashSet<int>();
        if (fullHandTileIds == null || fullHandTileIds.Count == 0 || discardTileId == 0) return result;
        string[] handCodes = fullHandTileIds
            .Select(HongqueTileVisual.ToCode)
            .Where(code => code != null)
            .ToArray();
        if (handCodes.Length != fullHandTileIds.Count) return result;
        string discardCode = HongqueTileVisual.ToCode(discardTileId);
        if (discardCode == null) return result;

        List<string> usedCodes = new List<string>(handCodes);
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
        BigInteger usedMask = MaskFromCodes(usedCodes);
        Dictionary<BigInteger, BigInteger> waitsByDiscard = WaitingMasksAfterDiscards(
            handMask, usedMask, meldMasks != null && meldMasks.Count > 0);

        BigInteger discardBit = BigInteger.One << TileIndex[discardCode];
        if (!waitsByDiscard.TryGetValue(discardBit, out BigInteger waitMask) || waitMask == 0) return result;
        BigInteger bits = waitMask;
        while (bits != 0) {
            BigInteger bit = bits & -bits;
            int tileId = HongqueTileVisual.FromCode(Deck[LowestBitIndex(bit)]);
            if (tileId != 0) result.Add(tileId);
            bits ^= bit;
        }
        return result;
    }

    /// <summary>
    /// 杠和最优听牌：返回“摸到后可直接杠并立即和牌”的每张听牌及其杠后状态。
    /// 枚举方式与服务端 kong_win_candidates 一致：对每副明牌扫描包含它的合法组
    /// 超集，超集新增牌中恰好只有一张不在手牌里，且杠完剩余手牌可构成和牌型
    /// （剩余可为空）。按杠后计分（自摸口径）为每张听牌保留最优杠法。
    /// </summary>
    /// <param name="handTileIds">切牌后的手牌（已不含悬停切掉的牌）。</param>
    /// <param name="meldMasks">自家副露掩码（[flag, tileId] 对）。</param>
    /// <param name="excludedTileId">已弃/在场不可再摸的牌（如悬停切牌预览的弃牌）。</param>
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
            if (mask == null) continue;
            for (int i = 1; i < mask.Length; i += 2) {
                string code = HongqueTileVisual.ToCode(mask[i]);
                if (code != null) usedCodes.Add(code);
            }
        }
        BigInteger availableMask = FullDeckMask & ~MaskFromCodes(usedCodes);

        foreach (int[] mask in meldMasks) {
            if (mask == null || mask.Length < 2) continue;
            List<string> meldCodes = new List<string>();
            for (int i = 1; i < mask.Length; i += 2) {
                string code = HongqueTileVisual.ToCode(mask[i]);
                if (code != null) meldCodes.Add(code);
            }
            if (meldCodes.Count < 3) continue;
            BigInteger meldMask = MaskFromCodes(meldCodes);
            BigInteger anchorBit = meldMask & -meldMask;
            int anchorIndex = LowestBitIndex(anchorBit);
            foreach (BigInteger group in GroupsByTile[anchorIndex]) {
                if ((group & meldMask) != meldMask) continue;
                BigInteger extra = group ^ meldMask;
                if (extra == 0) continue;
                BigInteger missing = extra & ~handMask;
                if (BitCount(missing) != 1) continue;
                if ((missing & availableMask) == 0) continue;
                BigInteger rest = handMask ^ (extra & handMask);
                if (rest != 0 && !CanPartitionMask(rest)) continue;
                int tileId = HongqueTileVisual.FromCode(Deck[LowestBitIndex(missing)]);
                if (tileId == 0) continue;

                // 构建杠后状态：剩余手牌 + 扩展后的副露掩码（flag 统一 0，计分只读牌 id）。
                List<int> restTileIds = TileIdsFromMask(rest);
                List<int[]> meldsAfter = new List<int[]>();
                foreach (int[] original in meldMasks) {
                    if (original == null) continue;
                    if (original != mask) {
                        meldsAfter.Add(original);
                        continue;
                    }
                    List<int> extended = new List<int>();
                    BigInteger bits = group;
                    while (bits != 0) {
                        BigInteger bit = bits & -bits;
                        int id = HongqueTileVisual.FromCode(Deck[LowestBitIndex(bit)]);
                        if (id != 0) extended.Add(id);
                        bits ^= bit;
                    }
                    int[] extendedMask = new int[extended.Count * 2];
                    for (int j = 0; j < extended.Count; j++) {
                        extendedMask[j * 2] = 0;
                        extendedMask[j * 2 + 1] = extended[j];
                    }
                    meldsAfter.Add(extendedMask);
                }

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
