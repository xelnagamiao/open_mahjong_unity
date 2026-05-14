using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 局终流程：和牌、流局、荒牌流局与不听罚符、特殊流局罚符、终局与数和尾的先后顺序；操作区显隐。
/// 子物体可含 ENDResultPanel、ENDLiujuPanel、PenaltyPanel、ShuHeWeiPanel、ENDGamePanel、SwitchSeatPanel 等。
/// </summary>
public class RoundEndFlowManager : MonoBehaviour {
    public static RoundEndFlowManager Instance { get; private set; }

    /// <summary>荒牌流局标题与听牌标记面板的停留秒数（与牌谱自动步进一致）。</summary>
    public const float DrawCaptionHoldSeconds = 2f;

    /// <summary>荒牌不听罚符在 PenaltyPanel 上的总演出秒数（三等分）。</summary>
    public const float DrawNotenPenaltyHoldSeconds = 3f;

    [SerializeField] private GameObject selfGameplayControlRoot;

    private Coroutine activeRoundEndCoroutine;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void HideSelfGameplayControl(bool revealSelfHand = true) {
        selfGameplayControlRoot.SetActive(false);
        if (revealSelfHand) {
            Game3DManager.Instance.RefreshSelfFaceHandFromTileList();
        }
    }

    public void ShowSelfGameplayControlAndResyncHand3D() {
        selfGameplayControlRoot.SetActive(true);
        Game3DManager.Instance.RefreshSelfBlankHandFromSelfTileList();
    }

    public void StopActiveSequence() {
        if (activeRoundEndCoroutine != null) {
            StopCoroutine(activeRoundEndCoroutine);
            activeRoundEndCoroutine = null;
        }
    }

    public void PresentHuResultSequence(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, int? base_fu, string[] fu_fan_list, RiichiEndResultExtras riichiExtras, bool isSilent = false) {
        StopActiveSequence();
        activeRoundEndCoroutine = StartCoroutine(CoHuResult(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask, base_fu, fu_fan_list, riichiExtras, isSilent));
    }

    private IEnumerator CoHuResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, int? base_fu, string[] fu_fan_list, RiichiEndResultExtras riichiExtras, bool isSilent) {
        HideSelfGameplayControl();
        bool riichi = NormalGameStateManager.Instance.roomRule == "riichi";
        if (riichi && hepai_player_hand != null && hepai_player_hand.Length > 0) {
            yield return Game3DManager.Instance.RoundEndRevealWinnerHandAndPlayExpandAnimation(hepai_player_index, hepai_player_hand, hepai_player_combination_mask);
        }
        // 战术鸣牌：字体动画与音效已在申请阶段播放，跳过此处的胡牌发声与字体动画
        if (!isSilent) {
            GameCanvas.Instance.ShowActionDisplay(NormalGameStateManager.Instance.indexToPosition[hepai_player_index], hu_class);
            SoundManager.Instance.PlayActionSound(NormalGameStateManager.Instance.indexToPosition[hepai_player_index], hu_class);
        }
        EndResultPanel.Instance.StartShowResultAfterDelay(0f, hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask, base_fu, fu_fan_list, riichiExtras);
        activeRoundEndCoroutine = null;
    }

    public void PresentLiuju(string displayText) {
        HideSelfGameplayControl();
        EndLiujuPanel.Instance.ShowLiujuPanel(displayText);
    }

    /// <summary>荒牌流局：听牌展开手牌、流局与听牌标记、不听罚符分数变化三段演出。</summary>
    public void PresentDrawWallLiujuAndPenalty(string displayText, Dictionary<int, int> player_to_score, RiichiEndResultExtras riichiExtras) {
        StopActiveSequence();
        activeRoundEndCoroutine = StartCoroutine(CoRiichiDrawWallLiujuSequence(displayText, player_to_score, riichiExtras));
    }

    private IEnumerator CoRiichiDrawWallLiujuSequence(string displayText, Dictionary<int, int> player_to_score, RiichiEndResultExtras riichiExtras) {
        HideSelfGameplayControl(false);
        Dictionary<int, int[]> tenpaiTiles = riichiExtras != null ? riichiExtras.TenpaiTiles : null;
        // 听牌玩家手牌先倒下，动画时长由流程统一等待。
        yield return Game3DManager.Instance.RoundEndRevealTenpaiHandsAndPlayExpandAnimation(tenpaiTiles);
        // 流局面板按 player_index 在对应方位容器中显示真实听张。
        EndLiujuPanel.Instance.ShowLiujuPanel(displayText, tenpaiTiles, DrawCaptionHoldSeconds);
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
            // 不听罚符分数变化交给 PenaltyPanel 的三段动画。
            PenaltyPanel.Instance.ShowPenaltyPanel(usernameByPos, scoresByPos, deltaByPos, PenaltyPresentation.AfterDrawNotenThreePhase, DrawNotenPenaltyHoldSeconds);
            yield return new WaitForSeconds(DrawNotenPenaltyHoldSeconds);
        }
        activeRoundEndCoroutine = null;
    }

    public void PresentRiichiAbortPenalty(Dictionary<string, string> usernameByPos, Dictionary<string, int> scoreByPos, Dictionary<string, int> deltaByPos) {
        HideSelfGameplayControl();
        PenaltyPanel.Instance.ShowPenaltyPanel(usernameByPos, scoreByPos, deltaByPos, PenaltyPresentation.Standard);
    }

    public void PresentRiichiAbortPenalty(Dictionary<int, int> player_to_score, RiichiEndResultExtras riichiExtras) {
        var usernameByPos = new Dictionary<string, string>();
        var scoreByPos = new Dictionary<string, int>();
        var deltaByPos = new Dictionary<string, int>();
        Dictionary<int, int> scoreChanges = riichiExtras != null ? riichiExtras.ScoreChanges : null;
        foreach (var kvp in NormalGameStateManager.Instance.indexToPosition) {
            int idx = kvp.Key;
            string pos = kvp.Value;
            int scoreAfter = player_to_score[idx];
            usernameByPos[pos] = NormalGameStateManager.Instance.player_to_info[pos].username;
            scoreByPos[pos] = scoreAfter;
            deltaByPos[pos] = scoreChanges[idx];
            NormalGameStateManager.Instance.player_to_info[pos].score = scoreAfter;
        }
        BoardCanvas.Instance.UpdatePlayerScores(player_to_score, NormalGameStateManager.Instance.indexToPosition);
        PresentRiichiAbortPenalty(usernameByPos, scoreByPos, deltaByPos);
    }

    public void PresentEndGame(long game_random_seed, Dictionary<string, Dictionary<string, object>> player_final_data) {
        HideSelfGameplayControl();
        GameSceneUIManager.Instance.ShowEndGame(game_random_seed, player_final_data);
    }

    public void PresentShuhewei(Dictionary<int, int> player_fu, Dictionary<int, int> player_to_score, Dictionary<int, int> score_changes, Dictionary<int, string[]> player_fan, Dictionary<int, string[]> player_fu_types, Dictionary<int, string> indexToPosition, Dictionary<string, PlayerInfoClass> player_to_info) {
        HideSelfGameplayControl();
        GameSceneUIManager.Instance.ShowShuhewei(player_fu, player_to_score, score_changes, player_fan, player_fu_types, indexToPosition, player_to_info);
    }
}
