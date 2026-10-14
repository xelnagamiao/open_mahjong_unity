using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager {
    private readonly Dictionary<string, GameObject> changshaOpenKongRows = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, List<int>> changshaOpenKongPendingTiles = new Dictionary<string, List<int>>();

    /// <summary>开杠补牌在独立亮牌区展示，沿原座位队列排在删杠牌之后，不混入暗手背牌。</summary>
    public void ShowChangshaOpenKongTiles(string seat, int[] tiles, bool restore = false, int[] restoredHandTiles = null) {
        if (!_handAnimQueues.ContainsKey(seat)) return;
        changshaOpenKongPendingTiles[seat] = new List<int>(tiles ?? Array.Empty<int>());
        _ankanPendingDrawByPlayer[seat] = false;
        if (restore && seat != "self" && tiles != null && tiles.Length > 0) {
            EnqueueHandAnimOp(seat, new HandAnimOp { Kind = HandAnimOpKind.ChangshaOpenKongRemoveHand, HandTiles = restoredHandTiles ?? (int[])tiles.Clone(), CutClass = true });
        }
        EnqueueHandAnimOp(seat, new HandAnimOp { Kind = HandAnimOpKind.ChangshaOpenKongBatch, HandTiles = tiles != null ? (int[])tiles.Clone() : Array.Empty<int>() });
    }

    public void ClearChangshaOpenKongTiles() {
        foreach (string seat in HandAnimPlayerPositions) ShowChangshaOpenKongTiles(seat, Array.Empty<int>());
    }

    public void ClearChangshaOpenKongTilesImmediate() {
        changshaOpenKongPendingTiles.Clear();
        foreach (GameObject row in changshaOpenKongRows.Values) {
            if (row == null) continue;
            for (int i = row.transform.childCount - 1; i >= 0; i--) MahjongObjectPool.Instance?.Return(-1, row.transform.GetChild(i).gameObject);
            row.SetActive(false);
        }
    }

    /// <summary>和牌倒牌前等待本批队列处理完，再释放公开行占用的实体牌。</summary>
    public IEnumerator PrepareChangshaOpenKongHuReveal() {
        while (HasPendingRecordTableAnimations) yield return null;
        ClearChangshaOpenKongTilesImmediate();
    }

    private void RenderChangshaOpenKongTiles(string seat, int[] tiles) {
        PosPanel3D panel = GetPosPanel(seat);
        if (panel == null) return;
        if (!changshaOpenKongRows.TryGetValue(seat, out GameObject row) || row == null) {
            row = new GameObject("ChangshaOpenKongBatch");
            row.transform.SetParent(panel.cardsPosition.parent, false);
            changshaOpenKongRows[seat] = row;
        }
        for (int i = row.transform.childCount - 1; i >= 0; i--) MahjongObjectPool.Instance.Return(-1, row.transform.GetChild(i).gameObject);
        row.SetActive(tiles != null && tiles.Length > 0);
        if (tiles == null || tiles.Length == 0) return;
        Vector3 direction = seat == "left" ? BackDirection : seat == "top" ? LeftDirection : seat == "right" ? FrontDirection : Vector3.right;
        Quaternion rotation = RiverTileWorldRotation(seat);
        Vector3 start = HandRowOrigin(panel.ShowCardsPosition, direction);
        if (panel.ShowCardsPosition.gameObject.activeInHierarchy) start += panel.ShowCardsPosition.childCount * handStep * direction;
        for (int i = 0; i < tiles.Length; i++) {
            Vector3 position = PlaceTileOnTable(start + i * handStep * direction, rotation);
            GameObject tile = MahjongObjectPool.Instance.Spawn(tiles[i], position, rotation);
            if (tile == null) continue;
            tile.name = "ChangshaOpenKongTile_" + tiles[i];
            tile.transform.SetParent(row.transform, true);
            Card3DHoverManager.Instance?.RegisterCard(tile, tiles[i]);
        }
    }

    /// <summary>亮牌摸切只回收该张亮牌；亮牌续暗杠只从暗手移除其余原手牌，避免误删原手。</summary>
    private bool TryEnqueueChangshaOpenKongAction(string action, int tile, string seat, int[] mask, bool riichi, bool physicsSound) {
        if (!changshaOpenKongPendingTiles.TryGetValue(seat, out List<int> pending) || pending.Count == 0) return false;
        if (action == "Discard" || action == "RecordDiscard") {
            int index = pending.IndexOf(tile);
            if (index < 0) return false;
            pending.RemoveAt(index);
            EnqueueHandAnimOp(seat, new HandAnimOp { Kind = HandAnimOpKind.ChangshaOpenKongDiscard, TileId = tile, IsRiichi = riichi, PlayCutPhysicsSound = physicsSound });
            return true;
        }
        if (action != "angang" || mask == null || mask.Length < 2) return false;
        int target = mask[1];
        int publicCount = pending.FindAll(value => value == target).Count;
        if (publicCount == 0) return false;
        pending.RemoveAll(value => value == target);
        EnqueueHandAnimOp(seat, new HandAnimOp { Kind = HandAnimOpKind.ChangshaOpenKongBatch, HandTiles = pending.ToArray() });
        if (publicCount < 4) {
            int[] privateTiles = new int[4 - publicCount];
            Array.Fill(privateTiles, target);
            EnqueueHandAnimOp(seat, new HandAnimOp { Kind = HandAnimOpKind.ChangshaOpenKongRemoveHand, HandTiles = privateTiles, CombinationMask = mask });
        }
        // 副露必须等公开牌及原手牌回收到池后再建立，避免四张实体池被旧明手占满。
        EnqueueHandAnimOp(seat, new HandAnimOp { Kind = HandAnimOpKind.ChangshaOpenKongMeld, ChangshaMeldAction = action, CombinationMask = mask });
        _ankanPendingDrawByPlayer[seat] = true;
        return true;
    }

    /// <summary>展开牌谱删除明手中的对应牌；普通对局删除背牌，两个容器共用本家串行队列。</summary>
    private IEnumerator RemoveChangshaOpenKongHandTiles(HandAnimOp op) {
        PosPanel3D panel = GetPosPanel(op.PlayerPosition);
        if (panel == null) yield break;
        if (IsRecordShowCardsModeActive() && op.PlayerPosition != "self") {
            foreach (int tile in op.HandTiles) {
                yield return RemoveRecordShowHandCardCoroutine(panel.ShowCardsPosition, tile, false, op.PlayerPosition);
            }
            yield return RearrangeRecordShowMergeAllWithAnimation(panel.ShowCardsPosition, op.PlayerPosition);
        } else {
            op.RemoveCount = op.HandTiles.Length;
            if (op.HandTiles.Length > 0) op.TileId = op.HandTiles[0];
            yield return RemoveHandCardsFromQueue(panel.cardsPosition, op);
            yield return Rearrange3DCardsWithAnimation(panel.cardsPosition);
        }
    }

    private IEnumerator DiscardChangshaOpenKongTile(HandAnimOp op) {
        if (changshaOpenKongRows.TryGetValue(op.PlayerPosition, out GameObject row) && row != null) {
            for (int i = 0; i < row.transform.childCount; i++) {
                GameObject tile = row.transform.GetChild(i).gameObject;
                if (tile.name != "ChangshaOpenKongTile_" + op.TileId) continue;
                MahjongObjectPool.Instance.Return(-1, tile);
                break;
            }
        }
        PosPanel3D panel = GetPosPanel(op.PlayerPosition);
        if (panel == null) yield break;
        if (op.PlayCutPhysicsSound) SoundManager.Instance.PlayPhysicsSound("cut");
        yield return Set3DTileCoroutine(op.TileId, panel.discardsPosition, "Discard", op.PlayerPosition,
            ShouldApplyMoqieDiscardGray(true), isRiichi: op.IsRiichi);
    }
}
