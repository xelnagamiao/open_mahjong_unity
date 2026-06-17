using System.Collections;

using System.Collections.Generic;

using UnityEngine;



/// <summary>

/// 四川血战中途和牌：和牌张从手牌/河牌 Lerp 移入补花区（简短动画，不重整手牌）。

/// </summary>

public partial class Game3DManager {

    private const int SichuanBuhuaCardsPerRow = 4;



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

        if (NormalGameStateManager.Instance == null || handsByPlayerIndex == null) return;

        var panels = new List<PosPanel3D>();

        foreach (var kvp in handsByPlayerIndex) {

            if (!NormalGameStateManager.Instance.indexToPosition.TryGetValue(kvp.Key, out string pos)) continue;

            int[] tiles = kvp.Value;

            if (tiles == null || tiles.Length == 0) continue;

            ClearSichuanHuBuhuaMarkers(pos);

            if (tiles.Length >= 14) {

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

                GameCanvas.Instance?.ChangeHandCards("RemoveHuWinTile", tileId, null, null);

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

        GameObject handTile = TryTakeLastHandTileObject(panel.cardsPosition);

        if (handTile == null) {

            Debug.LogWarning($"[SichuanHu] 他家自摸取末张手牌失败 winner={playerPosition}，fallback spawn");

            handTile = SpawnSichuanBuhuaWinTileObject(panel, playerPosition, 0, true, false);

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

        cardObj.transform.SetParent(buhuaParent, worldPositionStays: true);

        cardObj.transform.SetPositionAndRotation(pos, rotation);

        RegisterSichuanHuCard(cardObj, tileId, dimmed: false);

        return cardObj;

    }



    private IEnumerator CoSichuanRonSingleToBuhua(HepaiPresentationRequest request) {

        string winnerPos = request.WinnerPosition;

        int tileId = request.HepaiTile;

        PosPanel3D winnerPanel = GetPosPanel(winnerPos);

        if (winnerPanel == null || winnerPanel.buhuaPosition == null) yield break;



        GameObject riverTile = DetachLastDiscardFromRiver(request.DiscardPlayerPosition);

        if (riverTile != null) {

            NormalGameStateManager.Instance?.SyncRonDiscardRemoved(request.DiscardPlayerPosition, tileId);

            yield return CoAnimateTileToBuhua(riverTile, winnerPanel, winnerPos, tileId, faceDown: false, dimmed: false, null);

        } else {

            Debug.LogWarning($"[SichuanHu] 河牌取牌失败，fallback spawn tileId={tileId} winner={winnerPos}");

            GameObject fallback = SpawnSichuanBuhuaWinTileObject(winnerPanel, winnerPos, tileId, false, false);

            if (fallback != null) {

                yield return CoAnimateTileToBuhua(fallback, winnerPanel, winnerPos, tileId, faceDown: false, dimmed: false, null);

            }

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

        GameObject clone = MahjongObjectPool.Instance.Spawn(spawnId, startPos, startRot);

        if (clone == null) yield break;



        yield return CoAnimateTileToBuhua(clone, winnerPanel, winnerPos, tileId, faceDown: false, dimmed: true, startPos);



        if (request.RecycleDiscardAfterPresent) {

            RecyclePresentedWinTileSource(request, tileId);

        }

    }



    private bool TryGetWinTileSpawnPose(HepaiPresentationRequest request, out Vector3 pos, out Quaternion rot) {

        if (request != null && request.IsQianggang && lastCutJiagang3DObject != null) {

            pos = lastCutJiagang3DObject.transform.position;

            rot = lastCutJiagang3DObject.transform.rotation;

            return true;

        }

        return TryGetRiverDiscardPose(request?.DiscardPlayerPosition, out pos, out rot);

    }



    private void RecyclePresentedWinTileSource(HepaiPresentationRequest request, int tileId) {

        if (request != null && request.IsQianggang && lastCutJiagang3DObject != null) {

            GameObject obj = lastCutJiagang3DObject;

            lastCutJiagang3DObject = null;

            obj.transform.SetParent(null, worldPositionStays: true);

            NormalGameStateManager.Instance?.SyncRonDiscardRemoved(request.DiscardPlayerPosition, tileId);

            MahjongObjectPool.Instance.Return(-1, obj);

            return;

        }

        RecycleRiverDiscard(request?.DiscardPlayerPosition, tileId);

    }



    private void PrepareTileForBuhuaMove(GameObject cardObj, Transform buhuaParent, string playerPosition) {

        if (cardObj == null) return;

        Tile3D tile3D = cardObj.GetComponent<Tile3D>();

        tile3D?.ResetConcealedState();

        Quaternion finalRot = GetMeldVerticalWorldRotation(playerPosition);

        cardObj.transform.SetParent(buhuaParent, worldPositionStays: true);

        cardObj.transform.rotation = finalRot;

    }



    private IEnumerator CoAnimateTileToBuhua(

        GameObject cardObj, PosPanel3D panel, string playerPosition,

        int tileId, bool faceDown, bool dimmed, Vector3? explicitStartPos) {

        if (cardObj == null || panel?.buhuaPosition == null) yield break;



        ComputeSichuanBuhuaSlot(panel.buhuaPosition, playerPosition, out Vector3 endPos, out _);

        Quaternion finalRot = faceDown
            ? GetMeldVerticalWorldRotation(playerPosition)
            : ComputeSichuanBuhuaSlotRotation(panel.buhuaPosition, playerPosition);



        if (faceDown) {

            PrepareTileForBuhuaMove(cardObj, panel.buhuaPosition, playerPosition);

        } else {

            cardObj.transform.SetParent(panel.buhuaPosition, worldPositionStays: true);

        }



        Vector3 startPos = explicitStartPos ?? cardObj.transform.position;

        Quaternion startRot = faceDown ? finalRot : cardObj.transform.rotation;

        cardObj.transform.SetPositionAndRotation(startPos, startRot);



        if (!faceDown) {

            RegisterSichuanHuCard(cardObj, tileId, dimmed);

        }



        yield return CoLerpTransform(cardObj.transform, startPos, startRot, endPos, finalRot, HepaiRevealTiming.TravelSeconds);

        cardObj.transform.SetPositionAndRotation(endPos, finalRot);



        if (faceDown) {

            ApplyConcealedFaceDownLikeMeld(cardObj, tileId, dimmed);

        } else if (dimmed) {

            ApplySichuanHuCardDim(cardObj);

        }



        cardObj.name = $"SichuanHu_{panel.buhuaPosition.childCount}";

    }



    private static GameObject TryTakeLastHandTileObject(Transform cardsPosition) {

        if (cardsPosition == null || cardsPosition.childCount == 0) return null;

        GameObject obj = cardsPosition.GetChild(cardsPosition.childCount - 1).gameObject;

        obj.transform.SetParent(null, worldPositionStays: true);

        return obj;

    }



    private GameObject TryDetachSelfHandTileById(Transform cardPosition, int tileId) {

        if (cardPosition == null) return null;

        for (int i = 0; i < cardPosition.childCount; i++) {

            Transform child = cardPosition.GetChild(i);

            Tile3D t3 = child.GetComponent<Tile3D>();

            if (t3 != null && t3.GetTileId() == tileId) {

                GameObject obj = child.gameObject;

                obj.transform.SetParent(null, worldPositionStays: true);

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



    private GameObject PeekLastDiscardObject(string discarderPos) {

        if (string.IsNullOrEmpty(discarderPos)) {

            return lastCutJiagang3DObject;

        }

        PosPanel3D panel = GetPosPanel(discarderPos);

        if (panel?.discardsPosition != null && panel.discardsPosition.childCount > 0) {

            return panel.discardsPosition.GetChild(panel.discardsPosition.childCount - 1).gameObject;

        }

        return lastCutJiagang3DObject;

    }



    private GameObject DetachLastDiscardFromRiver(string discarderPos) {

        GameObject obj = PeekLastDiscardObject(discarderPos);

        if (obj == null) return null;

        obj.transform.SetParent(null, worldPositionStays: true);

        if (lastCutJiagang3DObject == obj) {

            lastCutJiagang3DObject = null;

        }

        return obj;

    }



    private void RecycleRiverDiscard(string discarderPos, int tileId) {

        GameObject obj = DetachLastDiscardFromRiver(discarderPos);

        if (obj == null) return;

        NormalGameStateManager.Instance?.SyncRonDiscardRemoved(discarderPos, tileId);

        MahjongObjectPool.Instance.Return(-1, obj);

    }



    private GameObject SpawnSichuanBuhuaWinTileObject(

        PosPanel3D panel, string playerPosition, int tileId, bool faceDown, bool dimmed) {

        ComputeSichuanBuhuaSlot(panel.buhuaPosition, playerPosition, out Vector3 pos, out Quaternion rotation);

        GameObject cardObj;

        if (faceDown) {

            rotation = GetMeldVerticalWorldRotation(playerPosition);

            if (playerPosition == "self" && tileId >= 10) {

                cardObj = MahjongObjectPool.Instance.Spawn(tileId, pos, rotation);

            } else {

                cardObj = MahjongObjectPool.Instance.SpawnBlankTile(pos, rotation);

            }

        } else {

            int spawnId = tileId >= 10 ? tileId : 0;

            cardObj = MahjongObjectPool.Instance.Spawn(spawnId, pos, rotation);

        }

        if (cardObj == null) return null;

        cardObj.transform.SetParent(panel.buhuaPosition, worldPositionStays: true);

        cardObj.transform.SetPositionAndRotation(pos, rotation);

        if (faceDown) {

            ApplyConcealedFaceDownLikeMeld(cardObj, tileId, dimmed);

        } else {

            RegisterSichuanHuCard(cardObj, tileId, dimmed);

        }

        return cardObj;

    }



    private void ComputeSichuanBuhuaSlot(Transform target, string playerPosition, out Vector3 pos, out Quaternion rotation) {

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



        int index = target.childCount;

        int row = index / SichuanBuhuaCardsPerRow;

        int col = index % SichuanBuhuaCardsPerRow;

        float colOffset = ComputeRowCenterOffset(target, row, col, SichuanBuhuaCardsPerRow, false);

        pos = target.position

            + widthDir.normalized * colOffset

            + heightDir.normalized * heightSpacing * row;

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

        if (MahjongObjectPool.Instance != null) {

            MahjongObjectPool.Instance.RefreshTileCollider(cardObj);

        }

        if (Card3DHoverManager.Instance == null) return;

        int registerId = tileId >= 10 ? tileId : 0;

        Card3DHoverManager.Instance.RegisterCard(cardObj, registerId);

        if (dimmed) {

            ApplySichuanHuCardDim(cardObj);

        }

    }



    private static void ApplySichuanHuCardDim(GameObject cardObj) {

        if (Card3DHoverManager.Instance == null) return;

        Card3DHoverManager.Instance.SetCardGrayOverlay(

            cardObj,

            Card3DHoverManager.Instance.MoqieOverlayColor,

            Card3DHoverManager.Instance.MoqieOverlayIntensity);

    }

}


