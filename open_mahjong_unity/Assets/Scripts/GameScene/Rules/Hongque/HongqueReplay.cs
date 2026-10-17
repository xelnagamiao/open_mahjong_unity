using System.Collections.Generic;
using System.Linq;

public partial class GameRecordManager {
    internal RuleReplayModule CreateHongqueReplayModule() => new RuleReplayModule("hongque") {
        InitializePlayers = () => {
            int dealer = gameRecord.gameRound.rounds[currentRoundIndex].startPlayerIndex;
            foreach (var player in recordPlayerList)
                player.showHandDrawSlotActive = player.playerIndex == dealer && player.tileList.Count > 0;
        },
    };
}

/// <summary>牌谱沿用实战本地计分，按实际摸牌槽和完整副露处理变长组合。</summary>
internal static class HongqueRecordTips {
    // 四家听牌、手牌危险标记和牌山提示查询的是同一状态。一次枚举后共享结果，
    // 不为每张待牌的说明重新扫描全部合法组。键包含完整副露，不受手牌排列影响。
    private static readonly Dictionary<string, Dictionary<int, HongqueScoreHintInfo>> HintCache =
        new Dictionary<string, Dictionary<int, HongqueScoreHintInfo>>();
    private static Dictionary<int, HongqueScoreHintInfo> Hints(List<int> hand, List<int[]> melds) {
        string key = string.Join(",", hand.OrderBy(tile => tile)) + "|"
            + string.Join("/", (melds ?? new List<int[]>()).Select(mask =>
                mask == null ? "" : string.Join(",", mask)));
        if (HintCache.TryGetValue(key, out var cached)) return cached;
        var result = HongqueTenpai.BuildScoreHints(hand, melds)
            .ToDictionary(hint => HongqueTileVisual.FromCode(hint.tile));
        if (HintCache.Count >= 256) HintCache.Clear();
        HintCache[key] = result;
        return result;
    }
    internal static List<int> HandBeforeDraw(GameRecordManager.RecordPlayer player) {
        if (player?.tileList == null || player.isHu || player.hasRonWinningTile) return null;
        var manager = GameRecordManager.Instance;
        // 吃/碰后仍须先切牌，此时没有可当作摸牌去掉的末张手牌。
        if (!player.showHandDrawSlotActive && manager != null
            && manager.recordPlayer_to_info.Values.Contains(player) && manager.currentPlayerIndex == player.playerIndex
            && !manager.IsWaitingForDrawAfterCutForChongHint) return null;
        var hand = new List<int>(player.tileList);
        if (player.showHandDrawSlotActive && hand.Count > 0) hand.RemoveAt(hand.Count - 1);
        return hand;
    }

    internal static HashSet<int> Waiting(TingpaiQuery query) => query?.Hand == null
        ? new HashSet<int>() : new HashSet<int>(Hints(query.Hand, query.MeldMasks).Keys);

    internal static WaitTileHint Describe(WaitHintQuery query) {
        if (query?.HandWithWin == null) return null;
        var hand = new List<int>(query.HandWithWin);
        if (!hand.Remove(query.HepaiTile)) return null;
        if (!Hints(hand, query.MeldMasks).TryGetValue(query.HepaiTile, out var hint)) return null;
        string label = hint.points + "点";
        return hint.self_draw_only ? WaitTileHint.TsumoOnly(label) : WaitTileHint.Ron(label);
    }

    internal static IEnumerable<int> VisibleMeldTiles(List<int[]> melds) {
        if (melds == null) yield break;
        foreach (int[] mask in melds)
            for (int i = 1; mask != null && i < mask.Length; i += 2) yield return mask[i];
    }
}
