using System.Collections;
using System.Collections.Generic;

public partial class NormalGameStateManager {
    private void ShowChangshaMidRoundHuResult(
        int playerIndex,
        Dictionary<int, int> playerToScore,
        Dictionary<int, int> scoreChanges,
        string[] huFan,
        int[] revealedTiles,
        bool isSilent) {
        ApplyShowResultScores(playerToScore);
        string actionType = ContainsChangshaHuFan(huFan, "六六顺")
            ? "mid_round_six_six"
            : "mid_round_four_joys";
        if (!isSilent && indexToPosition.TryGetValue(playerIndex, out string playerPosition)) {
            GameCanvas.Instance.ShowActionDisplay(playerPosition, actionType, roomRule);
        }
        if (GameCanvas.HasNonZeroGangScoreChanges(scoreChanges)) {
            GameCanvas.Instance.ShowGangScoreFloats(scoreChanges, 0f);
        }
        if (revealedTiles == null || revealedTiles.Length == 0
            || !indexToPosition.TryGetValue(playerIndex, out string revealPosition)) {
            return;
        }

        StartCoroutine(PlayChangshaMidRoundHuReveal(
            playerIndex,
            revealedTiles,
            huFan,
            revealPosition));
    }

    /// <summary>中途胡只亮服务端确认的四张或六张牌，演出结束后恢复当前手牌。</summary>
    private IEnumerator PlayChangshaMidRoundHuReveal(
        int playerIndex,
        int[] revealedTiles,
        string[] huFan,
        string playerPosition) {
        yield return HepaiRevealDirector.Play(playerIndex, revealedTiles, "mid_round_hu", huFan);
        Game3DManager.Instance.RestoreMidGameHandAfterCuoheRonReveal(playerPosition);
    }

    private static bool ContainsChangshaHuFan(string[] huFan, string expectedFan) {
        if (huFan == null) return false;
        foreach (string fan in huFan) {
            if (fan == expectedFan) return true;
        }
        return false;
    }
}
