using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class RoundEndPresentation {
    /// <summary>开始和牌结果流程。</summary>
    public void PresentHuResultSequence(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, int? base_fu, string[] fu_fan_list, RiichiEndResultExtras riichiExtras, bool isSilent = false, bool playPresentationEffects = true) {
        StartSequence(HuResult(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask, base_fu, fu_fan_list, riichiExtras, isSilent, playPresentationEffects));
    }

    private IEnumerator HuResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, int? base_fu, string[] fu_fan_list, RiichiEndResultExtras riichiExtras, bool isSilent, bool playPresentationEffects) {
        bool selfWon = NormalGameStateManager.Instance.indexToPosition[hepai_player_index] == "self";
        bool willRevealWinnerHand = playPresentationEffects && hepai_player_hand != null && hepai_player_hand.Length > 0;
        if (selfWon) {
            HideSelfGameplayControl(!willRevealWinnerHand);
        }
        if (!isSilent) {
            // 显示喊胡提示
            GameCanvas.Instance.ShowActionDisplay(NormalGameStateManager.Instance.indexToPosition[hepai_player_index], hu_class);
            // 播放喊胡音效
            SoundManager.Instance.PlayActionSound(NormalGameStateManager.Instance.indexToPosition[hepai_player_index], hu_class);
        }

        // 展示赢家明牌+展开动画
        if (willRevealWinnerHand) {
            yield return Game3DManager.Instance.RoundEndRevealWinnerHandAndPlayExpandAnimation(hepai_player_index, hepai_player_hand);
        }

        PreparePresentationRoot(playPresentationEffects);
        EndResultPanel.Instance.PrepareShowResult(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask, riichiExtras);
        yield return PlayAfterFade(
            () => EndResultPanel.Instance.PlayPreparedShowResult(hu_score, hu_fan, base_fu, fu_fan_list, riichiExtras),
            playPresentationEffects
        );
    }
}
