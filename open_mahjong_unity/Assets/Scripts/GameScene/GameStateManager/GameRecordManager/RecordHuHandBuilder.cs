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
    /// 构建和牌面板用手牌：荣和时在末尾追加和牌张（即使手牌中已有同 id，末张也可能是刚摸未切牌）。
    /// </summary>
    /// <param name="trimDrawSlotForRon">
    /// 观战专用：牌谱回放 tileList 仍含摸牌区未切牌时，荣和展示前先去掉末张摸牌位再追加河牌和牌张，
    /// 避免 14+1=15 张；普通牌谱阅览勿开启。
    /// </param>
    public static int[] BuildDisplayHand(
        IReadOnlyList<int> closedHand,
        string huClass,
        int hepaiTile,
        int lastWinnableTileId,
        bool trimDrawSlotForRon = false) {
        if (closedHand == null || closedHand.Count == 0) return Array.Empty<int>();
        int[] hand = new int[closedHand.Count];
        for (int i = 0; i < closedHand.Count; i++) hand[i] = closedHand[i];

        if (huClass == "hu_self") return hand;

        int winTile = hepaiTile >= 10 ? hepaiTile : (lastWinnableTileId >= 10 ? lastWinnableTileId : 0);
        if (winTile <= 10) return hand;

        if (trimDrawSlotForRon && hand.Length % 3 == 2) {
            int[] trimmed = new int[hand.Length - 1];
            Array.Copy(hand, trimmed, trimmed.Length);
            hand = trimmed;
        }

        int[] extended = new int[hand.Length + 1];
        Array.Copy(hand, extended, hand.Length);
        extended[hand.Length] = winTile;
        return extended;
    }

    private static int ParseTickInt(IReadOnlyList<string> tick, int index) {
        if (tick == null || index < 0 || index >= tick.Count || string.IsNullOrEmpty(tick[index])) return 0;
        return int.TryParse(tick[index].Trim(), out int value) ? value : 0;
    }
}
