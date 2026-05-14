using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager {
    /// <summary>
    /// 按 selfHandTiles 排序后重建自家 3D 手牌为明牌；仅在局终隐藏操作区（HideSelfGameplayControl）时调用。
    /// </summary>
    public void RefreshSelfFaceHandFromTileList() {
        if (NormalGameStateManager.Instance == null || selfPosPanel == null) return;
        List<GameObject> objectsToReturn = new List<GameObject>();
        CollectChildren(selfPosPanel.cardsPosition, objectsToReturn);
        foreach (GameObject obj in objectsToReturn) {
            MahjongObjectPool.Instance.Return(-1, obj);
        }
        List<int> ids = new List<int>(NormalGameStateManager.Instance.selfHandTiles);
        ids.Sort(TileIdOrder.Comparer);
        for (int i = 0; i < ids.Count; i++) {
            Set3DTile(ids[i], selfPosPanel.cardsPosition, "Record", "self");
        }
    }

    /// <summary>
    /// 和牌展示：在赢家手牌区布置明牌并触发可选展开动画，等待 1.5 秒。
    /// </summary>
    public IEnumerator RoundEndRevealWinnerHandAndPlayExpandAnimation(int hepaiPlayerIndex, int[] hepaiPlayerHand, int[][] combinationMask) {
        if (NormalGameStateManager.Instance == null || hepaiPlayerHand == null || hepaiPlayerHand.Length == 0) yield break;
        if (!NormalGameStateManager.Instance.indexToPosition.ContainsKey(hepaiPlayerIndex)) yield break;
        string pos = NormalGameStateManager.Instance.indexToPosition[hepaiPlayerIndex];
        LayRoundEndFaceHandAtPosition(pos, hepaiPlayerHand, combinationMask);
        PosPanel3D panel = GetPosPanel(pos);
        PlayHandRevealAnimation(panel);
        yield return new WaitForSeconds(1.5f);
    }

    /// <summary>
    /// 日麻荒牌流局：听牌玩家手牌倒下，自家听牌时显示真实牌面。
    /// 入参为 {player_index: [tile_id...]}，未在字典中视为不听。
    /// </summary>
    public IEnumerator RoundEndRevealTenpaiHandsAndPlayExpandAnimation(Dictionary<int, int[]> tenpaiTilesByPlayerIndex) {
        if (NormalGameStateManager.Instance == null || tenpaiTilesByPlayerIndex == null || tenpaiTilesByPlayerIndex.Count == 0) yield break;
        bool played = false;
        foreach (var kvp in tenpaiTilesByPlayerIndex) {
            int playerIndex = kvp.Key;
            int[] tiles = kvp.Value;
            if (tiles == null || tiles.Length == 0) continue;
            if (!NormalGameStateManager.Instance.indexToPosition.ContainsKey(playerIndex)) continue;
            string pos = NormalGameStateManager.Instance.indexToPosition[playerIndex];
            if (pos == "self") {
                RefreshSelfFaceHandFromTileList();
            }
            PlayHandRevealAnimation(GetPosPanel(pos));
            played = true;
        }
        if (played) {
            yield return new WaitForSeconds(1.5f);
        }
    }

    private void PlayHandRevealAnimation(PosPanel3D panel) {
        if (panel.handRevealAnimator != null && !string.IsNullOrEmpty(panel.handRevealExpandTrigger)) {
            panel.handRevealAnimator.enabled = true;
            panel.handRevealAnimator.ResetTrigger(panel.handRevealExpandTrigger);
            panel.handRevealAnimator.SetTrigger(panel.handRevealExpandTrigger);
        }
    }


    /// <summary>
    /// 在指定位置布置明牌，用于和牌展示。
    /// </summary>
    private void LayRoundEndFaceHandAtPosition(string playerPosition, int[] hepaiPlayerHand, int[][] combinationMask) {
        PosPanel3D panel = GetPosPanel(playerPosition);
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
        if (combinationMask != null) {
            for (int r = 0; r < combinationMask.Length; r++) {
                int[] row = combinationMask[r];
                if (row == null) continue;
                for (int k = 1; k < row.Length; k += 2) {
                    Set3DTile(row[k], target, "Record", playerPosition);
                }
            }
        }
        Set3DTile(last, target, "Record", playerPosition);
    }
}
