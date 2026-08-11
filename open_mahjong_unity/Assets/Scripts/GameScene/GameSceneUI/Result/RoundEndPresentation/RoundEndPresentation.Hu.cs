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
        bool isQianggang = false, bool endgameScoreOnly = false, bool finalPanel = true,
        Dictionary<int, int[]> simultaneousHuHands = null, bool skipHandReveal = false) {
        StartSequence(PresentHuResultSequenceCoroutine(
            hepai_player_index, player_to_score, hu_score, hu_fan, hu_class,
            hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask,
            base_fu, fu_fan_list, riichiExtras, score_changes, isSilent, playPresentationEffects,
            suppressHandReveal, hepaiTile, multiRon, deferScoreSettlement, ronDiscarderIndex, recycleDiscard,
            isQianggang, endgameScoreOnly, finalPanel, simultaneousHuHands, skipHandReveal));
    }

    /// <summary>协程版和牌结算：供虹雀多家和按顺序逐家展示，等上一家面板播完再进下家。</summary>
    public IEnumerator PresentHuResultSequenceCoroutine(
        int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class,
        int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask,
        int? base_fu, string[] fu_fan_list, RiichiEndResultExtras riichiExtras, Dictionary<int, int> score_changes = null,
        bool isSilent = false, bool playPresentationEffects = true,
        bool suppressHandReveal = false, int hepaiTile = 0, bool multiRon = false,
        bool deferScoreSettlement = false, int? ronDiscarderIndex = null, bool recycleDiscard = false,
        bool isQianggang = false, bool endgameScoreOnly = false, bool finalPanel = true,
        Dictionary<int, int[]> simultaneousHuHands = null, bool skipHandReveal = false) {
        yield return HuResult(
            hepai_player_index, player_to_score, hu_score, hu_fan, hu_class,
            hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask,
            base_fu, fu_fan_list, riichiExtras, score_changes, isSilent, playPresentationEffects,
            suppressHandReveal, hepaiTile, multiRon, deferScoreSettlement, ronDiscarderIndex, recycleDiscard,
            isQianggang, endgameScoreOnly, finalPanel, simultaneousHuHands, skipHandReveal);
    }

    private IEnumerator HuResult(
        int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class,
        int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask,
        int? base_fu, string[] fu_fan_list, RiichiEndResultExtras riichiExtras, Dictionary<int, int> score_changes,
        bool isSilent, bool playPresentationEffects,
        bool suppressHandReveal, int hepaiTile, bool multiRon, bool deferScoreSettlement, int? ronDiscarderIndex,
        bool recycleDiscard, bool isQianggang, bool endgameScoreOnly, bool finalPanel,
        Dictionary<int, int[]> simultaneousHuHands, bool skipHandReveal) {
        bool selfWon = NormalGameStateManager.Instance.indexToPosition[hepai_player_index] == "self";
        bool isSichuan = NormalGameStateManager.Instance.IsSichuanRule();
        bool isMidGameSichuanHu = deferScoreSettlement && isSichuan && !endgameScoreOnly;
        // 终局 settle_hu：仅分数面板，不重复 3D 和牌动画（reveal_hu 已亮牌）
        bool isEndgameScoreOnly = endgameScoreOnly;
        bool hasSimultaneousReveal = simultaneousHuHands != null && simultaneousHuHands.Count > 1;
        bool willRevealWinnerHand = playPresentationEffects && !skipHandReveal && !isEndgameScoreOnly && !isMidGameSichuanHu
            && hepai_player_hand != null && hepai_player_hand.Length > 0;

        bool selfIsAmongBatchWinners = false;
        if (hasSimultaneousReveal) {
            foreach (KeyValuePair<int, string> seat in NormalGameStateManager.Instance.indexToPosition) {
                if (seat.Value == "self" && simultaneousHuHands.ContainsKey(seat.Key)) {
                    selfIsAmongBatchWinners = true;
                    break;
                }
            }
        }
        if ((selfWon || selfIsAmongBatchWinners) && !skipHandReveal
                && !isMidGameSichuanHu && !isEndgameScoreOnly) {
            HideSelfGameplayControl(!willRevealWinnerHand && !suppressHandReveal);
        }

        if (!isSilent && !isEndgameScoreOnly) {
            string presentationAction = HepaiRevealDirector.ResolveHuPresentationAction(
                NormalGameStateManager.Instance.roomRule,
                hu_class,
                hu_fan,
                NormalGameStateManager.Instance.detailedConfig
            );
            GameCanvas.Instance.ShowActionDisplay(NormalGameStateManager.Instance.indexToPosition[hepai_player_index], presentationAction);
            SoundManager.Instance.PlayActionSound(NormalGameStateManager.Instance.indexToPosition[hepai_player_index], presentationAction);
        }

        if (isMidGameSichuanHu) {
            yield return HepaiRevealDirector.PlaySichuanMidGame(
                hepai_player_index, hu_class, hepaiTile, multiRon, ronDiscarderIndex, recycleDiscard, isQianggang);
        } else if (hasSimultaneousReveal && playPresentationEffects && !skipHandReveal) {
            yield return HepaiRevealDirector.PlayMany(simultaneousHuHands, hu_class);
        } else if (willRevealWinnerHand) {
            yield return HepaiRevealDirector.Play(hepai_player_index, hepai_player_hand, hu_class, hu_fan, isQianggang, ronDiscarderIndex, hepaiTile);
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
        if (finalPanel) {
            yield return PlayAfterFade(
                () => EndResultPanel.Instance.PlayPreparedShowResult(hu_score, hu_fan, base_fu, fu_fan_list, riichiExtras),
                playPresentationEffects
            );
        } else {
            // 多家和中间面板：完整播完番数动画并维持 3s 后再进入下家；
            // 不出可点击的确定按钮（最后一家才确认）。PlayAfterFade 只触发协程即返回，
            // 不会等待面板播完，因此这里必须直接 yield 面板协程。
            yield return PlayPresentationFade(playPresentationEffects);
            yield return EndResultPanel.Instance.PlayPreparedShowResultCoroutine(
                hu_score, hu_fan, base_fu, fu_fan_list, riichiExtras,
                RoundEndTiming.SichuanMidPanelConfirmSeconds, false, false);
            activeRoundEndCoroutine = null;
        }
    }
}
