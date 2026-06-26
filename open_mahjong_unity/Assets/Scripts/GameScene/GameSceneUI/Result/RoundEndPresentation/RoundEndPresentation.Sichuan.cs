using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 四川血战终局演出（与服务端 _settle_liuju ABCD 顺序一致）：
/// reveal_hu → settle_hu（按和牌先后 hu_order）→ chajiao（末家和牌者下家起逆时针，逐家 1 面板）。
/// 退税仅并入没叫/花猪的查叫面板；三家和跳过查叫时不退税。
/// </summary>
public partial class RoundEndPresentation {
    public void EnqueueSichuanRevealHu(Dictionary<int, int[]> huHands) {
        EnqueueSichuanEndgameStep(CoSichuanRevealHuStep(huHands));
    }

    public void EnqueueSichuanSettleHu(
        int hepaiPlayerIndex,
        Dictionary<int, int> player_to_score,
        int huScore,
        string[] huFan,
        string huClass,
        int[] hepaiPlayerHand,
        int[][] hepaiPlayerCombinationMask,
        Dictionary<int, int> scoreChanges,
        bool isFinalPanel) {
        EnqueueSichuanEndgameStep(CoSichuanSettleHuStep(
            hepaiPlayerIndex, player_to_score, huScore, huFan, huClass,
            hepaiPlayerHand, hepaiPlayerCombinationMask, scoreChanges, isFinalPanel));
    }

    public void EnqueueSichuanChajiao(
        int focusPlayerIndex,
        string statusKey,
        int[] hand,
        int[][] combinationMask,
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> scoreChanges,
        bool isFinalPanel,
        bool hasRefund = false) {
        EnqueueSichuanEndgameStep(CoSichuanChajiaoStep(
            focusPlayerIndex, statusKey, hand, combinationMask, player_to_score, scoreChanges, isFinalPanel, hasRefund));
    }

    public void EnqueueSichuanChaRefund(
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> scoreChanges,
        bool isFinalPanel) {
        EnqueueSichuanEndgameStep(CoSichuanChaRefundStep(player_to_score, scoreChanges, isFinalPanel));
    }

    private IEnumerator CoSichuanRevealHuStep(Dictionary<int, int[]> huHands) {
        if (huHands != null && huHands.Count > 0 && Game3DManager.Instance != null) {
            Game3DManager.Instance.RevealSichuanLiujuAllHands(huHands);
            yield return new WaitForSeconds(RoundEndTiming.RoundEndHandRevealSeconds);
        }
    }

    private IEnumerator CoSichuanSettleHuStep(
        int hepaiPlayerIndex,
        Dictionary<int, int> player_to_score,
        int huScore,
        string[] huFan,
        string huClass,
        int[] hepaiPlayerHand,
        int[][] hepaiPlayerCombinationMask,
        Dictionary<int, int> scoreChanges,
        bool isFinalPanel) {
        BeginSichuanEndgamePanel();
        EndResultPanel.Instance.PrepareSichuanSettleHuSingle(
            hepaiPlayerIndex, player_to_score, huScore, huFan, huClass,
            hepaiPlayerHand, hepaiPlayerCombinationMask, scoreChanges);
        yield return CoFadeInSichuanEndgamePanel();
        yield return EndResultPanel.Instance.CoPlaySichuanSettleHuRoutine(huScore, huFan, isFinalPanel);
    }

    private IEnumerator CoSichuanChajiaoStep(
        int focusPlayerIndex,
        string statusKey,
        int[] hand,
        int[][] combinationMask,
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> scoreChanges,
        bool isFinalPanel,
        bool hasRefund) {
        BeginSichuanEndgamePanel();
        EndResultPanel.Instance.PrepareSichuanChajiaoSingle(
            focusPlayerIndex, statusKey, hand, combinationMask, player_to_score, scoreChanges, isFinalPanel, hasRefund);
        yield return CoFadeInSichuanEndgamePanel();
        yield return EndResultPanel.Instance.CoPlaySichuanChajiaoRoutine(statusKey, isFinalPanel, hasRefund);
    }

    private IEnumerator CoSichuanChaRefundStep(
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> scoreChanges,
        bool isFinalPanel) {
        BeginSichuanEndgamePanel();
        EndResultPanel.Instance.PrepareSichuanChaRefundSingle(player_to_score, scoreChanges);
        yield return CoFadeInSichuanEndgamePanel();
        yield return EndResultPanel.Instance.CoPlaySichuanChaRefundRoutine(isFinalPanel);
    }
}
