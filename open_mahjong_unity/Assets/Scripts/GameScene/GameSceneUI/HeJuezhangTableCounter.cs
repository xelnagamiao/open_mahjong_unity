using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 和绝张：统计场内（河牌）+ 副露 + 预测切牌 上已出现的和牌张数量。
/// 客户端听牌提示预判「点和/自摸」时和牌张尚未上桌，show==3 即和绝张（等价于 server 荣和校验时 show==4）。
/// </summary>
public static class HeJuezhangTableCounter {
    /// <summary>
    /// 场内可见张数：各家河牌 + 副露 + 可选预测切牌（悬停预览）。
    /// </summary>
    /// <param name="strictCombinationMatch">
    /// 牌谱/回放用整 key 匹配，避免 Contains 子串误伤；正常对局沿用 Contains 与服务端 in 一致。
    /// </param>
    public static int CountShowTilesOnTable(
        int hepaiTile,
        IEnumerable<IReadOnlyList<int>> discardLists,
        IReadOnlyList<string> combinations,
        int? pendingCutTileId = null,
        bool strictCombinationMatch = false) {
        int count = 0;

        if (discardLists != null) {
            foreach (IReadOnlyList<int> discards in discardLists) {
                if (discards == null) continue;
                foreach (int tile in discards) {
                    if (tile == hepaiTile) count++;
                }
            }
        }

        count += CountInCombinations(hepaiTile, combinations, strictCombinationMatch);

        if (pendingCutTileId.HasValue && pendingCutTileId.Value == hepaiTile) {
            count++;
        }

        return count;
    }

    /// <summary>
    /// 听牌提示：场内已见 3 张时，待和牌为第 4 张 → 和绝张。
    /// </summary>
    public static bool ShouldAddHeJuezhangForTips(int showTilesCount) => showTilesCount == 3;

    private static int CountInCombinations(int hepaiTile, IReadOnlyList<string> combinations, bool strictCombinationMatch) {
        if (combinations == null || combinations.Count == 0) return 0;

        int count = 0;
        var comboList = combinations as IList<string> ?? combinations.ToList();

        if (strictCombinationMatch) {
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
        } else {
            foreach (string combination in comboList) {
                if (combination.Contains($"k{hepaiTile}")) {
                    count += 3;
                }
                if (combination.Contains($"s{hepaiTile - 1}")) {
                    count += 1;
                }
                if (combination.Contains($"s{hepaiTile}")) {
                    count += 1;
                }
                if (combination.Contains($"s{hepaiTile + 1}")) {
                    count += 1;
                }
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
