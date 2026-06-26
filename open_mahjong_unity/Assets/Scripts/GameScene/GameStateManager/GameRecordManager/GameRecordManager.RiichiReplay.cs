using System;
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Linq;

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

    /// <summary>立直牌谱起手分（按 original 0~3）。非立直规则返回全 0。</summary>
    private int[] GetRecordStartingScoresByOriginal() {
        var scores = new int[4];
        if (!IsRiichiRuleRecord() || gameRecord?.gameTitle == null) return scores;
        var gt = gameRecord.gameTitle;
        if (gt.TryGetValue("starting_scores", out object arrObj)) {
            if (arrObj is JArray arr && arr.Count >= 4) {
                for (int i = 0; i < 4; i++) scores[i] = Convert.ToInt32(arr[i]);
                return scores;
            }
            if (arrObj is IList<object> list && list.Count >= 4) {
                for (int i = 0; i < 4; i++) scores[i] = Convert.ToInt32(list[i]);
                return scores;
            }
        }
        int uniform = ReadRecordStartingScoreUniform(gt);
        for (int i = 0; i < 4; i++) scores[i] = uniform;
        return scores;
    }

    /// <summary>立直牌谱统一起手分；旧牌谱无字段时按子规则推断（标准 25000 / 浪涌 50000）。</summary>
    private static int ReadRecordStartingScoreUniform(Dictionary<string, object> gt) {
        int explicitScore = ReadGameTitleInt(gt, "starting_score", -1);
        if (explicitScore >= 0) return explicitScore;
        string subRule = ReadGameTitleString(gt, "sub_rule", "");
        if (subRule == "riichi/langyong") return 50000;
        string rule = ReadGameTitleString(gt, "rule", "").ToLowerInvariant();
        if (rule == "riichi" || rule.StartsWith("riichi/") || subRule.StartsWith("riichi")) return 25000;
        return 0;
    }

    /// <summary>截至指定局开始前各 original 座位的累计分（含起手分）。</summary>
    private int[] BuildCumulativeScoresBeforeRound(int roundIndex) {
        int[] cumulativeByOrig = GetRecordStartingScoresByOriginal();
        if (gameRecord?.gameRound?.rounds == null) return cumulativeByOrig;
        for (int r = 1; r < roundIndex; r++) {
            if (gameRecord.gameRound.rounds.TryGetValue(r, out Round prevRound) &&
                prevRound.scoreChanges != null && prevRound.scoreChanges.Count >= 4) {
                for (int p = 0; p < 4; p++) cumulativeByOrig[p] += prevRound.scoreChanges[p];
            }
        }
        return cumulativeByOrig;
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
