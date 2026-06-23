using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 和绝张：统计场内（河牌）+ 副露 + 预测切牌 上已出现的和牌张数量。
/// 与 server/gamestate/game_guobiao/action_check.py 中 tips 计算一致（副露用整 key 匹配，避免 Contains 子串误伤）。
/// </summary>
public static class HeJuezhangTableCounter {
    /// <summary>
    /// 场内可见张数：各家河牌 + 副露 + 可选预测切牌（悬停预览）。
    /// </summary>
    public static int CountShowTilesOnTable(
        int hepaiTile,
        IEnumerable<IReadOnlyList<int>> discardLists,
        IReadOnlyList<string> combinations,
        int? pendingCutTileId = null) {
        int count = 0;

        if (discardLists != null) {
            foreach (IReadOnlyList<int> discards in discardLists) {
                if (discards == null) continue;
                foreach (int tile in discards) {
                    if (tile == hepaiTile) count++;
                }
            }
        }

        count += CountInCombinations(hepaiTile, combinations);

        if (pendingCutTileId.HasValue && pendingCutTileId.Value == hepaiTile) {
            count++;
        }

        return count;
    }

    /// <summary>
    /// 是否计入「点和」上下文的和绝张（show == 4）。
    /// </summary>
    public static bool ShouldAddHeJuezhangForRon(int showTilesCount) => showTilesCount == 4;

    /// <summary>
    /// 是否计入「自摸」上下文的和绝张（show == 3，与 server elif 分支一致）。
    /// </summary>
    public static bool ShouldAddHeJuezhangForTsumo(int showTilesCount) => showTilesCount == 3;

    private static int CountInCombinations(int hepaiTile, IReadOnlyList<string> combinations) {
        if (combinations == null || combinations.Count == 0) return 0;

        int count = 0;
        var comboList = combinations as IList<string> ?? combinations.ToList();

        if (GameRecordMeldCodec.FindCombinationIndex(comboList, $"k{hepaiTile}") >= 0) {
            count += 3;
        }

        foreach (string combination in comboList) {
            if (MatchesSequenceKey(combination, hepaiTile - 1)
                || MatchesSequenceKey(combination, hepaiTile)
                || MatchesSequenceKey(combination, hepaiTile + 1)) {
                count += 1;
            }
        }

        return count;
    }

    /// <summary>副露顺子 key 整段匹配（s 后牌号为顺子中间张）。</summary>
    private static bool MatchesSequenceKey(string combination, int sequenceMiddleTile) {
        if (string.IsNullOrEmpty(combination) || combination.Length < 2) return false;
        if (char.ToLower(combination[0]) != 's') return false;
        return combination == $"s{sequenceMiddleTile}";
    }
}
