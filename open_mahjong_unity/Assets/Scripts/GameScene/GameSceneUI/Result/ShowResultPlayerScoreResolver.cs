using System.Collections.Generic;

/// <summary>
/// 结算分数解析：各规则 broadcast 的 player_to_score / score_changes 键可能是当局座位 index 或 original_player_index。
/// </summary>
public static class ShowResultPlayerScoreResolver {
    public static bool TryGetAfterScore(Dictionary<int, int> playerToScore, int seatIndex, int originalPlayerIndex, out int score) {
        score = 0;
        if (playerToScore == null) return false;
        if (playerToScore.TryGetValue(seatIndex, out score)) return true;
        if (playerToScore.TryGetValue(originalPlayerIndex, out score)) return true;
        return false;
    }

    public static bool TryGetDelta(Dictionary<int, int> scoreChanges, int seatIndex, int originalPlayerIndex, out int delta) {
        delta = 0;
        if (scoreChanges == null) return false;
        if (scoreChanges.TryGetValue(originalPlayerIndex, out delta)) return true;
        if (scoreChanges.TryGetValue(seatIndex, out delta)) return true;
        return false;
    }

    /// <summary>
    /// 解析结算前后分。优先用 player_to_score 绝对分；
    /// 若本地已在追加 score_history 时同步加过 delta，用 after - delta 还原局前分，避免重复加算。
    /// </summary>
    public static void ResolveBeforeAfter(int currentScore, int seatIndex, int originalPlayerIndex,
        Dictionary<int, int> scoreChanges, Dictionary<int, int> playerToScore,
        out int beforeScore, out int afterScore) {
        bool hasDelta = TryGetDelta(scoreChanges, seatIndex, originalPlayerIndex, out int delta);
        if (TryGetAfterScore(playerToScore, seatIndex, originalPlayerIndex, out afterScore)) {
            beforeScore = hasDelta ? afterScore - delta : currentScore;
            return;
        }
        if (hasDelta) {
            // 无绝对分时：默认 current 已含本笔 delta（与 ApplyLocalScoreHistoryFromSettlement 对齐）
            afterScore = currentScore;
            beforeScore = currentScore - delta;
            return;
        }
        beforeScore = currentScore;
        afterScore = currentScore;
    }

    public static void ResolveScoreChange(int beforeScore, int seatIndex, int originalPlayerIndex,
        Dictionary<int, int> scoreChanges, Dictionary<int, int> playerToScore, out int afterScore) {
        ResolveBeforeAfter(beforeScore, seatIndex, originalPlayerIndex, scoreChanges, playerToScore,
            out _, out afterScore);
    }
}
