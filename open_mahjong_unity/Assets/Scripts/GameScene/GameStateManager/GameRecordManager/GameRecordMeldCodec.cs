using System.Collections.Generic;
using System.Linq;
using Riichi;

/// <summary>
/// 吃碰杠组合掩码编解码与手牌删牌工具（对局 / 牌谱 / 四规则共用）。
/// 掩码格式：[flag, tileId, flag, tileId, ...]
/// flag=0 手牌侧 / flag=1 河牌来源 / flag=2 暗杠 / flag=3 加杠
/// </summary>
public static class GameRecordMeldCodec {
    public static int HandTileCount(string recordAction) => recordAction == "g" ? 3 : 2;
    public const int AngangHandTileCount = 4;

    /// <summary>
    /// 暗杠 tick 解析并删牌：新格式 [ag, norm, T|F, id0..id3] 用真实 ID；
    /// 旧格式（仅 3 段）回退为归一化模拟删牌。
    /// </summary>
    public static List<int> ResolveAngangRemovedTiles(
        IReadOnlyList<string> tick,
        List<int> tileList,
        int angangTileId,
        bool isMoGang) {
        if (tick != null && tick.Count >= 3 + AngangHandTileCount) {
            var stored = new List<int>(AngangHandTileCount);
            for (int i = 3; i < 3 + AngangHandTileCount; i++) {
                stored.Add(ParseTickInt(tick, i));
            }
            RemoveExactTilesFromList(tileList, stored);
            return stored;
        }
        return RemoveNTilesByNormalized(tileList, angangTileId, AngangHandTileCount, preferDrawSlotFirst: isMoGang);
    }

    public static void RemoveExactTilesFromList(List<int> tileList, IReadOnlyList<int> tileIds) {
        if (tileList == null || tileIds == null) return;
        foreach (int tid in tileIds) {
            int idx = tileList.IndexOf(tid);
            if (idx >= 0) tileList.RemoveAt(idx);
        }
    }

    /// <summary>从掩码提取须从手牌删除的真实牌 ID（跳过 flag=1 的河牌来源位）。</summary>
    public static List<int> ExtractHandTilesFromMask(int[] mask) {
        var handTiles = new List<int>();
        if (mask == null) return handTiles;
        for (int i = 0; i + 1 < mask.Length; i += 2) {
            if (mask[i] == 1) continue;
            int tid = mask[i + 1];
            if (tid > 10) handTiles.Add(tid);
        }
        return handTiles;
    }

    /// <summary>取掩码中首个指定 flag 对应的 tileId；未找到返回 null。</summary>
    public static int? ExtractTileByFlag(int[] mask, int flag) {
        if (mask == null) return null;
        for (int i = 0; i + 1 < mask.Length; i += 2) {
            if (mask[i] == flag && mask[i + 1] > 10) return mask[i + 1];
        }
        return null;
    }

    /// <summary>按归一化点数统计手牌张数（日麻赤 5 与普通 5 合并计数）。</summary>
    public static int CountNormalizedTiles(IEnumerable<int> handTiles, int normalizedTile) {
        int norm = RiichiTileUtil.Normalize(normalizedTile);
        int count = 0;
        foreach (int t in handTiles) {
            if (RiichiTileUtil.Normalize(t) == norm) count++;
        }
        return count;
    }

    /// <summary>
    /// 从手牌列表移除 count 张归一化值为 normalizedTile 的牌，返回被移除的真实 ID 列表。
    /// preferDrawSlotFirst：摸杠时优先移除末张（与服务器 remove_angang_tiles / resolve_is_mo_gang 一致）。
    /// 删除顺序：先精确 ID，再赤宝归一化匹配（与服务器 _remove_by_normal 一致）。
    /// </summary>
    public static List<int> RemoveNTilesByNormalized(
        List<int> tileList,
        int normalizedTile,
        int count,
        bool preferDrawSlotFirst = false) {
        var removed = new List<int>();
        if (tileList == null || count <= 0) return removed;
        int norm = RiichiTileUtil.Normalize(normalizedTile);

        if (preferDrawSlotFirst && tileList.Count > 0
            && RiichiTileUtil.Normalize(tileList[tileList.Count - 1]) == norm) {
            removed.Add(tileList[tileList.Count - 1]);
            tileList.RemoveAt(tileList.Count - 1);
        }

        while (removed.Count < count) {
            int idx = FindRemoveIndex(tileList, norm, preferExact: true);
            if (idx < 0) idx = FindRemoveIndex(tileList, norm, preferExact: false);
            if (idx < 0) break;
            removed.Add(tileList[idx]);
            tileList.RemoveAt(idx);
        }
        return removed;
    }

    /// <summary>用实际移除的 4 张牌构建暗杠掩码（含赤宝真实 ID）。</summary>
    public static int[] BuildAngangMaskFromRemoved(IReadOnlyList<int> removedTiles, string rule) {
        int r0 = removedTiles != null && removedTiles.Count > 0 ? removedTiles[0] : 0;
        int r1 = removedTiles != null && removedTiles.Count > 1 ? removedTiles[1] : r0;
        int r2 = removedTiles != null && removedTiles.Count > 2 ? removedTiles[2] : r0;
        int r3 = removedTiles != null && removedTiles.Count > 3 ? removedTiles[3] : r0;
        bool isRiichi = !string.IsNullOrEmpty(rule)
            && (rule == "riichi" || rule.StartsWith("riichi"));
        if (isRiichi) {
            return new[] { 2, r0, 0, r1, 0, r2, 2, r3 };
        }
        return new[] { 2, r0, 2, r1, 2, r2, 2, r3 };
    }

    /// <summary>
    /// 从 tick 解析手牌侧真实 ID；旧牌谱（仅 3 段）回退为 tileId±1 算术推导。
    /// 格式：吃/碰 [code, mingpai, player, h1, h2]；杠 [code, mingpai, player, h1, h2, h3]
    /// </summary>
    public static List<int> ResolveHandTiles(IReadOnlyList<string> tick, string recordAction, int mingpaiTileId) {
        int expected = HandTileCount(recordAction);
        if (tick != null && tick.Count >= 3 + expected) {
            var stored = new List<int>(expected);
            for (int i = 3; i < 3 + expected; i++) {
                stored.Add(ParseTickInt(tick, i));
            }
            return stored;
        }
        return BuildHandTilesFallback(recordAction, mingpaiTileId);
    }

    public static string BuildCombinationTarget(string recordAction, int mingpaiTileId) {
        int norm = RiichiTileUtil.Normalize(mingpaiTileId);
        if (recordAction == "cl") return $"s{norm - 1}";
        if (recordAction == "cm") return $"s{norm}";
        if (recordAction == "cr") return $"s{norm + 1}";
        if (recordAction == "p") return $"k{norm}";
        return $"g{norm}";
    }

    public static int[] BuildMingpaiMask(string recordAction, int mingpaiTileId,
        IReadOnlyList<int> handTiles, string relativeToDiscardPlayer) {
        if (recordAction == "cl" || recordAction == "cm" || recordAction == "cr") {
            return new[] { 1, mingpaiTileId, 0, handTiles[0], 0, handTiles[1] };
        }

        int r1 = handTiles[0];
        int r2 = handTiles[1];
        if (recordAction == "p") {
            if (relativeToDiscardPlayer == "left") return new[] { 1, mingpaiTileId, 0, r1, 0, r2 };
            if (relativeToDiscardPlayer == "right") return new[] { 0, r1, 0, r2, 1, mingpaiTileId };
            return new[] { 0, r1, 1, mingpaiTileId, 0, r2 };
        }

        int r3 = handTiles[2];
        if (relativeToDiscardPlayer == "left") {
            return new[] { 1, mingpaiTileId, 0, r1, 0, r2, 0, r3 };
        }
        if (relativeToDiscardPlayer == "right") {
            return new[] { 0, r1, 0, r2, 0, r3, 1, mingpaiTileId };
        }
        return new[] { 0, r1, 1, mingpaiTileId, 0, r2, 0, r3 };
    }

    private static List<int> BuildHandTilesFallback(string recordAction, int tileId) {
        int norm = RiichiTileUtil.Normalize(tileId);
        if (recordAction == "cl") return new List<int> { norm - 1, norm - 2 };
        if (recordAction == "cm") return new List<int> { norm - 1, norm + 1 };
        if (recordAction == "cr") return new List<int> { norm + 1, norm + 2 };
        if (recordAction == "p") return new List<int> { norm, norm };
        return new List<int> { norm, norm, norm };
    }

    private static int FindRemoveIndex(List<int> tileList, int norm, bool preferExact) {
        if (preferExact) {
            for (int i = 0; i < tileList.Count; i++) {
                if (tileList[i] == norm) return i;
            }
            return -1;
        }
        for (int i = 0; i < tileList.Count; i++) {
            if (RiichiTileUtil.Normalize(tileList[i]) == norm) return i;
        }
        return -1;
    }

    private static int ParseTickInt(IReadOnlyList<string> tick, int index) {
        if (tick == null || index < 0 || index >= tick.Count || string.IsNullOrEmpty(tick[index])) {
            return 0;
        }
        return int.TryParse(tick[index].Trim(), out int value) ? value : 0;
    }
}
