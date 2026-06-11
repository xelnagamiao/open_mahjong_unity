using System.Collections.Generic;
using Riichi;

/// <summary>
/// 牌谱吃碰杠 tick 编解码：新格式在 action_player 后追加从手牌打出的真实牌 ID。
/// </summary>
public static class GameRecordMeldCodec {
    public static int HandTileCount(string recordAction) => recordAction == "g" ? 3 : 2;

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

    private static int ParseTickInt(IReadOnlyList<string> tick, int index) {
        if (tick == null || index < 0 || index >= tick.Count || string.IsNullOrEmpty(tick[index])) {
            return 0;
        }
        return int.TryParse(tick[index].Trim(), out int value) ? value : 0;
    }
}
