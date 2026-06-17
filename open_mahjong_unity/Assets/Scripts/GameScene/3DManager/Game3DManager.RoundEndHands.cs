using System.Collections;

using System.Collections.Generic;

using UnityEngine;



public partial class Game3DManager {

    private const int RecordHandCardsPerRow = 14;



    /// <summary>

    /// 按 selfHandTiles 排序后重建自家 3D 手牌为明牌；仅在局终隐藏操作区（HideSelfGameplayControl）时调用。

    /// </summary>

    public void RefreshSelfFaceHandFromTileList() {

        if (NormalGameStateManager.Instance == null || selfPosPanel == null) return;

        List<int> ids = new List<int>(NormalGameStateManager.Instance.selfHandTiles);

        LayRoundEndClosedFaceHandAtPosition("self", ids);

    }



    /// <summary>

    /// 和牌倒牌总入口（策略由 <see cref="HepaiRevealDirector"/> 决定）。

    /// </summary>

    public IEnumerator PlayHepaiHandReveal(HepaiPresentationRequest request) {

        if (request == null || request.HepaiPlayerHand == null || request.HepaiPlayerHand.Length == 0) {

            yield break;

        }



        string pos = request.WinnerPosition;

        PosPanel3D panel = GetPosPanel(pos);

        if (panel == null) yield break;



        int[] hand = request.HepaiPlayerHand;

        int winTileId = hand[hand.Length - 1];

        int[] closed = new int[hand.Length - 1];

        System.Array.Copy(hand, closed, closed.Length);

        System.Array.Sort(closed, TileIdOrder.Comparer);



        ForceHandRevealIdle(panel);

        ClearHandCardsPosition(panel.cardsPosition);



        switch (request.WinTileMode) {

            case HepaiWinTilePresentMode.TsumoTravel:

                yield return CoTsumoTravelReveal(pos, panel, closed, winTileId);

                break;

            case HepaiWinTilePresentMode.GuobiaoRonTravelFromRiver:

                yield return CoGuobiaoRonTravelFromRiver(pos, panel, closed, winTileId, request.DiscardPlayerPosition);

                break;

            case HepaiWinTilePresentMode.RonInstantThenPause:

                LayRoundEndFaceHandAtPosition(pos, hand);

                yield return new WaitForSeconds(HepaiRevealTiming.TravelSeconds);

                break;

        }



        PlayHandRevealAnimation(panel);

        yield return new WaitForSeconds(HepaiRevealTiming.ExpandHoldSeconds);

    }



    /// <summary>

    /// 国标荣和错和：倒牌仅为展示，恢复继续对局时的 3D 手牌区（不改动河牌）。

    /// </summary>

    public void RestoreMidGameHandAfterCuoheRonReveal(string winnerPosition) {

        if (string.IsNullOrEmpty(winnerPosition)) return;

        PosPanel3D panel = GetPosPanel(winnerPosition);

        if (panel != null) {

            ClearHandCardsPosition(panel.cardsPosition);

            ForceHandRevealIdle(panel);

        }



        if (winnerPosition == "self") {

            if (RoundEndPresentation.Instance != null) {

                RoundEndPresentation.Instance.ShowSelfGameplayControlAndResyncHand3D();

            }

            else {

                ClearSelf3DHandTiles();

            }

            return;

        }



        if (NormalGameStateManager.Instance == null) return;

        int count = 0;

        if (NormalGameStateManager.Instance.player_to_info.TryGetValue(winnerPosition, out var info)) {

            count = info.hand_tiles_count;

        }

        for (int i = 0; i < count; i++) {

            Get3DTile(winnerPosition, "init", 0);

        }

    }



    /// <summary>

    /// 日麻荒牌流局：听牌玩家手牌倒下，四家都按服务端下发的真实手牌渲染牌面。

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

            yield return new WaitForSeconds(HepaiRevealTiming.PrePanelTotalSeconds);

        }

    }



    private IEnumerator CoTsumoTravelReveal(string playerPosition, PosPanel3D panel, int[] closedSorted, int winTileId) {

        Transform target = panel.cardsPosition;

        for (int i = 0; i < closedSorted.Length; i++) {

            Set3DTile(closedSorted[i], target, "Record", playerPosition);

        }



        Vector3 spawnPos = GetTsumoSpawnWorldPosition(playerPosition, target);

        Quaternion rot = RecordHandTileRotation(playerPosition);

        GameObject winObj = MahjongObjectPool.Instance.Spawn(winTileId, spawnPos, rot);

        if (winObj == null) yield break;

        winObj.transform.SetParent(target, worldPositionStays: true);



        int lastSlot = closedSorted.Length;

        Vector3 endPos = GetRecordHandSlotWorldPosition(playerPosition, target, lastSlot);

        yield return CoLerpTransform(winObj.transform, spawnPos, rot, endPos, rot, HepaiRevealTiming.TravelSeconds);

    }



    private IEnumerator CoGuobiaoRonTravelFromRiver(string winnerPosition, PosPanel3D panel, int[] closedSorted, int winTileId, string discardPlayerPosition) {

        Transform target = panel.cardsPosition;

        for (int i = 0; i < closedSorted.Length; i++) {

            Set3DTile(closedSorted[i], target, "Record", winnerPosition);

        }



        GameObject riverTile = TryTakeLastDiscardObjectForRon(winTileId, discardPlayerPosition);

        Vector3 endPos = GetRecordHandSlotWorldPosition(winnerPosition, target, closedSorted.Length);

        Quaternion endRot = RecordHandTileRotation(winnerPosition);



        if (riverTile != null) {

            Vector3 startPos = riverTile.transform.position;

            Quaternion startRot = riverTile.transform.rotation;

            riverTile.transform.SetParent(target, worldPositionStays: true);

            yield return CoLerpTransform(riverTile.transform, startPos, startRot, endPos, endRot, HepaiRevealTiming.TravelSeconds);

            yield break;

        }



        GameObject fallback = MahjongObjectPool.Instance.Spawn(winTileId, endPos, endRot);

        if (fallback != null) {

            fallback.transform.SetParent(target, worldPositionStays: true);

        }

        yield return new WaitForSeconds(HepaiRevealTiming.TravelSeconds);

    }



    private GameObject TryTakeLastDiscardObjectForRon(int expectedTileId, string discardPlayerPosition) {

        if (lastCutJiagang3DObject == null) return null;

        Tile3D tile3D = lastCutJiagang3DObject.GetComponent<Tile3D>();

        if (tile3D != null && tile3D.GetTileId() != expectedTileId) {

            Debug.LogWarning($"河牌最后一张 id={tile3D.GetTileId()} 与和牌张 {expectedTileId} 不一致，仍使用该对象做演出");

        }



        GameObject obj = lastCutJiagang3DObject;

        lastCutJiagang3DObject = null;

        return obj;

    }



    private Vector3 GetTsumoSpawnWorldPosition(string playerPosition, Transform cardsPosition) {

        GetRecordHandLayoutDirections(playerPosition, out Vector3 widthDir, out _, out _);

        int childCount = cardsPosition.childCount;

        return cardsPosition.position + (childCount + 1) * cardWidth * widthDir;

    }



    private Vector3 GetRecordHandSlotWorldPosition(string playerPosition, Transform cardsPosition, int slotIndex) {

        GetRecordHandLayoutDirections(playerPosition, out Vector3 widthDir, out Vector3 heightDir, out _);

        int row = slotIndex / RecordHandCardsPerRow;

        int col = slotIndex % RecordHandCardsPerRow;

        float colOffset = ComputeRowCenterOffset(cardsPosition, row, col, RecordHandCardsPerRow, false);

        Vector3 pos = cardsPosition.position;

        pos += widthDir.normalized * colOffset;

        pos += heightDir.normalized * heightSpacing * row;

        return pos;

    }



    private void GetRecordHandLayoutDirections(string playerPosition, out Vector3 widthDir, out Vector3 heightDir, out Quaternion rotation) {

        rotation = RecordHandTileRotation(playerPosition);

        if (playerPosition == "self") {

            widthDir = RightDirection;

            heightDir = BackDirection;

        }

        else if (playerPosition == "left") {

            widthDir = BackDirection;

            heightDir = LeftDirection;

        }

        else if (playerPosition == "top") {

            widthDir = LeftDirection;

            heightDir = FrontDirection;

        }

        else {

            widthDir = FrontDirection;

            heightDir = RightDirection;

        }

    }



    private static IEnumerator CoLerpTransform(Transform t, Vector3 fromPos, Quaternion fromRot, Vector3 toPos, Quaternion toRot, float duration) {

        if (t == null) yield break;

        if (duration <= 0f) {

            t.SetPositionAndRotation(toPos, toRot);

            yield break;

        }

        float elapsed = 0f;

        while (elapsed < duration) {

            elapsed += Time.deltaTime;

            float u = Mathf.Clamp01(elapsed / duration);

            t.SetPositionAndRotation(Vector3.Lerp(fromPos, toPos, u), Quaternion.Slerp(fromRot, toRot, u));

            yield return null;

        }

        t.SetPositionAndRotation(toPos, toRot);

    }



    private void ClearHandCardsPosition(Transform cardsPosition) {

        if (cardsPosition == null) return;

        List<GameObject> objectsToReturn = new List<GameObject>();

        CollectChildren(cardsPosition, objectsToReturn);

        foreach (GameObject obj in objectsToReturn) {

            MahjongObjectPool.Instance.Return(-1, obj);

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



    private void LayRoundEndFaceHandAtPosition(string playerPosition, int[] hepaiPlayerHand) {

        PosPanel3D panel = GetPosPanel(playerPosition);

        ForceHandRevealIdle(panel);

        Transform target = panel.cardsPosition;

        ClearHandCardsPosition(target);



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

        ClearHandCardsPosition(target);



        List<int> sorted = new List<int>(handTiles);

        sorted.Sort(TileIdOrder.Comparer);

        for (int i = 0; i < sorted.Count; i++) {

            Set3DTile(sorted[i], target, "Record", playerPosition);

        }

    }

}


