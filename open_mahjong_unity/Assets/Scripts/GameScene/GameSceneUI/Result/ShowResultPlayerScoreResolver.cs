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

    public static void ResolveScoreChange(int beforeScore, int seatIndex, int originalPlayerIndex,
        Dictionary<int, int> scoreChanges, Dictionary<int, int> playerToScore, out int afterScore) {
        if (TryGetDelta(scoreChanges, seatIndex, originalPlayerIndex, out int delta)) {
            afterScore = beforeScore + delta;
            return;
        }
        if (TryGetAfterScore(playerToScore, seatIndex, originalPlayerIndex, out afterScore)) {
            return;
        }
        afterScore = beforeScore;
    }
}
