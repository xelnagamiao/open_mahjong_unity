using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class RoundEndPresentation {
    public void PresentEndGame(long game_random_seed, Dictionary<string, Dictionary<string, object>> player_final_data) {
        GameSceneUIManager.Instance.ShowEndGame(game_random_seed, player_final_data);
    }

    public void PresentShuhewei(Dictionary<int, int> player_fu, Dictionary<int, int> player_to_score, Dictionary<int, int> score_changes, Dictionary<int, string[]> player_fan, Dictionary<int, string[]> player_fu_types, Dictionary<int, string> indexToPosition, Dictionary<string, PlayerInfoClass> player_to_info, int? hepaiPlayerIndex = null, int[] hepaiPlayerHand = null, int[][] hepaiPlayerCombinationMask = null, bool playPresentationEffects = true) {
        StartSequence(CoShuhewei(player_fu, player_to_score, score_changes, player_fan, player_fu_types, indexToPosition, player_to_info, hepaiPlayerIndex, hepaiPlayerHand, hepaiPlayerCombinationMask, playPresentationEffects));
    }

    private IEnumerator CoShuhewei(Dictionary<int, int> player_fu, Dictionary<int, int> player_to_score, Dictionary<int, int> score_changes, Dictionary<int, string[]> player_fan, Dictionary<int, string[]> player_fu_types, Dictionary<int, string> indexToPosition, Dictionary<string, PlayerInfoClass> player_to_info, int? hepaiPlayerIndex, int[] hepaiPlayerHand, int[][] hepaiPlayerCombinationMask, bool playPresentationEffects) {
        if (hepaiPlayerIndex.HasValue
            && NormalGameStateManager.Instance.indexToPosition.TryGetValue(hepaiPlayerIndex.Value, out string winnerPos)
            && winnerPos == "self") {
            HideSelfGameplayControl(true);
        }
        if (playPresentationEffects && hepaiPlayerIndex.HasValue && hepaiPlayerHand != null && hepaiPlayerHand.Length > 0) {
            yield return Game3DManager.Instance.RoundEndRevealWinnerHandAndPlayExpandAnimation(hepaiPlayerIndex.Value, hepaiPlayerHand, hepaiPlayerCombinationMask);
        }

        PreparePresentationRoot(playPresentationEffects);
        EndShuheWeiPanel.Instance.PrepareShuhewei(player_fu, player_to_score, score_changes, player_fan, player_fu_types, indexToPosition, player_to_info, false);
        yield return PlayAfterFade(
            () => EndShuheWeiPanel.Instance.PlayPreparedShuhewei(player_fu, player_to_score, score_changes, player_fan, player_fu_types, false),
            playPresentationEffects
        );
    }
}
