using System;
using System.Collections.Generic;

/// <summary>
/// 牌谱/观战和牌面板用手牌数组：与实时 show_result 的「暗手 | 副露 | 和牌张」布局对齐。
/// </summary>
public static class RecordHuHandBuilder {
    /// <summary>
    /// 从 hu_* tick 解析和牌张（服务端 player_action_record_hu 写入）。
    /// </summary>
    public static bool TryParseHepaiTile(List<string> tick, string rule, out int hepaiTile) {
        hepaiTile = 0;
        if (tick == null || tick.Count <= 5) return false;

        string r = rule?.ToLowerInvariant() ?? "";
        if (r.StartsWith("classical")) {
            if (tick.Count <= 7) return false;
            hepaiTile = ParseTickInt(tick, 7);
            return hepaiTile >= 10;
        }
        if (r.StartsWith("sichuan")) {
            hepaiTile = ParseTickInt(tick, 5);
            return hepaiTile >= 10;
        }
        hepaiTile = ParseTickInt(tick, 5);
        return hepaiTile >= 10;
    }

    /// <summary>
    /// 解析四川 hu tick 扩展字段（multi_ron / ron_discarder / recycle_discard）。
    /// </summary>
    public static void ParseSichuanHuExtras(List<string> tick, out int hepaiTile, out bool multiRon,
        out int? ronDiscarderIndex, out bool recycleDiscard) {
        hepaiTile = 0;
        multiRon = false;
        ronDiscarderIndex = null;
        recycleDiscard = false;
        if (tick == null || tick.Count <= 5) return;
        hepaiTile = ParseTickInt(tick, 5);
        if (tick.Count > 6) multiRon = ParseTickInt(tick, 6) != 0;
        if (tick.Count > 7) ronDiscarderIndex = ParseTickInt(tick, 7);
        if (tick.Count > 8) recycleDiscard = ParseTickInt(tick, 8) != 0;
    }

    /// <summary>
    /// 从 hu_* / hu_riichi tick 与推演手牌构建和牌展示数组。
    /// </summary>
    public static int[] BuildDisplayHandFromTick(
        List<string> tick,
        string rule,
        IReadOnlyList<int> closedHand,
        string huClass,
        int lastWinnableTileId) {
        TryParseHepaiTile(tick, rule, out int hepaiTile);
        if (IsFlowerWin(tick, rule)) {
            return BuildDisplayHand(closedHand, "hu_self", 0, 0);
        }
        return BuildDisplayHand(closedHand, huClass, hepaiTile, lastWinnableTileId);
    }

    /// <summary>和牌触发牌为花牌时，按花胡处理且不将触发花加入暗手。</summary>
    public static bool IsFlowerWin(List<string> tick, string rule) {
        return TryParseHepaiTile(tick, rule, out int tile) && tile >= 51 && tile <= 58;
    }

    /// <summary>
    /// 构建和牌面板用手牌：荣和时按手牌结构在末尾追加和牌张。
    /// </summary>
    public static int[] BuildDisplayHand(
        IReadOnlyList<int> closedHand,
        string huClass,
        int hepaiTile,
        int lastWinnableTileId) {
        if (closedHand == null || closedHand.Count == 0) return Array.Empty<int>();
        int[] hand = new int[closedHand.Count];
        for (int i = 0; i < closedHand.Count; i++) hand[i] = closedHand[i];

        if (huClass == "hu_self") return hand;

        int winTile = hepaiTile >= 10 ? hepaiTile : (lastWinnableTileId >= 10 ? lastWinnableTileId : 0);
        if (winTile <= 10) return hand;

        if (!NeedsRonTile(hand)) {
            return hand;
        }

        int[] extended = new int[hand.Length + 1];
        Array.Copy(hand, extended, hand.Length);
        extended[hand.Length] = winTile;
        return extended;
    }

    /// <summary>荣和前暗手张数恒为 3n+1；写入和牌张后为 3n+2。</summary>
    public static bool NeedsRonTile(IReadOnlyList<int> hand) {
        return hand != null && hand.Count % 3 == 1;
    }

    private static int ParseTickInt(IReadOnlyList<string> tick, int index) {
        if (tick == null || index < 0 || index >= tick.Count || string.IsNullOrEmpty(tick[index])) return 0;
        return int.TryParse(tick[index].Trim(), out int value) ? value : 0;
    }
}
