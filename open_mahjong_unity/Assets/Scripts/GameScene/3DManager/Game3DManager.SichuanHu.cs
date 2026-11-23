using System.Collections;
using System.Collections.Generic;
using UnityEngine;
/// <summary>
/// 血战中途和牌张的移动与重建。广东推广双打按出牌者定位点和牌。
/// </summary>
public partial class Game3DManager {
    private const int SichuanBuhuaCardsPerRow = 4;
    private static bool UsesRobbedKongMeldSource {
        get {
            GameRecordManager.ResolveActionRuleContext(null, null, out _, out string subRule);
            return subRule == GuobiaoGameState.BloodBattleSubRule
                || subRule == NanqueGameState.BloodBattleSubRule || subRule == GuangdongMilRules.Doubles || SichuanLobby.IsDoubles(subRule);
        }
    }

    public void RestoreBloodBattleWinMarker(string position, int tile, bool zimo, bool multi, string sourcePosition = null) {
        PosPanel3D panel = GetPosPanel(position);
        if (panel?.buhuaPosition == null) return;
        ClearSichuanHuBuhuaMarkers(position);
        GameObject marker = SpawnSichuanBuhuaWinTileObject(panel, position, tile, zimo, multi, sourcePosition);
        if (marker != null) marker.name = "SichuanHu_Restored";
    }
    public IEnumerator PlaySichuanMidGameHu(HepaiPresentationRequest request) {
        if (request == null || string.IsNullOrEmpty(request.WinnerPosition)) yield break;
        switch (request.WinTileMode) {
            case HepaiWinTilePresentMode.SichuanZimoToBuhuaFaceDown:
                yield return CoSichuanZimoToBuhua(request);
                break;
            case HepaiWinTilePresentMode.SichuanRonSingleToBuhua:
                yield return CoSichuanRonSingleToBuhua(request);
                break;
            case HepaiWinTilePresentMode.SichuanRonMultiToBuhua:
                yield return CoSichuanRonMultiToBuhua(request);
                break;
            default:
                yield break;
        }
    }
    /// <summary>流局查牌：四家手牌同时展开（和牌者末张为和牌张，先清补花区和牌标记避免重复）。</summary>
    public void RevealSichuanLiujuAllHands(Dictionary<int, int[]> handsByPlayerIndex) {
        if (handsByPlayerIndex == null) return;
        GameRecordManager record = GameRecordManager.Instance;
        var seatMap = record != null && record.gameObject.activeInHierarchy
            ? record.indexToPosition
            : NormalGameStateManager.Instance.indexToPosition;
        var panels = new List<PosPanel3D>();
        // 先归还全部旧明牌，再逐家重建，避免尚未清理的另一家手牌占住同牌的四张实体。
        foreach (var kvp in handsByPlayerIndex) {
            if (kvp.Value == null || kvp.Value.Length == 0) continue;
            if (!seatMap.TryGetValue(kvp.Key, out string pos)) continue;
            PosPanel3D panel = GetPosPanel(pos);
            if (panel == null) continue;
            ForceHandRevealIdle(panel);
            ClearHandCardsPosition(panel.cardsPosition);
            // Replay hands occupy a separate face-up row. Return those tiles too
            // before rebuilding all four hands for the final reveal.
            ClearHandCardsPosition(panel.ShowCardsPosition);
            ClearSichuanHuBuhuaMarkers(pos);
        }
        foreach (var kvp in handsByPlayerIndex) {
            if (!seatMap.TryGetValue(kvp.Key, out string pos)) continue;
            int[] tiles = kvp.Value;
            if (tiles == null || tiles.Length == 0) continue;
            // A completed hand keeps 2 mod 3 concealed tiles, including hands
            // with melds. Its winning tile may be shared by multiple winners.
            if (tiles.Length % 3 == 2) {
                LayRoundEndFaceHandAtPosition(pos, tiles);
            } else {
                LayRoundEndClosedFaceHandAtPosition(pos, tiles);
            }
            PosPanel3D panel = GetPosPanel(pos);
            if (panel != null) panels.Add(panel);
        }
        foreach (PosPanel3D panel in panels) {
            PlayHandRevealAnimation(panel);
        }
    }
    private void ClearSichuanHuBuhuaMarkers(string playerPosition) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        if (panel?.buhuaPosition == null) return;
        var toReturn = new List<GameObject>();
        for (int i = panel.buhuaPosition.childCount - 1; i >= 0; i--) {
            Transform child = panel.buhuaPosition.GetChild(i);
            if (child.name.StartsWith("SichuanHu_")) {
                toReturn.Add(child.gameObject);
            }
        }
        foreach (GameObject obj in toReturn) {
            MahjongObjectPool.Instance.Return(-1, obj);
        }
    }
    /// <summary>与鸣牌/补花一致：副露竖牌世界旋转（暗面在此基础上 Y 轴翻面）。</summary>
    private static Quaternion GetMeldVerticalWorldRotation(string playerPosition) {
        if (playerPosition == "self") return Quaternion.Euler(90, 0, 180);
        if (playerPosition == "left") return Quaternion.Euler(90, 0, 90);
        if (playerPosition == "top") return Quaternion.Euler(90, 0, 0);
        if (playerPosition == "right") return Quaternion.Euler(90, 0, 270);
        return Quaternion.identity;
    }
    private IEnumerator CoSichuanZimoToBuhua(HepaiPresentationRequest request) {
        string playerPosition = request.WinnerPosition;
        int tileId = request.HepaiTile;
        PosPanel3D panel = GetPosPanel(playerPosition);
        if (panel == null || panel.buhuaPosition == null) yield break;
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        if (playerPosition == "self") {
            if (gsm != null && tileId >= 10) {
                gsm.selfHandTiles.Remove(tileId);
                GameCanvas.Instance.ChangeHandCards("RemoveHuWinTile", tileId, null, null);
            }
            GameObject removedHandTile = tileId >= 10
                ? TryDetachSelfHandTileById(panel.cardsPosition, tileId)
                    ?? TryTakeLastHandTileObject(panel.cardsPosition)
                : TryTakeLastHandTileObject(panel.cardsPosition);
            if (removedHandTile != null) {
                MahjongObjectPool.Instance.Return(-1, removedHandTile);
            }
            GetZimoOutputSpawnPose(panel, out Vector3 spawnPos);
            GameObject winTile = SpawnSichuanZimoTileFromOutput(playerPosition, tileId, spawnPos, panel.buhuaPosition);
            if (winTile == null) {
                Debug.LogWarning($"[SichuanHu] 自家自摸 outputPos 生成失败，fallback spawn");
                winTile = SpawnSichuanBuhuaWinTileObject(panel, playerPosition, tileId >= 10 ? tileId : 0, true, false);
            }
            if (winTile != null) {
                yield return CoAnimateTileToBuhua(winTile, panel, playerPosition, tileId, faceDown: true, dimmed: false, spawnPos);
            }
            yield break;
        }
        if (gsm != null && gsm.player_to_info.TryGetValue(playerPosition, out PlayerInfoClass info)) {
            info.hand_tiles_count = Mathf.Max(0, info.hand_tiles_count - 1);
        }
        Transform handPosition = SichuanWinHandRow(panel, playerPosition);
        GameObject handTile = TryTakeLastHandTileObject(handPosition);
        if (handTile == null) {
            Debug.LogWarning($"[SichuanHu] 他家自摸取末张手牌失败 winner={playerPosition}，fallback spawn");
            handTile = SpawnSichuanBuhuaWinTileObject(panel, playerPosition, tileId, true, false);
            if (handTile != null) handTile.name = "SichuanHu_Restored";
            yield break;
        }
        PrepareTileForBuhuaMove(handTile, panel.buhuaPosition, playerPosition);
        Vector3 startPos = handTile.transform.position;
        yield return CoAnimateTileToBuhua(handTile, panel, playerPosition, tileId, faceDown: true, dimmed: false, startPos);
    }
    /// <summary>自摸和牌张从 outputPos 飞出；生成时与补花/鸣牌相同朝向，落位后再翻面。</summary>
    private void GetZimoOutputSpawnPose(PosPanel3D panel, out Vector3 pos) {
        Transform output = panel.outputPos != null ? panel.outputPos : panel.cardsPosition;
        pos = output.position;
    }
    private GameObject SpawnSichuanZimoTileFromOutput(
        string playerPosition, int tileId, Vector3 pos, Transform buhuaParent) {
        Quaternion rotation = GetMeldVerticalWorldRotation(playerPosition);
        GameObject cardObj = playerPosition == "self" && tileId >= 10
            ? MahjongObjectPool.Instance.Spawn(tileId, pos, rotation)
            : MahjongObjectPool.Instance.SpawnBlankTile(pos, rotation);
        if (cardObj == null) return null;
        TableWorldObjectMount.AttachPreservingWorld(cardObj.transform, buhuaParent);
        cardObj.transform.SetPositionAndRotation(pos, rotation);
        RegisterSichuanHuCard(cardObj, tileId, dimmed: false);
        return cardObj;
    }
    private IEnumerator CoSichuanRonSingleToBuhua(HepaiPresentationRequest request) {
        string winnerPos = request.WinnerPosition;
        int tileId = request.HepaiTile;
        PosPanel3D winnerPanel = GetPosPanel(winnerPos);
        if (winnerPanel == null || winnerPanel.buhuaPosition == null) yield break;
        GameObject riverTile = DetachRonSourceObject(request.DiscardPlayerPosition, tileId, UsesRobbedKongMeldSource && request.IsQianggang);
        if (riverTile != null) {
            if (UsesRobbedKongMeldSource && request.IsQianggang) SyncGuobiaoBloodRobbedKong(request.DiscardPlayerPosition, tileId);
            else TableMirror.Current.SyncRonDiscardRemoved(request.DiscardPlayerPosition, tileId);
            yield return CoAnimateTileToBuhua(riverTile, winnerPanel, winnerPos, tileId, faceDown: false, dimmed: false, null, request.DiscardPlayerPosition);
        } else {
            Debug.LogError($"[SichuanHu] 荣和取河/加杠牌失败，不 spawn 兜底 tileId={tileId} winner={winnerPos} discarder={request.DiscardPlayerPosition}");
        }
    }
    private IEnumerator CoSichuanRonMultiToBuhua(HepaiPresentationRequest request) {
        string winnerPos = request.WinnerPosition;
        int tileId = request.HepaiTile;
        PosPanel3D winnerPanel = GetPosPanel(winnerPos);
        if (winnerPanel == null || winnerPanel.buhuaPosition == null) yield break;
        if (!TryGetWinTileSpawnPose(request, out Vector3 startPos, out Quaternion startRot)) {
            startPos = winnerPanel.buhuaPosition.position;
            startRot = Quaternion.identity;
        }
        int spawnId = tileId >= 10 ? tileId : 0;
        // Several winners share one physical tile. Their display markers must
        // not consume additional copies from the four-tile physical pool.
        GameObject clone = MahjongObjectPool.Instance.SpawnPresentationTile(spawnId, startPos, startRot);
        if (clone == null) yield break;
        yield return CoAnimateTileToBuhua(clone, winnerPanel, winnerPos, tileId, faceDown: false, dimmed: true, startPos, request.DiscardPlayerPosition);
        if (request.RecycleDiscardAfterPresent) {
            RecyclePresentedWinTileSource(request, tileId);
        }
    }
    private bool TryGetWinTileSpawnPose(HepaiPresentationRequest request, out Vector3 pos, out Quaternion rot) {
        if (request != null && request.IsQianggang) {
            GameObject jiagangObj = TryResolveJiagangSourceObject(request.DiscardPlayerPosition, request.HepaiTile);
            if (jiagangObj != null) {
                pos = jiagangObj.transform.position;
                rot = jiagangObj.transform.rotation;
                return true;
            }
        }
        return TryGetRiverDiscardPose(request?.DiscardPlayerPosition, out pos, out rot);
    }
    private void RecyclePresentedWinTileSource(HepaiPresentationRequest request, int tileId) {
        if (request != null && request.IsQianggang) {
            GameObject obj = TryResolveJiagangSourceObject(request.DiscardPlayerPosition, tileId);
            if (obj != null) {
                ClearLastJiagangIfMatches(request.DiscardPlayerPosition, obj);
                TableWorldObjectMount.AttachDetached(obj.transform, true);
                if (UsesRobbedKongMeldSource) SyncGuobiaoBloodRobbedKong(request.DiscardPlayerPosition, tileId);
                else TableMirror.Current.SyncRonDiscardRemoved(request.DiscardPlayerPosition, tileId);
                MahjongObjectPool.Instance.Return(-1, obj);
            }
            return;
        }
        RecycleRiverDiscard(request?.DiscardPlayerPosition, tileId);
    }
    private static void SyncGuobiaoBloodRobbedKong(string source, int tile) {
        PlayerInfoClass player = TableMirror.Current.Info(source);
        if (player?.combination_tiles == null || player.combination_masks == null) return;
        for (int i = 0; i < player.combination_tiles.Count; i++) {
            if (player.combination_tiles[i] != $"g{tile}") continue;
            var mask = new List<int>(player.combination_masks[i]);
            for (int j = 0; j < mask.Count; j += 2) {
                if (mask[j] != 3) continue;
                mask.RemoveRange(j, 2);
                player.combination_masks[i] = mask.ToArray();
                player.combination_tiles[i] = $"k{tile}";
                break;
            }
        }
    }
    private void PrepareTileForBuhuaMove(GameObject cardObj, Transform buhuaParent, string playerPosition) {
        if (cardObj == null) return;
        Tile3D tile3D = cardObj.GetComponent<Tile3D>();
        tile3D?.ResetConcealedState();
        Quaternion finalRot = GetMeldVerticalWorldRotation(playerPosition);
        TableWorldObjectMount.AttachPreservingWorld(cardObj.transform, buhuaParent);
        cardObj.transform.rotation = finalRot;
    }
    private IEnumerator CoAnimateTileToBuhua(
        GameObject cardObj, PosPanel3D panel, string playerPosition,
        int tileId, bool faceDown, bool dimmed, Vector3? explicitStartPos, string sourcePosition = null) {
        if (cardObj == null || panel?.buhuaPosition == null) yield break;
        // 自摸牌在起飞前已挂到补花区，不能把它自身再算作一个已占用牌位。
        int slot = panel.buhuaPosition.childCount - (cardObj.transform.parent == panel.buhuaPosition ? 1 : 0);
        ComputeSichuanBuhuaSlot(panel.buhuaPosition, playerPosition, out Vector3 endPos, out _, slot);
        Quaternion finalRot = faceDown
            ? GetMeldVerticalWorldRotation(playerPosition)
            : ComputeSichuanBuhuaSlotRotation(panel.buhuaPosition, playerPosition);
        bool besideHand = TryApplyGuangdongWinPose(panel, playerPosition, sourcePosition, faceDown, ref endPos, ref finalRot);
        if (faceDown) {
            PrepareTileForBuhuaMove(cardObj, panel.buhuaPosition, playerPosition);
        } else {
            TableWorldObjectMount.AttachPreservingWorld(cardObj.transform, panel.buhuaPosition);
        }
        Vector3 startPos = explicitStartPos ?? cardObj.transform.position;
        Quaternion startRot = faceDown ? finalRot : cardObj.transform.rotation;
        cardObj.transform.SetPositionAndRotation(startPos, startRot);
        if (!faceDown) {
            RegisterSichuanHuCard(cardObj, tileId, dimmed);
        }
        yield return CoLerpTransform(cardObj.transform, startPos, startRot, endPos, finalRot, HepaiRevealTiming.TravelSeconds);
        if (besideHand && playerPosition == "self")
            TryApplyGuangdongWinPose(panel, playerPosition, sourcePosition, faceDown, ref endPos, ref finalRot);
        cardObj.transform.SetPositionAndRotation(endPos, finalRot);
        if (faceDown && besideHand) {
            RegisterSichuanHuCard(cardObj, MahjongObjectPool.GetTile3D(cardObj).GetTileId(), dimmed);
        } else if (faceDown) {
            ApplyConcealedFaceDownLikeMeld(cardObj, tileId, dimmed);
        } else if (dimmed) {
            ApplySichuanHuCardDim(cardObj);
        }
        cardObj.name = $"SichuanHu_{panel.buhuaPosition.childCount}";
    }
    private static GameObject TryTakeLastHandTileObject(Transform cardsPosition) {
        if (cardsPosition == null || cardsPosition.childCount == 0) return null;
        GameObject obj = cardsPosition.GetChild(cardsPosition.childCount - 1).gameObject;
        TableWorldObjectMount.AttachDetached(obj.transform, true);
        return obj;
    }
    private GameObject TryDetachSelfHandTileById(Transform cardPosition, int tileId) {
        if (cardPosition == null) return null;
        for (int i = 0; i < cardPosition.childCount; i++) {
            Transform child = cardPosition.GetChild(i);
            Tile3D t3 = child.GetComponent<Tile3D>();
            if (t3 != null && t3.GetTileId() == tileId) {
                GameObject obj = child.gameObject;
                TableWorldObjectMount.AttachDetached(obj.transform, true);
                return obj;
            }
        }
        return null;
    }
    private bool TryGetRiverDiscardPose(string discarderPos, out Vector3 pos, out Quaternion rot) {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        GameObject obj = PeekLastDiscardObject(discarderPos);
        if (obj == null) return false;
        pos = obj.transform.position;
        rot = obj.transform.rotation;
        return true;
    }
    /// <summary>预览用：仅认 RegisterLastDiscard 登记对象，不取河末张。</summary>
    private GameObject PeekLastDiscardObject(string discarderPos) {
        if (string.IsNullOrEmpty(discarderPos)) return null;
        if (_lastDiscardObjByPlayer.TryGetValue(discarderPos, out GameObject regObj)
            && regObj != null && regObj.activeInHierarchy) {
            return regObj;
        }
        return null;
    }
    private GameObject DetachRonSourceObject(string discarderPos, int expectedTileId, bool preferAddedKong = false) {
        GameObject obj = preferAddedKong ? null : ResolveLastDiscardObject(discarderPos, expectedTileId);
        bool fromRegisteredRiver = obj != null;
        if (obj == null) {
            obj = TryResolveJiagangSourceObject(discarderPos, expectedTileId);
        }
        if (obj == null) {
            Debug.LogWarning(
                $"DetachRonSourceObject: 未认到河牌/加杠牌，放弃取牌 discarder={discarderPos}, tile={expectedTileId}");
            return null;
        }
        TableWorldObjectMount.AttachDetached(obj.transform, true);
        if (fromRegisteredRiver) {
            OnLastDiscardTaken(discarderPos);
        } else {
            ClearLastJiagangIfMatches(discarderPos, obj);
        }
        return obj;
    }
    private void RecycleRiverDiscard(string discarderPos, int tileId) {
        GameObject obj = DetachRonSourceObject(discarderPos, tileId);
        if (obj == null) return;
        TableMirror.Current.SyncRonDiscardRemoved(discarderPos, tileId);
        MahjongObjectPool.Instance.Return(-1, obj);
    }
    private GameObject SpawnSichuanBuhuaWinTileObject(
        PosPanel3D panel, string playerPosition, int tileId, bool faceDown, bool dimmed, string sourcePosition = null) {
        ComputeSichuanBuhuaSlot(panel.buhuaPosition, playerPosition, out Vector3 pos, out Quaternion rotation);
        if (faceDown) rotation = GetMeldVerticalWorldRotation(playerPosition);
        bool besideHand = TryApplyGuangdongWinPose(panel, playerPosition, sourcePosition, faceDown, ref pos, ref rotation);
        GameObject cardObj;
        if (faceDown) {
            if ((playerPosition == "self" || (besideHand && SichuanWinHandRow(panel, playerPosition) == panel.ShowCardsPosition)) && tileId >= 10) {
                cardObj = MahjongObjectPool.Instance.Spawn(tileId, pos, rotation);
            } else {
                cardObj = MahjongObjectPool.Instance.SpawnBlankTile(pos, rotation);
            }
        } else {
            int spawnId = tileId >= 10 ? tileId : 0;
            // Dimmed multi-ron markers are copies of one shared winning tile,
            // also when a replay jump or reconnect restores them directly.
            cardObj = dimmed
                ? MahjongObjectPool.Instance.SpawnPresentationTile(spawnId, pos, rotation)
                : MahjongObjectPool.Instance.Spawn(spawnId, pos, rotation);
        }
        if (cardObj == null) return null;
        TableWorldObjectMount.AttachPreservingWorld(cardObj.transform, panel.buhuaPosition);
        cardObj.transform.SetPositionAndRotation(pos, rotation);
        if (faceDown && !besideHand) {
            ApplyConcealedFaceDownLikeMeld(cardObj, tileId, dimmed);
        } else {
            RegisterSichuanHuCard(cardObj, MahjongObjectPool.GetTile3D(cardObj).GetTileId(), dimmed);
        }
        return cardObj;
    }
    private Transform SichuanWinHandRow(PosPanel3D panel, string seat) {
        var shown = panel.ShowCardsPosition;
        return seat != "self" && IsRecordShowCardsModeActive() && shown != null && shown.childCount > 0
            ? shown : panel.cardsPosition;
    }

    private bool TryApplyGuangdongWinPose(PosPanel3D panel, string seat, string source, bool faceDown, ref Vector3 pos, ref Quaternion rotation) {
        GameRecordManager.ResolveActionRuleContext(null, null, out _, out string subRule);
        if (subRule != GuangdongMilRules.Doubles) return false;
        var row = SichuanWinHandRow(panel, seat);
        if (row == null) return false;
        string[] ring = { "self", "right", "top", "left" };
        int from = System.Array.IndexOf(ring, source);
        if (!faceDown && from < 0) return false;
        int distance = (System.Array.IndexOf(ring, seat) - from + 4) % 4;
        GetRecordHandLayoutDirections(seat, out var width, out var depth);
        var origin = HandRowOrigin(row, width);
        var record = GameRecordManager.Instance;
        int groups = record != null && record.gameObject.activeInHierarchy && record.recordPlayer_to_info.TryGetValue(seat, out var player)
            ? player.combinationTiles.Count : TableMirror.Current.Info(seat).combination_tiles.Count;
        int handCount = Mathf.Max(1, 13 - 3 * groups);
        Quaternion upright = RiverTileWorldRotation(seat);
        if (faceDown) {
            var last = row.childCount > 0 ? row.GetChild(row.childCount - 1) : null;
            var hudCamera = seat == "self" ? Camera.main : null;
            var handRotation = hudCamera != null ? hudCamera.transform.rotation
                : last != null ? last.rotation : RecordHandTileRotation(seat);
            rotation = handRotation * Quaternion.AngleAxis(-90f, Vector3.forward);
            var lastPos = last != null ? last.position : PlaceTileOnTable(origin + width * ((handCount - 1) * handStep), handRotation);
            var handBody = TileBodyAt(lastPos, handRotation);
            pos = PlaceTileOnTable(lastPos, rotation);
            var winBody = TileBodyAt(pos, rotation);
            pos += width * (TileSurfacePlacement.MaximumProjection(tileBodyBounds, handBody, width)
                - TileSurfacePlacement.MinimumProjection(tileBodyBounds, winBody, width) + HandColumnGap);
            pos += depth * (TileSurfacePlacement.MaximumProjection(tileBodyBounds, handBody, depth)
                - TileSurfacePlacement.MaximumProjection(tileBodyBounds, winBody, depth));
            if (hudCamera != null && GameCanvas.Instance != null
                && GameCanvas.Instance.TryGetHandScreenBounds(handCount, out var hud)) {
                float gapPixels = hud.width / handCount * HandColumnGap / (handStep - HandColumnGap);
                var target = new Vector3(hud.xMax + gapPixels, hud.yMin, 0f);
                pos += HandScreenAlignment(hudCamera, target, TileBodyAt(pos, rotation), -depth, false, -1);
                pos += HandScreenAlignment(hudCamera, target, TileBodyAt(pos, rotation), width, true, -1);
            }
            return true;
        }
        rotation = distance != 3 ? Quaternion.AngleAxis(90, Vector3.up) * upright : upright;
        Vector3 handCenter = GetDoublesHandBounds(row, handCount, origin, width, upright, out float left, out float right);
        var body = TileBodyAt(origin, rotation);
        float nearEdge = TileSurfacePlacement.MinimumProjection(tileBodyBounds, body, width);
        float farEdge = TileSurfacePlacement.MaximumProjection(tileBodyBounds, body, width);
        float gap = .55f * handStep;
        Vector3 point;
        if (distance == 2) {
            point = origin + width * ((left + right - nearEdge - farEdge) * .5f) - depth * (2.25f * handStep);
        } else {
            point = origin + width * (distance == 1 ? left - gap - farEdge : right + gap - nearEdge);
        }
        pos = PlaceTileOnTable(point, rotation);
        var camera = Camera.main;
        Vector2? singleCenter = null;
        if (camera != null) {
            var placed = TileBodyAt(pos, rotation);
            Vector3 center = placed.MultiplyPoint3x4(tileBodyBounds.center);
            if (seat == "self" && GameCanvas.Instance != null &&
                GameCanvas.Instance.TryGetHandScreenBounds(handCount, out var hud)) {
                if (handCount == 1) singleCenter = hud.center;
                Vector3 target = camera.WorldToScreenPoint(center);
                int edge = 0;
                if (distance == 2) {
                    target.x = hud.center.x;
                } else {
                    bool onRight = distance == 3;
                    float uiGap = .55f * hud.width / handCount;
                    target.x = onRight ? hud.xMax + uiGap : hud.xMin - uiGap;
                    edge = onRight ? -1 : 1;
                }
                pos += HandScreenAlignment(camera, target, placed, width, true, edge);
                if (edge != 0) {
                    // 两侧玩家的长副露可能延伸到自家手牌旁，向桌边让开并保持屏幕间距。
                    var adjacent = edge > 0 ? leftPosPanel : rightPosPanel;
                    Vector3 clear = ClearWinTileOfMelds(adjacent, pos, rotation, width, depth);
                    if (clear != pos) pos = clear + HandScreenAlignment(camera, target, TileBodyAt(clear, rotation), width, true, edge);
                }
            } else if (handCount == 1 && row.childCount == 1) {
                var mesh = row.GetChild(0).GetComponentInChildren<MeshFilter>();
                if (mesh != null && mesh.sharedMesh != null)
                    singleCenter = TileScreenCenter(camera, mesh.sharedMesh.bounds, mesh.transform.localToWorldMatrix);
            } else if (distance == 2) {
                pos += HandScreenAlignment(camera, camera.WorldToScreenPoint(handCenter), placed, width, seat == "top" || seat == "self");
            }
        }
        pos = ClearWinTileOfMelds(panel, pos, rotation, width, -depth);
        if (singleCenter.HasValue) {
            bool opposite = distance == 2;
            bool horizontal = (seat == "self" || seat == "top") == opposite;
            Vector3 axis = opposite ? width : depth;
            Vector3 outward = opposite ? -depth : distance == 1 ? -width : width;
            pos = AlignSingleHandCenter(camera, singleCenter.Value, pos, rotation, axis, horizontal);
            var clear = ClearWinTileOfMelds(panel, pos, rotation, axis, outward);
            if (seat == "self" && !opposite) {
                var adjacent = distance == 1 ? leftPosPanel : rightPosPanel;
                clear = ClearWinTileOfMelds(adjacent, clear, rotation, axis, outward);
            }
            if (clear != pos) pos = AlignSingleHandCenter(camera, singleCenter.Value, clear, rotation, axis, horizontal);
        }
        return true;
    }

    private Vector3 GetDoublesHandBounds(Transform row, int handCount, Vector3 origin, Vector3 width, Quaternion upright,
        out float left, out float right) {
        left = float.PositiveInfinity;
        right = float.NegativeInfinity;
        Vector3 leftCenter = origin, rightCenter = origin;
        int count = 0;
        foreach (Transform tile in row) {
            var state = tile.GetComponent<Tile3D>();
            if (state == null || state.isRecordDrawSlotPinned) continue;
            var mesh = tile.GetComponentInChildren<MeshFilter>();
            if (mesh == null || mesh.sharedMesh == null) continue;
            var bounds = mesh.sharedMesh.bounds;
            var matrix = mesh.transform.localToWorldMatrix;
            float lo = TileSurfacePlacement.MinimumProjection(bounds, matrix, width);
            float hi = TileSurfacePlacement.MaximumProjection(bounds, matrix, width);
            var center = matrix.MultiplyPoint3x4(bounds.center);
            if (lo < left) { left = lo; leftCenter = center; }
            if (hi > right) { right = hi; rightCenter = center; }
            count++;
        }
        if (count == handCount) return (leftCenter + rightCenter) * .5f;
        // 自家手牌由 2D 绘制，重连时 3D 手牌也可能尚未建齐。
        var first = TileBodyAt(PlaceTileOnTable(origin, upright), upright);
        left = TileSurfacePlacement.MinimumProjection(tileBodyBounds, first, width);
        float span = (handCount - 1) * handStep;
        right = TileSurfacePlacement.MaximumProjection(tileBodyBounds, first, width) + span;
        return first.MultiplyPoint3x4(tileBodyBounds.center) + width * (.5f * span);
    }

    private static Vector2 TileScreenCenter(Camera camera, Bounds bounds, Matrix4x4 body) {
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        Vector3 lo = bounds.min, hi = bounds.max;
        for (int i = 0; i < 8; i++) {
            var corner = new Vector3((i & 1) == 0 ? lo.x : hi.x, (i & 2) == 0 ? lo.y : hi.y,
                (i & 4) == 0 ? lo.z : hi.z);
            Vector2 point = camera.WorldToScreenPoint(body.MultiplyPoint3x4(corner));
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return (min + max) * .5f;
    }

    private Vector3 AlignSingleHandCenter(Camera camera, Vector2 target, Vector3 pos, Quaternion rotation,
        Vector3 axis, bool horizontal) {
        // 透视下，牌身投影轮廓的中点会随落点变化。
        for (int i = 0; i < 3; i++) {
            var body = TileBodyAt(pos, rotation);
            Vector2 delta = target - TileScreenCenter(camera, tileBodyBounds, body);
            if (Mathf.Abs(horizontal ? delta.x : delta.y) < .05f) break;
            Vector2 pivot = camera.WorldToScreenPoint(body.MultiplyPoint3x4(tileBodyBounds.center));
            pos += HandScreenAlignment(camera, pivot + delta, body, axis, horizontal);
        }
        return pos;
    }

    private Vector3 HandScreenAlignment(Camera camera, Vector3 screen, Matrix4x4 body, Vector3 width, bool horizontal, int edge = 0) {
        var axis = horizontal ? camera.transform.up : camera.transform.right;
        Ray sight = camera.ScreenPointToRay(screen);
        Vector3 normal = Vector3.Cross(sight.direction, axis);
        float slope = Vector3.Dot(normal, width);
        if (Mathf.Abs(slope) < .0001f) return Vector3.zero;
        normal /= slope;
        float support = edge < 0 ? TileSurfacePlacement.MinimumProjection(tileBodyBounds, body, normal)
            : edge > 0 ? TileSurfacePlacement.MaximumProjection(tileBodyBounds, body, normal)
            : Vector3.Dot(normal, body.MultiplyPoint3x4(tileBodyBounds.center));
        return width * (Vector3.Dot(normal, sight.origin) - support);
    }

    private Vector3 ClearWinTileOfMelds(PosPanel3D panel, Vector3 position, Quaternion rotation, Vector3 across, Vector3 inward) {
        var body = TileBodyAt(position, rotation);
        float left = TileSurfacePlacement.MinimumProjection(tileBodyBounds, body, across);
        float right = TileSurfacePlacement.MaximumProjection(tileBodyBounds, body, across);
        float back = TileSurfacePlacement.MinimumProjection(tileBodyBounds, body, inward);
        float front = TileSurfacePlacement.MaximumProjection(tileBodyBounds, body, inward);
        float shift = 0f;
        foreach (var group in panel.combination3DObjects) {
            if (group == null) continue;
            foreach (Transform tile in group) {
                var mesh = tile.GetComponentInChildren<MeshFilter>();
                if (mesh == null || mesh.sharedMesh == null) continue;
                var bounds = mesh.sharedMesh.bounds;
                var matrix = mesh.transform.localToWorldMatrix;
                if (TileSurfacePlacement.MinimumProjection(bounds, matrix, across) >= right ||
                    TileSurfacePlacement.MaximumProjection(bounds, matrix, across) <= left ||
                    TileSurfacePlacement.MinimumProjection(bounds, matrix, inward) >= front) continue;
                float extent = TileSurfacePlacement.MaximumProjection(bounds, matrix, inward);
                if (extent > back) shift = Mathf.Max(shift, extent - back + ScaledTileSurfaceClearance);
            }
        }
        return position + inward * shift;
    }

    private void ComputeSichuanBuhuaSlot(Transform target, string playerPosition, out Vector3 pos, out Quaternion rotation, int? slot = null) {
        rotation = ComputeSichuanBuhuaSlotRotation(target, playerPosition);
        Vector3 widthDir = Vector3.zero;
        Vector3 heightDir = Vector3.zero;
        if (playerPosition == "self") {
            widthDir = RightDirection;
            heightDir = BackDirection;
        } else if (playerPosition == "left") {
            widthDir = BackDirection;
            heightDir = LeftDirection;
        } else if (playerPosition == "top") {
            widthDir = LeftDirection;
            heightDir = FrontDirection;
        } else if (playerPosition == "right") {
            widthDir = FrontDirection;
            heightDir = RightDirection;
        }
        int index = slot ?? target.childCount;
        int row = index / SichuanBuhuaCardsPerRow;
        int col = index % SichuanBuhuaCardsPerRow;
        float colOffset = ComputeRowCenterOffset(target, row, col, SichuanBuhuaCardsPerRow, false);
        pos = target.position
            + widthDir.normalized * colOffset
            + heightDir.normalized * heightSpacing * row;
        pos = PlaceTileOnTable(pos, rotation);
    }
    private Quaternion ComputeSichuanBuhuaSlotRotation(Transform target, string playerPosition) {
        if (playerPosition == "self") {
            return SelfTileWorldRotation(target);
        }
        if (playerPosition == "left") return Quaternion.Euler(90, 0, 90);
        if (playerPosition == "top") return Quaternion.Euler(90, 0, 0);
        if (playerPosition == "right") return Quaternion.Euler(90, 0, 270);
        return Quaternion.identity;
    }
    /// <summary>与 ActionAnimationCoroutine 暗杠一致：先竖牌朝向，落位后 ApplyCombinationPeekState(2)。</summary>
    private void ApplyConcealedFaceDownLikeMeld(GameObject cardObj, int tileId, bool dimmed) {
        Tile3D tile3D = cardObj.GetComponent<Tile3D>();
        tile3D?.ResetConcealedState();
        tile3D?.ApplyCombinationPeekState(tileId >= 10 ? tileId : 0, 2);
        RegisterSichuanHuCard(cardObj, tileId, dimmed);
    }
    private void RegisterSichuanHuCard(GameObject cardObj, int tileId, bool dimmed) {
        MahjongObjectPool.Instance.RefreshTileCollider(cardObj);
        int registerId = tileId >= 10 ? tileId : 0;
        Card3DHoverManager.Instance.RegisterCard(cardObj, registerId);
        if (dimmed) {
            ApplySichuanHuCardDim(cardObj);
        }
    }
    private static void ApplySichuanHuCardDim(GameObject cardObj) {
        Card3DHoverManager.Instance.SetCardGrayOverlay(
            cardObj,
            Card3DHoverManager.Instance.MoqieOverlayColor,
            Card3DHoverManager.Instance.MoqieOverlayIntensity);
    }
}
