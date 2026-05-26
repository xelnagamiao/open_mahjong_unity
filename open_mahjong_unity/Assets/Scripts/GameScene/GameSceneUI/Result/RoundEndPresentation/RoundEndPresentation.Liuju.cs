using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class RoundEndPresentation {
    /// <summary>荒牌流局标题与听牌标记面板的停留秒数（与牌谱自动步进一致）。</summary>
    public const float DrawCaptionHoldSeconds = 2f;

    /// <summary>荒牌不听罚符在 PenaltyPanel 上的总演出秒数（三等分）。</summary>
    public const float DrawNotenPenaltyHoldSeconds = 3f;

    public void PresentLiuju(string displayText, bool playPresentationEffects = true) {
        StartSequence(CoLiuju(displayText, playPresentationEffects));
    }

    private IEnumerator CoLiuju(string displayText, bool playPresentationEffects) {
        PreparePresentationRoot(playPresentationEffects);
        EndLiujuPanel.Instance.PrepareLiujuPanel(displayText);
        yield return PlayAfterFade(
            () => EndLiujuPanel.Instance.PlayPreparedLiujuPanel(DrawCaptionHoldSeconds),
            playPresentationEffects
        );
    }

    /// <summary>荒牌流局：听牌展开手牌、流局与听牌标记、不听罚符分数变化三段演出。</summary>
    public void PresentDrawWallLiujuAndPenalty(string displayText, Dictionary<int, int> player_to_score, RiichiEndResultExtras riichiExtras, bool playPresentationEffects = true) {
        StartSequence(CoRiichiDrawWallLiujuSequence(displayText, player_to_score, riichiExtras, playPresentationEffects));
    }

    private IEnumerator CoRiichiDrawWallLiujuSequence(string displayText, Dictionary<int, int> player_to_score, RiichiEndResultExtras riichiExtras, bool playPresentationEffects) {
        HideSelfGameplayControl(false);
        Dictionary<int, int[]> tenpaiTiles = riichiExtras != null ? riichiExtras.TenpaiTiles : null;
        Dictionary<int, int[]> tenpaiHands = riichiExtras != null ? riichiExtras.TenpaiHands : null;
        if (playPresentationEffects) {
            yield return Game3DManager.Instance.RoundEndRevealTenpaiHandsAndPlayExpandAnimation(tenpaiHands);
        }

        PreparePresentationRoot(playPresentationEffects);
        EndLiujuPanel.Instance.PrepareLiujuPanel(displayText, tenpaiTiles);
        yield return PlayPresentationFade(playPresentationEffects);
        EndLiujuPanel.Instance.PlayPreparedLiujuPanel(DrawCaptionHoldSeconds);
        yield return new WaitForSeconds(DrawCaptionHoldSeconds);

        bool hasPenalty = riichiExtras != null && riichiExtras.NotenPenaltyAfterDraw;
        if (hasPenalty) {
            var usernameByPos = new Dictionary<string, string>();
            var scoresByPos = new Dictionary<string, int>();
            var deltaByPos = new Dictionary<string, int>();
            foreach (var kvp in NormalGameStateManager.Instance.indexToPosition) {
                int idx = kvp.Key;
                string pos = kvp.Value;
                usernameByPos[pos] = NormalGameStateManager.Instance.player_to_info[pos].username;
                scoresByPos[pos] = player_to_score[idx];
                deltaByPos[pos] = riichiExtras.ScoreChanges[idx];
            }
            PenaltyPanel.Instance.PreparePenaltyPanel(usernameByPos, scoresByPos, deltaByPos, PenaltyPresentation.AfterDrawNotenThreePhase);
            PenaltyPanel.Instance.PlayPreparedPenaltyPanel(usernameByPos, scoresByPos, deltaByPos, PenaltyPresentation.AfterDrawNotenThreePhase, DrawNotenPenaltyHoldSeconds);
            yield return new WaitForSeconds(DrawNotenPenaltyHoldSeconds);
        }
        activeRoundEndCoroutine = null;
    }
}
