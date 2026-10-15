using System.Collections.Generic;

/// <summary>长沙听牌与和牌张提示：按房间小胡/大胡分值折算分数，标签带第一项番名。</summary>
internal static class ChangshaTips {
    public static HashSet<int> Tingpai(TingpaiQuery q) {
        if (q.SubRule == ChangshaGameState.ExchangeSixSubRule) return ChangshaExchangeSixTips.Waiting(q);
        return ChangshaExternal.TingpaiCheck(q.Hand, q.Melds ?? new List<string>());
    }

    public static WaitTileHint Describe(WaitHintQuery q) {
        if (q.SubRule == ChangshaGameState.ExchangeSixSubRule) return ChangshaExchangeSixTips.Describe(q);
        var result = ChangshaExternal.HepaiCheck(q.HandWithWin, q.Melds, new List<string>(), q.HepaiTile, false);
        ChangshaGameState state = ChangshaGameState.Active;
        int score = ChangshaExternal.BaseFromFans(
            result.Item2,
            false,
            state != null ? state.SmallHuScore : 2,
            state != null ? state.BigHuScore : 8,
            state != null && state.BaseScoreNoDealer);
        string label = score > 0 ? $"{score}分" : "无番";
        if (result.Item2 != null && result.Item2.Count > 0) {
            label = $"{result.Item2[0]} {label}";
        }
        bool smallHuOnly = result.Item2 != null && result.Item2.Count == 1 && result.Item2[0] == "小胡";
        if (score > 0 && smallHuOnly && state != null && state.RuleSettings.small_hu_self_draw_only) {
            return WaitTileHint.TsumoOnly(label + " 仅自摸");
        }
        return score > 0 ? WaitTileHint.Ron(label) : WaitTileHint.None(label);
    }
}
