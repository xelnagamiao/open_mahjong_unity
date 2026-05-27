using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager {
    /// <summary>
    /// 按 selfHandTiles 排序后重建自家 3D 手牌为明牌；仅在局终隐藏操作区（HideSelfGameplayControl）时调用。
    /// </summary>
    public void RefreshSelfFaceHandFromTileList() {
        if (NormalGameStateManager.Instance == null || selfPosPanel == null) return;
        List<int> ids = new List<int>(NormalGameStateManager.Instance.selfHandTiles);
        LayRoundEndClosedFaceHandAtPosition("self", ids);
    }

    /// <summary>
    /// 和牌展示：在赢家手牌区布置明牌并触发可选展开动画，等待 1.5 秒。
    /// </summary>
    public IEnumerator RoundEndRevealWinnerHandAndPlayExpandAnimation(int hepaiPlayerIndex, int[] hepaiPlayerHand) {
        if (NormalGameStateManager.Instance == null || hepaiPlayerHand == null || hepaiPlayerHand.Length == 0) yield break;
        if (!NormalGameStateManager.Instance.indexToPosition.ContainsKey(hepaiPlayerIndex)) yield break;
        string pos = NormalGameStateManager.Instance.indexToPosition[hepaiPlayerIndex];
        PosPanel3D panel = GetPosPanel(pos);
        ForceHandRevealIdle(panel);
        LayRoundEndFaceHandAtPosition(pos, hepaiPlayerHand);
        PlayHandRevealAnimation(panel);
        yield return new WaitForSeconds(RoundEndPresentation.Instance.HandRevealHoldSeconds);
    }

    /// <summary>
    /// 日麻荒牌流局：听牌玩家手牌倒下，四家都按服务端下发的真实手牌渲染牌面。
    /// 入参为 {player_index: [tile_id...]}，未在字典中视为不听。
    /// </summary>
    public IEnumerator RoundEndRevealTenpaiHandsAndPlayExpandAnimation(Dictionary<int, int[]> tenpaiTilesByPlayerIndex) {
        if (NormalGameStateManager.Instance == null || tenpaiTilesByPlayerIndex == null || tenpaiTilesByPlayerIndex.Count == 0) yield break;
        List<PosPanel3D> panelsToReveal = new List<PosPanel3D>();
        foreach (var kvp in tenpaiTilesByPlayerIndex) {
            int playerIndex = kvp.Key;
            int[] tiles = kvp.Value;
            if (tiles == null || tiles.Length == 0) continue;
            if (!NormalGameStateManager.Instance.indexToPosition.ContainsKey(playerIndex)) continue;
            string pos = NormalGameStateManager.Instance.indexToPosition[playerIndex];
            PosPanel3D panel = GetPosPanel(pos);
            LayRoundEndClosedFaceHandAtPosition(pos, tiles);
            panelsToReveal.Add(panel);
        }
        if (panelsToReveal.Count > 0) {
            yield return null;
            foreach (PosPanel3D panel in panelsToReveal) {
                PlayHandRevealAnimation(panel);
            }
            yield return new WaitForSeconds(RoundEndPresentation.Instance.HandRevealHoldSeconds);
        }
    }

    private void PlayHandRevealAnimation(PosPanel3D panel) {
        if (panel.handRevealAnimator == null || string.IsNullOrEmpty(panel.handRevealExpandTrigger)) return;
        Animator anim = panel.handRevealAnimator;
        anim.ResetTrigger(panel.handRevealExpandTrigger);
        anim.SetTrigger(panel.handRevealExpandTrigger);
    }

    private void ForceHandRevealIdle(PosPanel3D panel) {
        if (panel == null || panel.handRevealAnimator == null) return;
        Animator anim = panel.handRevealAnimator;
        anim.enabled = true;
        if (!string.IsNullOrEmpty(panel.handRevealExpandTrigger)) {
            anim.ResetTrigger(panel.handRevealExpandTrigger);
        }
        if (!string.IsNullOrEmpty(panel.handRevealIdleStateName)) {
            anim.Play(panel.handRevealIdleStateName, 0, 0f);
        }
        anim.Update(0f);
    }

    /// <summary>
    /// 在指定位置布置明牌，用于和牌展示。
    /// </summary>
    private void LayRoundEndFaceHandAtPosition(string playerPosition, int[] hepaiPlayerHand) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        ForceHandRevealIdle(panel);
        Transform target = panel.cardsPosition;
        List<GameObject> objectsToReturn = new List<GameObject>();
        CollectChildren(target, objectsToReturn);
        foreach (GameObject obj in objectsToReturn) {
            MahjongObjectPool.Instance.Return(-1, obj);
        }

        int last = hepaiPlayerHand[hepaiPlayerHand.Length - 1];
        int[] closed = new int[hepaiPlayerHand.Length - 1];
        System.Array.Copy(hepaiPlayerHand, closed, closed.Length);
        System.Array.Sort(closed, TileIdOrder.Comparer);
        for (int i = 0; i < closed.Length; i++) {
            Set3DTile(closed[i], target, "Record", playerPosition);
        }
        Set3DTile(last, target, "Record", playerPosition);
    }

    private void LayRoundEndClosedFaceHandAtPosition(string playerPosition, IList<int> handTiles) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        ForceHandRevealIdle(panel);
        Transform target = panel.cardsPosition;
        List<GameObject> objectsToReturn = new List<GameObject>();
        CollectChildren(target, objectsToReturn);
        foreach (GameObject obj in objectsToReturn) {
            MahjongObjectPool.Instance.Return(-1, obj);
        }

        List<int> sorted = new List<int>(handTiles);
        sorted.Sort(TileIdOrder.Comparer);
        for (int i = 0; i < sorted.Count; i++) {
            Set3DTile(sorted[i], target, "Record", playerPosition);
        }
    }
}
