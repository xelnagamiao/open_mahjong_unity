using System.Collections.Generic;

/// <summary>
/// 立直麻将牌谱回放：场况（宝牌指示牌、场供立直棒）在跳转/切局时的推演与 UI 同步。
/// </summary>
public partial class GameRecordManager {
    /// <summary>当前推演节点对应的场供立直棒数（开局值 + 宣告立直 - 和牌收走）。</summary>
    private int recordRiichiSticks;

    /// <summary>是否已在本局推演中经过「和牌收走场供」节点（用于 3D 立直棒与场供计数一致）。</summary>
    private bool recordRiichiTenbousClearedAfterHu;

    private bool IsRiichiRuleRecord() {
        if (gameRecord?.gameTitle == null) return false;
        string rule = ReadGameTitleString(gameRecord.gameTitle, "rule", "").ToLowerInvariant();
        if (rule == "riichi" || rule.StartsWith("riichi/")) return true;
        string subRule = ReadGameTitleString(gameRecord.gameTitle, "sub_rule", "").ToLowerInvariant();
        return subRule.StartsWith("riichi");
    }

    /// <summary>
    /// 将宝牌/场供重置为本局开局状态：首张宝牌由牌山倒数第 6 张推导，场供取自 round.riichi 元数据。
    /// </summary>
    private void ResetRecordRiichiFieldState(Round roundData) {
        recordRiichiDoraIndicators.Clear();
        if (roundData?.tilesList != null && roundData.tilesList.Count >= 6) {
            recordRiichiDoraIndicators.Add(roundData.tilesList[roundData.tilesList.Count - 6]);
        }
        recordRiichiSticks = roundData?.riichi?.riichiSticks ?? 0;
        recordRiichiTenbousClearedAfterHu = false;
    }

    private void ApplyRecordRiichiDoraTick(int doraTile) {
        recordRiichiDoraIndicators.Add(doraTile);
    }

    private void ApplyRecordRiichiDeclare() {
        recordRiichiSticks++;
    }

    private void ApplyRecordRiichiSticksCollected(int collected) {
        if (collected > 0) {
            recordRiichiSticks = 0;
            recordRiichiTenbousClearedAfterHu = true;
        }
    }

    /// <summary>将推演后的宝牌/本场/场供同步到 RoundPanel。</summary>
    private void RefreshRecordRiichiRoundPanel() {
        if (!IsRiichiRuleRecord() || RoundPanel.Instance == null) return;
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) return;

        int honba = roundData.riichi?.honba ?? 0;
        var initialDora = new List<int>();
        var kanDora = new List<int>();
        if (recordRiichiDoraIndicators.Count > 0) {
            initialDora.Add(recordRiichiDoraIndicators[0]);
            for (int i = 1; i < recordRiichiDoraIndicators.Count; i++) {
                kanDora.Add(recordRiichiDoraIndicators[i]);
            }
        }
        RoundPanel.Instance.RefreshRiichi(honba, recordRiichiSticks, initialDora, kanDora);
    }
}
