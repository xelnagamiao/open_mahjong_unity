using System.Collections;
using System.Collections.Generic;
using Riichi;
using UnityEngine;

/// <summary>
/// 牌谱/观战展开明牌：独立摸牌区、手切/摸切/手补/摸补/手杠/摸杠区分；出牌后归并摸牌并排序。
/// </summary>
public partial class Game3DManager {
    /// <summary>主列末张与摸牌区间隔（对齐 2D 摸牌区 half-gap）。</summary>
    private const float RecordDrawSlotGapFactor = 0.5f;

    private bool TryGetRecordShowHandLayout(string playerPosition, out Vector3 direction, out Quaternion rotation) {
        direction = Vector3.zero;
        rotation = Quaternion.identity;
        if (playerPosition == "left") {
            direction = BackDirection;
            rotation = RiverTileWorldRotation("left");
            return true;
        }
        if (playerPosition == "top") {
            direction = LeftDirection;
            rotation = RiverTileWorldRotation("top");
            return true;
        }
        if (playerPosition == "right") {
            direction = FrontDirection;
            rotation = RiverTileWorldRotation("right");
            return true;
        }
        return false;
    }

    private static Tile3D GetRecordShowDrawTile3D(Transform showCards) {
        if (showCards == null) return null;
        for (int i = 0; i < showCards.childCount; i++) {
            Tile3D tile3D = showCards.GetChild(i).GetComponent<Tile3D>();
            if (tile3D != null && tile3D.isRecordDrawSlotPinned) {
                return tile3D;
            }
        }
        return null;
    }

    private void ComputeRecordShowTargetPositions(
        Vector3 startPos,
        Vector3 direction,
        int mainCount,
        bool hasDraw,
        out List<Vector3> mainPositions,
        out Vector3? drawPosition) {
        mainPositions = new List<Vector3>(mainCount);
        Vector3 dir = direction.normalized;
        for (int i = 0; i < mainCount; i++) {
            mainPositions.Add(startPos + dir * widthSpacing * i);
        }
        if (hasDraw) {
            float drawOffset = widthSpacing * mainCount + widthSpacing * RecordDrawSlotGapFactor;
            drawPosition = startPos + dir * drawOffset;
        } else {
            drawPosition = null;
        }
    }

    private static bool RecordShowTileIdMatches(Tile3D tile3D, int tileId) {
        if (tile3D == null) return false;
        if (tileId < 10) return true;
        return tile3D.GetTileId() == tileId;
    }

    private void ClearRecordPlayerDrawSlotState(string playerPosition) {
        if (GameRecordManager.Instance == null) return;
        if (!GameRecordManager.Instance.recordPlayer_to_info.TryGetValue(playerPosition, out GameRecordManager.RecordPlayer player)) {
            return;
        }
        player.showHandDrawSlotActive = false;
    }

    private void LayRecordShowHandTiles(string playerPosition, Transform showCardsPosition, IList<int> handTiles, bool pinLastAsDraw) {
        if (!TryGetRecordShowHandLayout(playerPosition, out Vector3 direction, out Quaternion rotation)) return;

        List<int> sortedTiles = new List<int>(handTiles);
        sortedTiles.Sort(TileIdOrder.Compare);
        Vector3 startPos = showCardsPosition.position;

        int mainCount = pinLastAsDraw && sortedTiles.Count > 0 ? sortedTiles.Count - 1 : sortedTiles.Count;
        ComputeRecordShowTargetPositions(startPos, direction, mainCount, pinLastAsDraw && sortedTiles.Count > 0,
            out List<Vector3> mainPositions, out Vector3? drawPosition);

        for (int i = 0; i < mainCount; i++) {
            GameObject cardObj = MahjongObjectPool.Instance.Spawn(sortedTiles[i], mainPositions[i], rotation);
            if (cardObj == null) continue;
            cardObj.transform.SetParent(showCardsPosition, worldPositionStays: true);
            cardObj.name = $"RecordShow_{i}";
            if (Card3DHoverManager.Instance != null) {
                Card3DHoverManager.Instance.RegisterCard(cardObj, sortedTiles[i]);
            }
        }

        if (pinLastAsDraw && sortedTiles.Count > 0 && drawPosition.HasValue) {
            int drawTileId = sortedTiles[sortedTiles.Count - 1];
            GameObject drawObj = MahjongObjectPool.Instance.Spawn(drawTileId, drawPosition.Value, rotation);
            if (drawObj != null) {
                drawObj.transform.SetParent(showCardsPosition, worldPositionStays: true);
                drawObj.name = "RecordGet_Draw";
                Tile3D drawTile3D = drawObj.GetComponent<Tile3D>();
                if (drawTile3D != null) {
                    drawTile3D.isRecordDrawSlotPinned = true;
                }
                if (Card3DHoverManager.Instance != null) {
                    Card3DHoverManager.Instance.RegisterCard(drawObj, drawTileId);
                }
            }
        }
    }

    /// <summary>
    /// 牌谱展开模式：在独立摸牌区放置摸到的牌（不排序主列，对齐对局 GetCard）。
    /// </summary>
    private IEnumerator RecordShowCardGetCoroutine(string playerPosition, int tileId) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        Transform showCards = panel.ShowCardsPosition;
        if (!TryGetRecordShowHandLayout(playerPosition, out Vector3 direction, out Quaternion rotation)) yield break;

        Vector3? drawPosition = ComputeRecordShowDrawSlotPosition(showCards, direction);
        Vector3 spawnPosition = drawPosition ?? showCards.position;

        GameObject cardObj = MahjongObjectPool.Instance.Spawn(tileId, spawnPosition, rotation);
        if (cardObj == null) yield break;

        cardObj.transform.SetParent(showCards, worldPositionStays: true);
        cardObj.name = "RecordGet_Draw";
        Tile3D tile3D = cardObj.GetComponent<Tile3D>();
        if (tile3D != null) {
            tile3D.isRecordDrawSlotPinned = true;
        }
        if (Card3DHoverManager.Instance != null) {
            Card3DHoverManager.Instance.RegisterCard(cardObj, tileId);
        }

        yield return null;
    }

    private Vector3? ComputeRecordShowDrawSlotPosition(Transform showCards, Vector3 direction) {
        if (showCards == null) return null;
        int mainCount = 0;
        for (int i = 0; i < showCards.childCount; i++) {
            Tile3D tile3D = showCards.GetChild(i).GetComponent<Tile3D>();
            if (tile3D != null && !tile3D.isRecordDrawSlotPinned) {
                mainCount++;
            }
        }
        Vector3 startPos = showCards.position;
        float drawOffset = widthSpacing * mainCount + widthSpacing * RecordDrawSlotGapFactor;
        return startPos + direction.normalized * drawOffset;
    }

    /// <summary>
    /// 出牌/手补/鸣牌后：摸牌张归并进主列，全体按 tileId 排序并动画归位（对齐对局 RearrangeHandCardsWithAnimation）。
    /// </summary>
    private IEnumerator RearrangeRecordShowMergeAllWithAnimation(Transform showCardsPosition, string playerPosition) {
        if (showCardsPosition == null || showCardsPosition.childCount == 0) yield break;
        if (!TryGetRecordShowHandLayout(playerPosition, out Vector3 direction, out _)) yield break;

        ClearRecordPlayerDrawSlotState(playerPosition);

        List<Transform> allCards = new List<Transform>();
        for (int i = 0; i < showCardsPosition.childCount; i++) {
            allCards.Add(showCardsPosition.GetChild(i));
        }
        foreach (Transform card in allCards) {
            Tile3D tile3D = card.GetComponent<Tile3D>();
            if (tile3D != null) {
                tile3D.isRecordDrawSlotPinned = false;
            }
        }

        allCards.Sort((a, b) => {
            int idA = a.GetComponent<Tile3D>()?.GetTileId() ?? 0;
            int idB = b.GetComponent<Tile3D>()?.GetTileId() ?? 0;
            return TileIdOrder.Compare(idA, idB);
        });

        Vector3 startPos = showCardsPosition.position;
        List<Vector3> targetPositions = new List<Vector3>(allCards.Count);
        Vector3 dir = direction.normalized;
        for (int i = 0; i < allCards.Count; i++) {
            targetPositions.Add(startPos + dir * widthSpacing * i);
        }

        yield return StartCoroutine(Animate3DCardsToPositions(allCards, targetPositions, showCardsPosition));
    }

    /// <summary>
    /// 牌谱展开模式：删除一张手牌。
    /// fromDrawSlot=true 表示摸切/摸补/摸杠，仅从摸牌区删除；false 表示手切/手补/手杠，从主列删除。
    /// </summary>
    private IEnumerator RemoveRecordShowHandCardCoroutine(Transform showCardsPosition, int tileId, bool fromDrawSlot) {
        if (showCardsPosition.childCount == 0) {
            yield break;
        }

        Transform targetCard = null;
        if (fromDrawSlot) {
            Tile3D drawTile = GetRecordShowDrawTile3D(showCardsPosition);
            if (drawTile != null && RecordShowTileIdMatches(drawTile, tileId)) {
                targetCard = drawTile.transform;
            }
            if (targetCard == null) {
                for (int i = showCardsPosition.childCount - 1; i >= 0; i--) {
                    Transform child = showCardsPosition.GetChild(i);
                    Tile3D tile3D = child.GetComponent<Tile3D>();
                    if (tile3D != null && tile3D.isRecordDrawSlotPinned) {
                        targetCard = child;
                        break;
                    }
                }
            }
        } else {
            for (int i = 0; i < showCardsPosition.childCount; i++) {
                Transform child = showCardsPosition.GetChild(i);
                Tile3D tile3D = child.GetComponent<Tile3D>();
                if (tile3D != null && !tile3D.isRecordDrawSlotPinned && RecordShowTileIdMatches(tile3D, tileId)) {
                    targetCard = child;
                    break;
                }
            }
        }

        if (targetCard == null) {
            for (int i = showCardsPosition.childCount - 1; i >= 0; i--) {
                Transform child = showCardsPosition.GetChild(i);
                Tile3D tile3D = child.GetComponent<Tile3D>();
                if (tile3D == null) continue;
                if (fromDrawSlot == tile3D.isRecordDrawSlotPinned) {
                    targetCard = child;
                    break;
                }
            }
        }

        if (targetCard == null) {
            yield break;
        }

        Tile3D removedTile = targetCard.GetComponent<Tile3D>();
        if (removedTile != null) {
            removedTile.isRecordDrawSlotPinned = false;
        }
        lastRemove3DPosition = targetCard.position;
        MahjongObjectPool.Instance.Return(-1, targetCard.gameObject);
        yield return null;
    }

    /// <summary>
    /// 牌谱展开模式：根据组合掩码从 ShowCardsPosition 中删除指定手牌（跳过 flag=1 河牌来源位）。
    /// </summary>
    private IEnumerator RemoveRecordShowHandCardsByMaskCoroutine(Transform showCardsPosition, int[] combinationMask) {
        if (showCardsPosition == null || combinationMask == null) yield break;

        List<int> tilesToRemove = new List<int>();
        for (int i = 0; i + 1 < combinationMask.Length; i += 2) {
            int flag = combinationMask[i];
            int tid = combinationMask[i + 1];
            if (flag != 1 && tid >= 10) {
                tilesToRemove.Add(tid);
            }
        }

        foreach (int tileId in tilesToRemove) {
            bool removed = false;
            for (int pass = 0; pass < 2 && !removed; pass++) {
                bool preferDraw = pass == 0 && GetRecordShowDrawTile3D(showCardsPosition) != null;
                for (int i = 0; i < showCardsPosition.childCount; i++) {
                    Transform child = showCardsPosition.GetChild(i);
                    Tile3D tile3D = child.GetComponent<Tile3D>();
                    if (tile3D == null) continue;
                    if (preferDraw != tile3D.isRecordDrawSlotPinned) continue;
                    if (!RecordShowTileIdMatches(tile3D, tileId)
                        && RiichiTileUtil.Normalize(tile3D.GetTileId()) != RiichiTileUtil.Normalize(tileId)) {
                        continue;
                    }
                    if (tile3D.isRecordDrawSlotPinned) {
                        tile3D.isRecordDrawSlotPinned = false;
                    }
                    lastRemove3DPosition = child.position;
                    MahjongObjectPool.Instance.Return(-1, child.gameObject);
                    removed = true;
                    break;
                }
            }
        }

        yield return null;
    }

    /// <summary>牌谱展开明牌·国标：和牌张移入摸牌区（自摸取自手牌，荣和取自河牌）。</summary>
    private IEnumerator CoRecordShowCardsGuobiaoWinTileToDrawSlot(HepaiPresentationRequest request) {
        if (request == null) yield break;

        string pos = request.WinnerPosition;
        int winTileId = request.HepaiTile >= 10
            ? request.HepaiTile
            : (request.HepaiPlayerHand != null && request.HepaiPlayerHand.Length > 0
                ? request.HepaiPlayerHand[request.HepaiPlayerHand.Length - 1]
                : 0);
        if (winTileId < 10) yield break;

        PosPanel3D panel = GetPosPanel(pos);
        Transform showCards = panel?.ShowCardsPosition;
        if (showCards == null || !TryGetRecordShowHandLayout(pos, out Vector3 direction, out Quaternion rotation)) {
            yield break;
        }

        bool isSelfDraw = request.HuClass == "hu_self";
        GameObject winObj = null;

        if (isSelfDraw) {
            winObj = TryDetachRecordShowHandTileForWin(showCards, winTileId);
        } else {
            Tile3D existingDraw = GetRecordShowDrawTile3D(showCards);
            if (existingDraw != null) {
                MahjongObjectPool.Instance.Return(-1, existingDraw.gameObject);
            }
            winObj = TryTakeLastDiscardObjectForRon(winTileId, request.DiscardPlayerPosition);
        }

        Vector3? endPos = ComputeRecordShowDrawSlotPosition(showCards, direction);
        if (!endPos.HasValue) yield break;

        if (winObj == null) {
            Vector3 spawnPos = isSelfDraw ? showCards.position : endPos.Value;
            winObj = MahjongObjectPool.Instance.Spawn(winTileId, spawnPos, rotation);
        }
        if (winObj == null) yield break;

        winObj.transform.SetParent(showCards, worldPositionStays: true);
        winObj.name = "RecordGet_Draw";

        Tile3D tile3D = winObj.GetComponent<Tile3D>();
        if (tile3D != null) {
            tile3D.isRecordDrawSlotPinned = true;
        }
        if (Card3DHoverManager.Instance != null) {
            Card3DHoverManager.Instance.RegisterCard(winObj, winTileId);
        }

        Vector3 startPos = winObj.transform.position;
        Quaternion startRot = winObj.transform.rotation;
        if (Vector3.Distance(startPos, endPos.Value) > 0.01f
            || Quaternion.Angle(startRot, rotation) > 0.1f) {
            yield return CoLerpTransform(winObj.transform, startPos, startRot, endPos.Value, rotation, HepaiRevealTiming.TravelSeconds);
        } else {
            winObj.transform.SetPositionAndRotation(endPos.Value, rotation);
        }

        if (GameRecordManager.Instance != null
            && GameRecordManager.Instance.recordPlayer_to_info.TryGetValue(pos, out GameRecordManager.RecordPlayer rp)) {
            rp.showHandDrawSlotActive = true;
        }
    }

    private GameObject TryDetachRecordShowHandTileForWin(Transform showCards, int winTileId) {
        if (showCards == null) return null;

        Tile3D drawTile = GetRecordShowDrawTile3D(showCards);
        if (drawTile != null && RecordShowTileIdMatches(drawTile, winTileId)) {
            return drawTile.gameObject;
        }

        for (int i = showCards.childCount - 1; i >= 0; i--) {
            Tile3D tile3D = showCards.GetChild(i).GetComponent<Tile3D>();
            if (tile3D == null || tile3D.isRecordDrawSlotPinned) continue;
            if (!RecordShowTileIdMatches(tile3D, winTileId)) continue;
            return tile3D.gameObject;
        }

        return null;
    }
}
