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

        int tickIndex = RuleRegistry.Resolve(rule, rule)?.RecordHuTileTickIndex ?? 5;
        if (tick.Count <= tickIndex) return false;
        hepaiTile = ParseTickInt(tick, tickIndex);
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
    /// 调用方必须明确告知荣和张是否已写入推演手牌，不能由张数或牌面推断。
    /// </summary>
    public static int[] BuildDisplayHandFromTick(
        List<string> tick,
        string rule,
        IReadOnlyList<int> closedHand,
        string huClass,
        int lastWinnableTileId,
        bool hasRonWinningTile) {
        TryParseHepaiTile(tick, rule, out int hepaiTile);
        if (IsFlowerWin(tick, rule)) {
            return BuildDisplayHand(closedHand, "hu_self", 0, 0, hasRonWinningTile: false);
        }
        return BuildDisplayHand(closedHand, huClass, hepaiTile, lastWinnableTileId, hasRonWinningTile);
    }

    /// <summary>和牌触发牌为花牌时，按花胡处理且不将触发花加入暗手。</summary>
    public static bool IsFlowerWin(List<string> tick, string rule) {
        // 广东的花鬼是闭手实体牌；胡到鬼也不是独立花胡。
        if (rule == "vietnamese" || (rule?.StartsWith("vietnamese/", StringComparison.Ordinal) ?? false)) return false;
        if (rule == "guangdong" || (rule?.StartsWith("guangdong/", StringComparison.Ordinal) ?? false)) return false;
        return RuleRegistry.Resolve(rule, rule)?.RecordFlowerWin?.Invoke(tick) == true
            || (TryParseHepaiTile(tick, rule, out int tile) && tile >= 51 && tile <= 58);
    }

    /// <summary>
    /// 返回完整和牌展示快照，末项为和牌张，不修改输入。
    /// 自摸的摸牌已在手牌末尾；荣和只在推演状态尚未接收时追加一次。
    /// 变长牌组、空暗手及同牌复数均由这个显式状态处理，不依赖张数取模。
    /// </summary>
    public static int[] BuildDisplayHand(
        IReadOnlyList<int> closedHand,
        string huClass,
        int hepaiTile,
        int lastWinnableTileId,
        bool hasRonWinningTile) {
        if (closedHand == null) return Array.Empty<int>();
        int[] hand = new int[closedHand.Count];
        for (int i = 0; i < closedHand.Count; i++) hand[i] = closedHand[i];

        if (huClass == "hu_self" || hasRonWinningTile) return hand;

        int winTile = hepaiTile >= 10 ? hepaiTile : (lastWinnableTileId >= 10 ? lastWinnableTileId : 0);
        if (winTile < 10) return hand;

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
