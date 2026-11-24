using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class Game3DManager {
    private readonly Dictionary<string, Coroutine> exchangeSixRiverUpdates = new Dictionary<string, Coroutine>();
    public void ReturnChangshaContinuationTile(string seat, int tile) {
        var source = RegisterChangshaPublicRiverTile(seat, tile);
        if (source == null) return;
        TableWorldObjectMount.AttachDetached(source.transform, true);
        OnLastDiscardTaken(seat);
        MahjongObjectPool.Instance.Return(-1, source);
        CompactChangshaRiver(seat);
    }

    // 本批公开牌可能与旧弃牌同值；从后往前找，只取该玩家最后一张匹配的牌对象。
    public GameObject RegisterChangshaPublicRiverTile(string seat, int tile) {
        var river = GetPosPanel(seat).discardsPosition;
        for (int index = river.childCount - 1; index >= 0; index--) {
            var child = river.GetChild(index);
            var card = child.GetComponent<Tile3D>();
            if (card == null || card.GetTileId() != tile) continue;
            RegisterLastDiscard(seat, child.gameObject, tile);
            return child.gameObject;
        }
        return null;
    }

    public void PrepareChangshaBatchClaim(string source, int tile, string claimant) {
        var claimedObject = RegisterChangshaPublicRiverTile(source, tile);
        if (exchangeSixRiverUpdates.TryGetValue(source, out var pending) && pending != null) StopCoroutine(pending);
        exchangeSixRiverUpdates[source] = StartCoroutine(CompactChangshaRiverAfterMeld(source, claimant, claimedObject));
    }

    private IEnumerator CompactChangshaRiverAfterMeld(string source, string claimant, GameObject claimedObject) {
        yield return null;
        // 大明杠可能还在等别人抢胡；等牌对象真正离开牌河、双方动画结束后再收拢牌河。
        while (claimedObject != null && claimedObject.transform.parent == GetPosPanel(source).discardsPosition) yield return null;
        while (HasPendingHandAnimWork(source) || HasPendingHandAnimWork(claimant)) yield return null;
        CompactChangshaRiver(source);
        exchangeSixRiverUpdates[source] = null;
    }

    private void CompactChangshaRiver(string seat) {
        var river = GetPosPanel(seat).discardsPosition;
        var rowDirection = seat == "self" ? RightDirection : seat == "left" ? BackDirection : seat == "top" ? LeftDirection : FrontDirection;
        var nextRowDirection = seat == "self" ? BackDirection : seat == "left" ? LeftDirection : seat == "top" ? FrontDirection : RightDirection;
        for (int index = 0; index < river.childCount; index++) {
            var child = river.GetChild(index);
            int row = index / 6, col = index % 6;
            var position = DiscardRowOrigin(river, rowDirection) + rowDirection.normalized * ComputeRowCenterOffset(river, row * 6, col, false)
                + nextRowDirection.normalized * heightSpacing * row;
            child.position = PlaceTileOnTable(position, child.rotation);
        }
    }

    public void SyncChangshaExchangeRecordHands(Dictionary<int, int[]> openHands, Dictionary<int, int[]> fourJoy,
                                              Dictionary<int, string> positions, Dictionary<int, int> counts) {
        if (IsRecordShowCardsModeActive()) return;
        foreach (var entry in positions) {
            int[] revealed = null;
            openHands.TryGetValue(entry.Key, out revealed);
            bool hideConcealed = revealed != null;
            if (exchangeSixHandUpdates.TryGetValue(entry.Value, out var pending) && pending != null) StopCoroutine(pending);
            exchangeSixHandUpdates[entry.Value] = StartCoroutine(SyncExchangeSixSeat(entry.Value, counts[entry.Key], revealed, hideConcealed));
        }
    }
    private readonly Dictionary<string, Coroutine> exchangeSixHandUpdates = new Dictionary<string, Coroutine>();
    private readonly Dictionary<string, int[]> exchangeSixShownHands = new Dictionary<string, int[]>();
    private readonly Dictionary<string, bool> exchangeSixConcealedHidden = new Dictionary<string, bool>();
    private readonly Dictionary<string, Transform> exchangeSixOpenHandRows = new Dictionary<string, Transform>();

    internal Transform ChangshaOpenHandRow(string seat) {
        if (!exchangeSixOpenHandRows.TryGetValue(seat, out var row) || row == null) {
            row = new GameObject("ChangshaOpenHand").transform;
            row.SetParent(GetPosPanel(seat).transform, false);
            exchangeSixOpenHandRows[seat] = row;
        }
        return row;
    }

    public void SyncChangshaExchangeHands(ChangshaExchangeSixPublic snapshot) {
        foreach (string seat in HandAnimPlayerPositions) {
            int index = TableMirror.Current.IndexToPosition.FirstOrDefault(entry => entry.Value == seat).Key;
            int[] revealed = null;
            snapshot.open_hands?.TryGetValue(index, out revealed);
            bool hideConcealed = revealed != null;
            int count = TableMirror.Current.Info(seat)?.hand_tiles_count ?? 0;
            if (exchangeSixShownHands.TryGetValue(seat, out int[] previous) &&
                (previous ?? new int[0]).SequenceEqual(revealed ?? new int[0]) && exchangeSixConcealedHidden[seat] == hideConcealed
                && (seat == "self" || GetPosPanel(seat).cardsPosition.childCount == count)) continue;
            if (exchangeSixHandUpdates.TryGetValue(seat, out Coroutine pending) && pending != null) StopCoroutine(pending);
            exchangeSixHandUpdates[seat] = StartCoroutine(SyncExchangeSixSeat(seat, count, revealed, hideConcealed));
        }
    }

    private IEnumerator SyncExchangeSixSeat(string seat, int count, int[] revealed, bool hideConcealed) {
        yield return null;
        while (HasPendingHandAnimWork(seat)) yield return null;
        var panel = GetPosPanel(seat);
        if (seat != "self" && panel.cardsPosition.childCount != count) {
            var old = new List<GameObject>();
            CollectChildren(panel.cardsPosition, old);
            foreach (var tile in old) MahjongObjectPool.Instance.Return(-1, tile);
            for (int index = 0; index < count; index++) Get3DTile(seat, "init");
        }
        var shown = new List<GameObject>();
        // 独立于和牌倒牌容器，避免普通摸牌动画重排公开行，或回收正在使用的副露对象。
        var row = ChangshaOpenHandRow(seat);
        CollectChildren(row, shown);
        foreach (var tile in shown) MahjongObjectPool.Instance.Return(-1, tile);
        panel.cardsPosition.gameObject.SetActive(!hideConcealed);
        // 本家已有可操作的二维手牌，不再在右下角重复铺一行，避免挡住副露。
        if (seat != "self" && revealed != null) foreach (int tile in revealed.OrderBy(value => value)) {
            // 明牌使用展示副本，避免占用牌河和副露共用的牌对象。
            Set3DTile(tile, row, "Record", seat, usePresentationCopy: true);
        }
        // Record 默认朝向要配合倒牌动画；公开行不播该动画，须直接把牌面朝上。
        Vector3 direction = seat == "self" ? RightDirection : seat == "left" ? BackDirection
            : seat == "top" ? LeftDirection : FrontDirection;
        Vector3 origin = HandRowOrigin(panel.cardsPosition, direction);
        int slot = 0;
        foreach (Transform tile in row) {
            tile.rotation = RiverTileWorldRotation(seat);
            tile.position = PlaceTileOnTable(origin + direction.normalized * handStep * slot++, tile.rotation);
        }
        exchangeSixShownHands[seat] = revealed == null ? null : (int[])revealed.Clone();
        exchangeSixConcealedHidden[seat] = hideConcealed;
        exchangeSixHandUpdates[seat] = null;
    }

    public void ClearChangshaExchangeHands() {
        ClearChangshaExchangeClaims();
        foreach (var update in exchangeSixRiverUpdates.Values) if (update != null) StopCoroutine(update);
        exchangeSixRiverUpdates.Clear();
        foreach (var update in exchangeSixHandUpdates.Values) if (update != null) StopCoroutine(update);
        exchangeSixHandUpdates.Clear();
        foreach (string seat in exchangeSixShownHands.Keys) {
            var panel = GetPosPanel(seat);
            var shown = new List<GameObject>();
            CollectChildren(ChangshaOpenHandRow(seat), shown);
            foreach (var tile in shown) MahjongObjectPool.Instance?.Return(-1, tile);
            panel.cardsPosition.gameObject.SetActive(true);
        }
        exchangeSixShownHands.Clear();
        exchangeSixConcealedHidden.Clear();
    }
}
