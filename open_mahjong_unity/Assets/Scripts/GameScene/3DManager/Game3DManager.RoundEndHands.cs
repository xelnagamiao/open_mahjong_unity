using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public partial class Game3DManager {
    /// <summary>按当前规则的手牌结构返回局终单行明牌容量。</summary>
    private int GetRevealedHandCardsPerRow() {
        GameRecordManager.ResolveActionRuleContext(null, null, out string roomRule, out string subRule);
        return HandStructures.Resolve(roomRule, subRule).CompleteHandTileCount;
    }
    /// <summary>
    /// 按 selfHandTiles 排序后重建自家 3D 手牌为明牌；仅在局终隐藏操作区（HideSelfGameplayControl）时调用。
    /// </summary>
    public void RefreshSelfFaceHandFromTileList() {
        if (selfPosPanel == null) return;
        List<int> ids = new List<int>(NormalGameStateManager.Instance.selfHandTiles);
        LayRoundEndClosedFaceHandAtPosition("self", ids);
    }
    /// <summary>和牌倒牌总入口（策略由 <see cref="HepaiRevealDirector"/> 决定）。</summary>
    public IEnumerator PlayHepaiHandReveal(HepaiPresentationRequest request) {
        if (request == null || request.HepaiPlayerHand == null || request.HepaiPlayerHand.Length == 0) {
            yield break;
        }
        // A fast win response can arrive before the discard/draw animation has
        // created its physical tile. Let its owner finish before taking or revealing it.
        int generation = _handAnimationGeneration;
        while (HasPendingRecordStepAnimations(request.WinnerPosition)
            || (IsHandAnimPlayer(request.DiscardPlayerPosition)
                && HasPendingRecordStepAnimations(request.DiscardPlayerPosition))) {
            yield return null;
            if (generation != _handAnimationGeneration) yield break;
        }
        ResetHandRevealAnimators();
        string pos = request.WinnerPosition;
        PosPanel3D panel = GetPosPanel(pos);
        if (panel == null) yield break;
        int[] hand = request.HepaiPlayerHand;
        int winTileId = hand[hand.Length - 1];
        int[] closed = new int[hand.Length - 1];
        System.Array.Copy(hand, closed, closed.Length);
        closed = OrderRoundEndClosedHand(pos, closed);
        SuspendWashizuPublicHand(pos);
        ForceHandRevealIdle(panel);
        ClearHandCardsPosition(panel.cardsPosition);
        if (request.IsQianggang && request.WinTileMode == HepaiWinTilePresentMode.RonInstantThenPause) {
            RecycleRobbedAddedKongSource(request);
        }
        switch (request.WinTileMode) {
            case HepaiWinTilePresentMode.TsumoTravel:
                yield return CoTsumoTravelReveal(pos, panel, closed, winTileId);
                break;
            case HepaiWinTilePresentMode.GuobiaoRonTravelFromRiver:
                yield return CoGuobiaoRonTravelFromRiver(pos, panel, closed, winTileId, request.DiscardPlayerPosition);
                break;
            case HepaiWinTilePresentMode.RonInstantThenPause:
                for (int i = 0; i < closed.Length; i++) {
                    Set3DTile(closed[i], panel.cardsPosition, "Record", pos);
                }
                Set3DTile(winTileId, panel.cardsPosition, "Record", pos, usePresentationCopy: true);
                yield return new WaitForSeconds(HepaiRevealTiming.TravelSeconds);
                break;
        }
        PlayHandRevealAnimation(panel);
        yield return new WaitForSeconds(HepaiRevealTiming.ExpandHoldSeconds);
    }

    /// <summary>多家荣和：同一帧摆好全部赢家手牌并同时触发倒牌动画。</summary>
    public IEnumerator PlayMultipleHepaiHandReveals(IList<HepaiPresentationRequest> requests) {
        if (requests == null || requests.Count == 0) yield break;
        // 与单家荣和相同，先等弃牌落河；快响应不能在实体牌生成前取走牌源。
        int generation = _handAnimationGeneration;
        foreach (HepaiPresentationRequest request in requests) {
            if (request == null) continue;
            while ((IsHandAnimPlayer(request.WinnerPosition)
                    && HasPendingRecordStepAnimations(request.WinnerPosition))
                || (IsHandAnimPlayer(request.DiscardPlayerPosition)
                    && HasPendingRecordStepAnimations(request.DiscardPlayerPosition))) {
                yield return null;
                if (generation != _handAnimationGeneration) yield break;
            }
        }
        ResetHandRevealAnimators();
        List<PosPanel3D> revealPanels = new List<PosPanel3D>();
        var claimedSources = new HashSet<string>();
        for (int i = 0; i < requests.Count; i++) {
            HepaiPresentationRequest request = requests[i];
            if (request == null || request.HepaiPlayerHand == null || request.HepaiPlayerHand.Length == 0) continue;
            PosPanel3D panel = GetPosPanel(request.WinnerPosition);
            if (panel == null) continue;
            ForceHandRevealIdle(panel);
            bool takeRiver = request.WinTileMode == HepaiWinTilePresentMode.GuobiaoRonTravelFromRiver;
            int winningId = request.HepaiPlayerHand[request.HepaiPlayerHand.Length - 1];
            string sourceKey = request.DiscardPlayerPosition + ":" + winningId;
            bool firstClaim = takeRiver && claimedSources.Add(sourceKey);
            GameObject source = firstClaim
                ? TryTakeLastDiscardObjectForRon(winningId, request.DiscardPlayerPosition) : null;
            // 一炮多响共享同一实体牌；后续赢家才使用展示副本。首家缺牌源时不能凭空补牌。
            LayRoundEndFaceHandAtPosition(request.WinnerPosition, request.HepaiPlayerHand,
                source, spawnWinningCopy: !firstClaim);
            revealPanels.Add(panel);
        }
        if (revealPanels.Count == 0) yield break;
        yield return new WaitForSeconds(HepaiRevealTiming.TravelSeconds);
        for (int i = 0; i < revealPanels.Count; i++) {
            PlayHandRevealAnimation(revealPanels[i]);
        }
        yield return new WaitForSeconds(HepaiRevealTiming.ExpandHoldSeconds);
    }

    /// <summary>
    /// 被抢加杠在服务端确认前已按加杠暂态展示；和牌确认后应撤回该加杠牌。
    /// </summary>
    private void RecycleRobbedAddedKongSource(HepaiPresentationRequest request) {
        GameObject source = TryResolveJiagangSourceObject(
            request.DiscardPlayerPosition,
            request.HepaiTile);
        if (source == null) return;
        ClearLastJiagangIfMatches(request.DiscardPlayerPosition, source);
        source.transform.SetParent(null, worldPositionStays: true);
        MahjongObjectPool.Instance.Return(-1, source);
    }
    /// <summary>
    /// 牌谱/观战和牌 3D：自家始终与对局一致（隐藏 2D 手牌区 + cardsPosition 倒牌）；
    /// 他家收起明牌与对局一致；展开明牌时他家仍走 ShowCards 区国标和牌张移入（与步进相同）。
    /// </summary>
    public IEnumerator PlayRecordHepaiReveal(HepaiPresentationRequest request) {
        if (request == null) yield break;
        if (request.HepaiPlayerHand == null || request.HepaiPlayerHand.Length == 0) yield break;
        if (request.RestoreRecordHandFromSnapshot) {
            // The rule's replay reducer has removed the uncommitted added tile
            // from its owner and appended it to the winner. Rebuild both sides;
            // hasRonWinningTile pins the winner's last tile in expanded mode.
            var recorder = GameRecordManager.Instance;
            if (recorder != null && recorder.recordPlayer_to_info.TryGetValue("self", out var self)) {
                GameCanvas.Instance.ChangeHandCards("InitHandCardsFromRecord", 0, self.tileList.ToArray(), null);
                InitHandCardsFromRecordImmediate();
            }
        }
        ResetHandRevealAnimators();
        bool isSelfWinner = request.WinnerPosition == "self";
        if ((isSelfWinner || !request.IsRecordShowCardsExpanded)
            && request.WinTileMode == HepaiWinTilePresentMode.GuobiaoRonTravelFromRiver
            && RecordRonSourceAlreadyPresented(request)) {
            request.WinTileMode = HepaiWinTilePresentMode.RonInstantThenPause;
        }
        // 自家和牌：无论是否展开明牌，均与对局相同的全套倒牌演出
        if (isSelfWinner) {
            yield return PlayHepaiHandReveal(request);
            yield break;
        }
        if (request.IsRecordShowCardsExpanded) {
            if (request.IsQianggang) {
                RecycleRobbedAddedKongSource(request);
            }
            if (ShouldRecordShowCardsGuobiaoWinMove(request)) {
                yield return CoRecordShowCardsGuobiaoWinTileToDrawSlot(request);
            }
            yield return new WaitForSeconds(HepaiRevealTiming.RecordShowCardsPanelDelaySeconds);
            yield break;
        }
        yield return PlayHepaiHandReveal(request);
    }
    // 回放逐家展示多响结算。第一家的真实来源已移入手牌后，后续赢家使用展示副本。
    // 仍有真实河牌/加杠牌时优先使用它，避免把后续另一张同牌误认成同一来源。
    private bool RecordRonSourceAlreadyPresented(HepaiPresentationRequest request) {
        int tile = request.HepaiTile;
        if (request.HuClass == "hu_self" || tile < 10) return false;
        if (ResolveLastDiscardObject(request.DiscardPlayerPosition, tile) != null
            || TryResolveJiagangSourceObject(request.DiscardPlayerPosition, tile) != null) return false;
        var record = GameRecordManager.Instance;
        if (record == null) return false;
        foreach (var pair in record.recordPlayer_to_info) {
            var player = pair.Value;
            if (pair.Key != request.WinnerPosition && player != null && player.hasRonWinningTile
                && player.tileList != null && player.tileList.Count > 0
                && player.tileList[player.tileList.Count-1] == tile) return true;
        }
        return false;
    }
    private static bool ShouldRecordShowCardsGuobiaoWinMove(HepaiPresentationRequest request) {
        if (request == null || !request.IsRecordShowCardsExpanded) return false;
        if (request.WinnerPosition == "self") return false;
        if (request.IsCuoheRon) return false;
        // A rule can use an instant reveal when no river/committed kong source exists.
        // Keep the existing TsumoTravel draw-slot path for ordinary expanded replay.
        if (request.WinTileMode == HepaiWinTilePresentMode.RonInstantThenPause) return false;
        return HepaiRevealDirector.RonWinTileTravelsFromRiver(request.RecordRule);
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
            RoundEndPresentation.Instance.ShowSelfGameplayControlAndResyncHand3D();
            return;
        }
        int count = 0;
        if (NormalGameStateManager.Instance.player_to_info.TryGetValue(winnerPosition, out var info)) {
            count = info.hand_tiles_count;
        }
        for (int i = 0; i < count; i++) {
            Get3DTile(winnerPosition, "init");
        }
    }
    /// <summary>
    /// 日麻荒牌流局：听牌玩家手牌倒下，四家都按服务端下发的真实手牌渲染牌面。
    /// </summary>
    public IEnumerator RoundEndRevealTenpaiHandsAndPlayExpandAnimation(Dictionary<int, int[]> tenpaiTilesByPlayerIndex) {
        if (tenpaiTilesByPlayerIndex == null || tenpaiTilesByPlayerIndex.Count == 0) yield break;
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
        Tile3D winTile3D = winObj.GetComponent<Tile3D>();
        if (winTile3D != null) winTile3D.SetBackOrientationUpright();
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
            Tile3D riverTile3D = riverTile.GetComponent<Tile3D>();
            // 和牌倒牌立牌：牌背统一转 180°（旅行途中背图随牌转动）。
            if (riverTile3D != null) riverTile3D.SetBackOrientationUpright();
            yield return CoLerpTransform(riverTile.transform, startPos, startRot, endPos, endRot, HepaiRevealTiming.TravelSeconds);
            yield break;
        }
        Debug.LogError(
            $"CoGuobiaoRonTravelFromRiver: 荣和取河/加杠牌失败，不 spawn 兜底 winner={winnerPosition}, discarder={discardPlayerPosition}, tile={winTileId}");
        yield return new WaitForSeconds(HepaiRevealTiming.TravelSeconds);
    }
    /// <summary>
    /// 牌谱跳转重建后：为荣和演出对齐弃牌/加杠登记。
    /// 优先已有弃牌登记；否则牌谱专用河末张登记；再登记抢杠加杠牌。不做全河 id 扫描。
    /// </summary>
    public void SyncRecordLastDiscardForRon(string sourcePlayerPosition, int expectedTileId) {
        if (expectedTileId < 10 || string.IsNullOrEmpty(sourcePlayerPosition)) return;
        if (ResolveLastDiscardObject(sourcePlayerPosition, expectedTileId) != null) {
            return;
        }
        if (TryRegisterRecordRiverLastDiscard(sourcePlayerPosition, expectedTileId) != null) {
            return;
        }
        GameObject jiagangObj = FindJiagangTileObject(sourcePlayerPosition, expectedTileId);
        if (jiagangObj != null) {
            RegisterLastJiagang(sourcePlayerPosition, jiagangObj, expectedTileId);
            return;
        }
        // 同一张牌的第二/第三条和牌节点没有新的河牌来源，无需再次登记。
        int ronWinners = 0;
        if (GameRecordManager.Instance != null) {
            foreach (var player in GameRecordManager.Instance.recordPlayer_to_info.Values) {
                if (player != null && player.hasRonWinningTile && player.tileList.Count > 0
                    && player.tileList[player.tileList.Count-1] == expectedTileId && ++ronWinners > 1) return;
            }
        }
        Debug.LogWarning(
            $"SyncRecordLastDiscardForRon: 无法为荣和建立登记 source={sourcePlayerPosition}, tile={expectedTileId}");
    }
    private GameObject TryTakeLastDiscardObjectForRon(int expectedTileId, string discardPlayerPosition) {
        GameObject obj = ResolveLastDiscardObject(discardPlayerPosition, expectedTileId);
        bool fromRegisteredRiver = obj != null;
        if (obj == null) {
            obj = TryResolveJiagangSourceObject(discardPlayerPosition, expectedTileId);
        }
        if (obj == null) {
            Debug.LogWarning(
                $"TryTakeLastDiscardObjectForRon: 未认到河牌/加杠牌，放弃取牌 discarder={discardPlayerPosition}, tile={expectedTileId}");
            return null;
        }
        if (fromRegisteredRiver) {
            OnLastDiscardTaken(discardPlayerPosition);
        } else {
            ClearLastJiagangIfMatches(discardPlayerPosition, obj);
        }
        return obj;
    }
    private Vector3 GetTsumoSpawnWorldPosition(string playerPosition, Transform cardsPosition) {
        GetRecordHandLayoutDirections(playerPosition, out Vector3 widthDir, out _);
        int childCount = cardsPosition.childCount;
        return PlaceTileOnTable(HandRowOrigin(cardsPosition, widthDir) + HandDrawSlotOffset(childCount) * widthDir, RecordHandTileRotation(playerPosition));
    }
    private Vector3 GetRecordHandSlotWorldPosition(string playerPosition, Transform cardsPosition, int slotIndex) {
        GetRecordHandLayoutDirections(playerPosition, out Vector3 widthDir, out Vector3 heightDir);
        int cardsPerRow = GetRevealedHandCardsPerRow();
        int row = slotIndex / cardsPerRow;
        int col = slotIndex % cardsPerRow;
        float colOffset = ComputeRowCenterOffset(cardsPosition, row, col, cardsPerRow, false, useHandSpacing: true);
        Vector3 pos = HandRowOrigin(cardsPosition, widthDir);
        pos += widthDir.normalized * colOffset;
        pos += heightDir.normalized * handRowStep * row;
        return PlaceTileOnTable(pos, RecordHandTileRotation(playerPosition));
    }
    private void GetRecordHandLayoutDirections(string playerPosition, out Vector3 widthDir, out Vector3 heightDir) {
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
    private IEnumerator CoLerpTransform(Transform t, Vector3 fromPos, Quaternion fromRot, Vector3 toPos, Quaternion toRot, float duration) {
        if (t == null) yield break;
        Transform expectedParent = t.parent;
        Tile3D tileState = t.GetComponent<Tile3D>();
        uint lease = tileState != null ? tileState.PoolLeaseVersion : 0;
        if (!CanDriveTileTravel(t, expectedParent, tileState, lease)) yield break;
        if (duration <= 0f) {
            t.SetPositionAndRotation(KeepMovingTileAboveTable(t, toPos, toRot), toRot);
            yield break;
        }
        float elapsed = 0f;
        while (elapsed < duration) {
            if (!CanDriveTileTravel(t, expectedParent, tileState, lease)) yield break;
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / duration);
            Quaternion rotation = Quaternion.Slerp(fromRot, toRot, u);
            Vector3 position = KeepMovingTileAboveTable(t, Vector3.Lerp(fromPos, toPos, u), rotation);
            t.SetPositionAndRotation(position, rotation);
            yield return null;
        }
        if (!CanDriveTileTravel(t, expectedParent, tileState, lease)) yield break;
        t.SetPositionAndRotation(KeepMovingTileAboveTable(t, toPos, toRot), toRot);
    }

    private static bool CanDriveTileTravel(Transform tile, Transform expectedParent, Tile3D state, uint lease) {
        return IsCardDrivable(tile, expectedParent) && tile.parent == expectedParent
            && (state == null || state.PoolLeaseVersion == lease);
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
        BeginHandSurfacePlacement(panel);
        anim.ResetTrigger(panel.handRevealExpandTrigger);
        anim.SetTrigger(panel.handRevealExpandTrigger);
    }
    private void ForceHandRevealIdle(PosPanel3D panel) {
        if (panel == null || panel.handRevealAnimator == null) return;
        ResetHandSurfacePlacement(panel);
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
    private void LayRoundEndFaceHandAtPosition(string playerPosition, int[] hepaiPlayerHand,
        GameObject winningTile = null, bool spawnWinningCopy = true) {
        int last = hepaiPlayerHand[hepaiPlayerHand.Length - 1];
        int[] closed = new int[hepaiPlayerHand.Length - 1];
        System.Array.Copy(hepaiPlayerHand, closed, closed.Length);
        closed = OrderRoundEndClosedHand(playerPosition, closed);
        SuspendWashizuPublicHand(playerPosition);
        PosPanel3D panel = GetPosPanel(playerPosition);
        ForceHandRevealIdle(panel);
        Transform target = panel.cardsPosition;
        ClearHandCardsPosition(target);
        for (int i = 0; i < closed.Length; i++) {
            Set3DTile(closed[i], target, "Record", playerPosition);
        }
        if (winningTile != null) {
            winningTile.transform.SetParent(target, worldPositionStays: true);
            winningTile.GetComponent<Tile3D>()?.SetBackOrientationUpright();
            winningTile.transform.SetPositionAndRotation(
                GetRecordHandSlotWorldPosition(playerPosition, target, closed.Length),
                RecordHandTileRotation(playerPosition));
        } else if (spawnWinningCopy) {
            Set3DTile(last, target, "Record", playerPosition, usePresentationCopy: true);
        }
    }
    private void LayRoundEndClosedFaceHandAtPosition(string playerPosition, IList<int> handTiles) {
        int[] sorted = OrderRoundEndClosedHand(playerPosition, handTiles);
        SuspendWashizuPublicHand(playerPosition);
        PosPanel3D panel = GetPosPanel(playerPosition);
        ForceHandRevealIdle(panel);
        Transform target = panel.cardsPosition;
        ClearHandCardsPosition(target);
        for (int i = 0; i < sorted.Length; i++) {
            Set3DTile(sorted[i], target, "Record", playerPosition);
        }
    }

    private int[] OrderRoundEndClosedHand(string playerPosition, IList<int> closedTiles) {
        if (WashizuTile.IsRealtimeHandOrder) {
            var record = GameRecordManager.Instance;
            var layout = record != null && record.gameObject.activeSelf
                ? record.GetWashizuRecordLayout(playerPosition, true) : null;
            if (layout != null && layout.tiles.Length == closedTiles.Count + 1 && layout.drawn_key != 0) {
                var order = new System.Collections.Generic.List<int>();
                for (int i=0;i<layout.tiles.Length;i++) if (layout.keys[i] != layout.drawn_key) order.Add(layout.tiles[i]);
                return WashizuHandOrder.OrderSubset(closedTiles, order);
            }
            // Live settlement payloads are already ordered by the server. Replay
            // derives the same order from wo ticks without sorting logical hands.
            return WashizuHandOrder.OrderSubset(closedTiles, layout?.tiles ?? closedTiles);
        }
        // Capture the public ordering before SuspendWashizuPublicHand releases it.
        // Self hands and expanded replay hands have no concealed public row.
        washizuPublicHands.TryGetValue(playerPosition, out int[] publicHand);
        return WashizuTile.OrderRevealedHand(closedTiles, publicHand);
    }
}
