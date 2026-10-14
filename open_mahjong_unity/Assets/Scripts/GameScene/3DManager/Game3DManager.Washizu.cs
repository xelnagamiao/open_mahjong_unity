using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class Game3DManager {
    private readonly Dictionary<string, int[]> washizuPublicHands = new Dictionary<string, int[]>();
    private readonly Dictionary<string, List<GameObject>> washizuReadingRows = new Dictionary<string, List<GameObject>>();
    private readonly HashSet<string> washizuDirtySeats = new HashSet<string>();
    private readonly HashSet<string> washizuRevealedSeats = new HashSet<string>();
    private static readonly string[] WashizuOpponentSeats = { "left", "top", "right" };

    public void SetWashizuPublicHands(Dictionary<int, int[]> hands, Dictionary<int, string> positions) {
        if (hands == null) { ClearWashizuPresentation(); return; }
        foreach (var entry in hands) {
            if (!positions.TryGetValue(entry.Key, out string seat) || seat == "self") continue;
            if (washizuRevealedSeats.Contains(seat)) continue;
            if (washizuPublicHands.TryGetValue(seat, out int[] previous) && previous.SequenceEqual(entry.Value)) continue;
            washizuPublicHands[seat] = (int[])entry.Value.Clone();
            washizuDirtySeats.Add(seat);
        }
    }

    // Uses the same animation queue as ordinary hands. A snapshot may arrive before
    // a flying tile lands; never texture a pool object while the old action owns it.
    private void RefreshWashizuPresentation() {
        FollowWashizuReadingRows();
        if (washizuDirtySeats.Count == 0 || MahjongObjectPool.Instance == null) return;
        foreach (string seat in WashizuOpponentSeats) {
            if (!washizuDirtySeats.Contains(seat) || HasPendingHandAnimWork(seat)) continue;
            PosPanel3D panel = GetPosPanel(seat);
            if (panel == null || !washizuPublicHands.TryGetValue(seat, out int[] cards)) continue;
            // Full-hand reveal in a replay or settlement takes precedence.
            Transform row = IsWashizuExpandedReplay ? panel.ShowCardsPosition : panel.cardsPosition;
            if (row.childCount != cards.Length) continue;
            if (washizuRealtimeLayouts.TryGetValue(seat, out WashizuHandLayout layout)) {
                washizuDirtySeats.Remove(seat);
                EnqueueHandAnimOp(seat, new HandAnimOp { Kind = HandAnimOpKind.WashizuOrder, WashizuLayout = layout });
                continue;
            }
            ApplyWashizuStandingRow(panel, cards);
            RenderWashizuReadingRow(seat, panel, cards);
            washizuDirtySeats.Remove(seat);
        }
    }

    private void ApplyWashizuStandingRow(PosPanel3D panel, int[] cards) {
        for (int i=0; i<cards.Length; i++) {
            GameObject obj=panel.cardsPosition.GetChild(i).gameObject;
            Card3DHoverManager.Instance?.ResetAndUnregisterCard(obj);
            MahjongObjectPool.Instance.SetPresentationFace(obj, cards[i]);
            Tile3D state = MahjongObjectPool.GetTile3D(obj);
            // Concealed standing tiles have the opposite face-up axis to river
            // tiles. Rotate only their artwork, before the atlas mapping.
            state?.SetFrontFaceHalfTurn(WashizuTile.IsTransparent(cards[i]));
            state?.SetHoverPeekAllowed(false);
            if (cards[i] != 0) Card3DHoverManager.Instance?.RegisterCard(obj, cards[i]);
        }
    }

    private void RenderWashizuReadingRow(string seat, PosPanel3D panel, int[] cards) {
        ClearWashizuReadingRow(seat);
        if (!TryGetRecordShowHandLayout(seat, out Vector3 direction, out Quaternion rotation)) return;
        var row=new List<GameObject>();washizuReadingRows[seat]=row;
        Vector3 inward = seat == "left" ? RightDirection : seat == "right" ? LeftDirection : BackDirection;
        // ShowCardsPosition is the normal full reveal anchor, often directly under
        // the standing row. A glass hand needs two separate rows at the same time.
        InitializeTilePlacement();
        // Half the standing depth + half the flat face + a narrow cloth gap.
        float readingInset = tileBodyBounds.extents.z * Mathf.Abs(tileWorldScale.z) + cardHeight * .58f;
        Vector3 start=HandRowOrigin(panel.cardsPosition,direction) + inward.normalized * readingInset;
        for (int i=0;i<cards.Length;i++) {
            if (!WashizuTile.IsTransparent(cards[i])) continue;
            // Printed public information sits on the cloth, without a second tile body.
            GetTileSurface(out Vector3 surfacePoint, out Vector3 surfaceNormal);
            Vector3 position = start + direction.normalized * handStep * i;
            position += surfaceNormal * (tileSurfaceClearance - Vector3.Dot(position - surfacePoint, surfaceNormal));
            GameObject tile=MahjongObjectPool.Instance.SpawnWashizuReadingTile(WashizuTile.Face(cards[i]), position, rotation);
            if (tile==null) continue;
            tile.name="WashizuPublic_"+cards[i];
            tile.transform.SetParent(panel.ShowCardsPosition,true);
            tile.GetComponent<WashizuReadingSurface>().Follow(
                MahjongObjectPool.GetTile3D(panel.cardsPosition.GetChild(i).gameObject),
                panel.cardsPosition, inward.normalized*readingInset);
            row.Add(tile);
            Card3DHoverManager.Instance?.RegisterCard(tile,cards[i]);
        }
    }

    // Track the physical pool lease through every sorting frame. A discarded or
    // recycled tile must lose its old reading aid before the next render.
    private void FollowWashizuReadingRows() {
        if (washizuReadingRows.Count == 0) return;
        GetTileSurface(out Vector3 point, out Vector3 normal);
        foreach (List<GameObject> row in washizuReadingRows.Values) {
            for (int i=row.Count-1;i>=0;i--) {
                GameObject tile=row[i];
                if (tile != null && tile.activeSelf && tile.GetComponent<WashizuReadingSurface>()
                    .FollowOnTable(point, normal, tileSurfaceClearance)) continue;
                if (tile != null && tile.activeSelf) MahjongObjectPool.Instance?.Return(-1,tile);
                row.RemoveAt(i);
            }
        }
    }

    private void ClearWashizuReadingRow(string seat) {
        if (!washizuReadingRows.TryGetValue(seat,out List<GameObject> row)) return;
        foreach (GameObject obj in row) if (obj!=null && obj.activeSelf) MahjongObjectPool.Instance?.Return(-1,obj);
        row.Clear();
    }

    // A winning or tenpai hand now owns the standing row. Stop public snapshots
    // from repainting its revealed opaque tiles or leaving a duplicate flat row.
    private void SuspendWashizuPublicHand(string seat) {
        if (!washizuPublicHands.ContainsKey(seat)) return;
        ClearWashizuReadingRow(seat);
        washizuPublicHands.Remove(seat);
        washizuDirtySeats.Remove(seat);
        washizuRevealedSeats.Add(seat);
    }

    public void ClearWashizuPresentation() {
        washizuRealtimeLayouts.Clear();
        washizuRealtimeTiles.Clear();
        foreach (string seat in WashizuOpponentSeats) ClearWashizuReadingRow(seat);
        washizuReadingRows.Clear();washizuPublicHands.Clear();washizuDirtySeats.Clear();washizuRevealedSeats.Clear();
    }

    public void RefreshWashizuRecordHands() {
        var record=GameRecordManager.Instance;
        if (record==null || !record.IsTransparentRecord()) return;
        foreach (string seat in WashizuOpponentSeats) {
            if (washizuRevealedSeats.Contains(seat)) continue;
            if (record.IsWashizuRealtimeRecord()) {
                if (!record.recordPlayer_to_info.TryGetValue(seat, out var realtimePlayer)) continue;
                var layout = record.GetWashizuRecordLayout(seat, IsWashizuExpandedReplay);
                SetWashizuRealtimeLayouts(new Dictionary<int, WashizuHandLayout> { [realtimePlayer.playerIndex] = layout }, record.indexToPosition);
                continue;
            }
            ClearWashizuReadingRow(seat);
            if (RecordSetting.Instance != null && RecordSetting.Instance.IsShowCardsMode) {
                washizuPublicHands.Remove(seat);washizuDirtySeats.Remove(seat);continue;
            }
            if (!record.recordPlayer_to_info.TryGetValue(seat,out var player)) continue;
            var publicTiles=new List<int>();int opaque=0;
            foreach (int tile in player.tileList) {
                if (WashizuTile.IsTransparent(tile)) publicTiles.Add(tile);else opaque++;
            }
            publicTiles.Sort((a,b)=>WashizuTile.Face(a).CompareTo(WashizuTile.Face(b)));
            for(int i=0;i<opaque;i++) publicTiles.Add(0);
            washizuPublicHands[seat]=publicTiles.ToArray();washizuDirtySeats.Add(seat);
        }
        RefreshWashizuPresentation();
    }
}
