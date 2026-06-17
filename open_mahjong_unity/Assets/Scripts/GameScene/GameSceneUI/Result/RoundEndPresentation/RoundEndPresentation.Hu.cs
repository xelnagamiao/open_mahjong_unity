using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class RoundEndPresentation {
    /// <summary>开始和牌结果流程。</summary>
    public void PresentHuResultSequence(
        int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class,
        int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask,
        int? base_fu, string[] fu_fan_list, RiichiEndResultExtras riichiExtras, Dictionary<int, int> score_changes = null,
        bool isSilent = false, bool playPresentationEffects = true,
        bool suppressHandReveal = false, int hepaiTile = 0, bool multiRon = false,
        bool deferScoreSettlement = false, int? ronDiscarderIndex = null, bool recycleDiscard = false,
        bool isQianggang = false, bool endgameScoreOnly = false) {
        StartSequence(HuResult(
            hepai_player_index, player_to_score, hu_score, hu_fan, hu_class,
            hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask,
            base_fu, fu_fan_list, riichiExtras, score_changes, isSilent, playPresentationEffects,
            suppressHandReveal, hepaiTile, multiRon, deferScoreSettlement, ronDiscarderIndex, recycleDiscard,
            isQianggang, endgameScoreOnly));
    }

    private IEnumerator HuResult(
        int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class,
        int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask,
        int? base_fu, string[] fu_fan_list, RiichiEndResultExtras riichiExtras, Dictionary<int, int> score_changes,
        bool isSilent, bool playPresentationEffects,
        bool suppressHandReveal, int hepaiTile, bool multiRon, bool deferScoreSettlement, int? ronDiscarderIndex,
        bool recycleDiscard, bool isQianggang, bool endgameScoreOnly) {
        bool selfWon = NormalGameStateManager.Instance.indexToPosition[hepai_player_index] == "self";
        bool isSichuan = NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.IsSichuanRule();
        bool isMidGameSichuanHu = deferScoreSettlement && isSichuan && !endgameScoreOnly;
        // 终局 settle_hu：仅分数面板，不重复 3D 和牌动画（reveal_hu 已亮牌）
        bool isEndgameScoreOnly = endgameScoreOnly;
        bool willRevealWinnerHand = playPresentationEffects && !isEndgameScoreOnly && !isMidGameSichuanHu
            && hepai_player_hand != null && hepai_player_hand.Length > 0;

        if (selfWon && !isMidGameSichuanHu && !isEndgameScoreOnly) {
            HideSelfGameplayControl(!willRevealWinnerHand && !suppressHandReveal);
        }

        if (!isSilent && !isEndgameScoreOnly) {
            GameCanvas.Instance.ShowActionDisplay(NormalGameStateManager.Instance.indexToPosition[hepai_player_index], hu_class);
            SoundManager.Instance.PlayActionSound(NormalGameStateManager.Instance.indexToPosition[hepai_player_index], hu_class);
        }

        if (isMidGameSichuanHu) {
            yield return HepaiRevealDirector.PlaySichuanMidGame(
                hepai_player_index, hu_class, hepaiTile, multiRon, ronDiscarderIndex, recycleDiscard, isQianggang);
        } else if (willRevealWinnerHand) {
            yield return HepaiRevealDirector.Play(hepai_player_index, hepai_player_hand, hu_class, hu_fan);
        }

        if (deferScoreSettlement && !endgameScoreOnly) {
            yield return new WaitForSeconds(1.5f);
            activeRoundEndCoroutine = null;
            yield break;
        }

        PreparePresentationRoot(playPresentationEffects);
        EndResultPanel.Instance.PrepareShowResult(
            hepai_player_index, player_to_score, hu_score, hu_fan, hu_class,
            hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask,
            riichiExtras, score_changes, suppressHandReveal);
        yield return PlayAfterFade(
            () => EndResultPanel.Instance.PlayPreparedShowResult(hu_score, hu_fan, base_fu, fu_fan_list, riichiExtras),
            playPresentationEffects
        );
    }
}
