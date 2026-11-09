using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager {
    private const int SichuanWallSlots = 14;
    private const float SichuanSingleRowInset = 15f;
    private const int SichuanWallFullMask = (1 << SichuanWallSlots) - 1;
    private Transform sichuanWallRoot;
    private bool sichuanWallSingleRow;
    private readonly GameObject[,] sichuanWallTiles = new GameObject[4, SichuanWallSlots];
    private readonly string[] sichuanWallSeats = new string[4];

    /// <summary>实时川麻复式墙。只有公开占位，不接触牌值或牌谱的牌山查看器。</summary>
    public void ShowSichuanWall(int[] masks, IDictionary<int, string> seats, bool singleRow) {
        if (!ValidSichuanWallMasks(masks) || !ValidSichuanWallSeats(seats)) {
            ClearSichuanWall();
            return;
        }
        bool reuse = sichuanWallRoot != null && sichuanWallSingleRow == singleRow;
        for (int seat = 0; seat < 4; seat++) reuse &= sichuanWallSeats[seat] == seats[seat];
        if (!reuse) {
            ClearSichuanWall();
            InitializeTilePlacement();
            if (!placementInitialized || MahjongObjectPool.Instance == null) return;
            sichuanWallRoot = new GameObject("SichuanDuplicateWalls").transform;
            sichuanWallRoot.SetParent(transform, false);
            sichuanWallSingleRow = singleRow;
            for (int seat = 0; seat < 4; seat++) {
                sichuanWallSeats[seat] = seats[seat];
                CreateSichuanWall(seat, seats[seat]);
            }
        }
        UpdateSichuanWall(masks);
    }

    public void UpdateSichuanWall(int[] masks) {
        if (sichuanWallRoot == null || !ValidSichuanWallMasks(masks)) return;
        for (int seat = 0; seat < 4; seat++) {
            for (int slot = 0; slot < SichuanWallSlots; slot++) {
                GameObject tile = sichuanWallTiles[seat, slot];
                bool present = (masks[seat] & (1 << slot)) != 0;
                if (tile != null && tile.activeSelf != present) tile.SetActive(present);
            }
        }
    }

    public void ClearSichuanWall() {
        if (sichuanWallRoot != null) {
            // Destroy is deferred; hide immediately when leaving or changing rounds.
            sichuanWallRoot.gameObject.SetActive(false);
            Destroy(sichuanWallRoot.gameObject);
        }
        sichuanWallRoot = null;
        sichuanWallSingleRow = false;
        System.Array.Clear(sichuanWallTiles, 0, sichuanWallTiles.Length);
        System.Array.Clear(sichuanWallSeats, 0, sichuanWallSeats.Length);
    }

    private static bool ValidSichuanWallMasks(int[] masks) {
        if (masks == null || masks.Length != 4) return false;
        foreach (int mask in masks) if (mask < 0 || (mask & ~SichuanWallFullMask) != 0) return false;
        return true;
    }

    private static bool ValidSichuanWallSeats(IDictionary<int, string> seats) {
        if (seats == null || seats.Count != 4) return false;
        var unique = new HashSet<string>();
        for (int seat = 0; seat < 4; seat++) {
            if (!seats.TryGetValue(seat, out string position)
                || (position != "self" && position != "right" && position != "top" && position != "left")
                || !unique.Add(position)) return false;
        }
        return true;
    }

    private void CreateSichuanWall(int originalSeat, string position) {
        Quaternion faceUp = RiverTileWorldRotation(position);
        GetTileSurface(out Vector3 surfacePoint, out Vector3 normal);
        // Longer single rows must clear the lower-left player badge and the self hand.
        Vector3 rowOffset = sichuanWallSingleRow
            ? (surfacePoint - WallSlotPosition(position, 0)) * (SichuanSingleRowInset / TableWallRadius)
            : Vector3.zero;
        for (int slot = 0; slot < SichuanWallSlots; slot++) {
            GameObject tile = MahjongObjectPool.Instance.CreatePresentationTile(2);
            if (tile == null) continue;
            tile.name = $"Wall_{originalSeat}_{slot}";
            tile.transform.SetParent(sichuanWallRoot, true);
            SetWallTileScale(tile.transform, TableWallScale);
            tile.transform.rotation = faceUp;
            Tile3D state = MahjongObjectPool.GetTile3D(tile);
            Card3DHoverManager.Instance?.ResetAndUnregisterCard(tile);
            state?.SetConcealedFaceDown(true);
            state?.SetHoverPeekAllowed(false);
            foreach (Collider collider in tile.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            Quaternion rotation = tile.transform.rotation;
            Matrix4x4 body = Matrix4x4.TRS(Vector3.zero, rotation, tileWorldScale * TableWallScale) * tileBodyFromRoot;
            float thickness = TileSurfacePlacement.MaximumProjection(tileBodyBounds, body, normal)
                - TileSurfacePlacement.MinimumProjection(tileBodyBounds, body, normal);
            // SBR doubles duplicate lays all 14 slots flat in front of each player.
            // Bloodflow keeps upper/lower stacks. Consumed slots never re-center.
            float centeredSlot = sichuanWallSingleRow ? (SichuanWallSlots - 1) * .5f - slot : 3 - slot / 2;
            int layer = !sichuanWallSingleRow && slot % 2 == 0 ? 1 : 0;
            Vector3 candidate = WallSlotPosition(position, centeredSlot) + rowOffset;
            Vector3 plane = surfacePoint + normal * (layer * (thickness + tileSurfaceClearance));
            tile.transform.position = TileSurfacePlacement.PlaceOnPlane(candidate, tileBodyBounds,
                Matrix4x4.Translate(candidate) * body, plane, normal, tileSurfaceClearance);
            sichuanWallTiles[originalSeat, slot] = tile;
        }
    }
}
