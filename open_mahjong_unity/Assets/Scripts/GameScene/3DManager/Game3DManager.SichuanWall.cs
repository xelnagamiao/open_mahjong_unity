using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager {
    private const int SichuanWallSlots = 14;
    private const float AuthoredSichuanSingleRowInset = 15f;
    private const int SichuanWallFullMask = (1 << SichuanWallSlots) - 1;
    private Transform sichuanWallRoot;
    private bool sichuanWallSingleRow;
    private readonly GameObject[,] sichuanWallTiles = new GameObject[4, SichuanWallSlots];
    private readonly string[] sichuanWallSeats = new string[4];

    /// <summary>实时川麻复式墙。只有公开占位，不接触牌值或牌谱的牌山查看器。</summary>
    public void ShowSichuanWall(int[] masks, IDictionary<int, string> seats, bool singleRow) {
        if (!ValidSichuanWallMasks(masks) || !ValidWallSeats(seats)) {
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

    private void CreateSichuanWall(int originalSeat, string position) {
        GetTileSurface(out Vector3 surfacePoint, out _);
        // Longer single rows must clear the lower-left player badge and the self hand.
        Vector3 rowOffset = sichuanWallSingleRow
            ? (surfacePoint - WallSlotPosition(position, 0))
                * (TableWorldScaleProfile.Length(AuthoredSichuanSingleRowInset) / TableWallRadius)
            : Vector3.zero;
        for (int slot = 0; slot < SichuanWallSlots; slot++) {
            // SBR doubles duplicate lays all 14 slots flat in front of each player.
            // Bloodflow keeps upper/lower stacks. Consumed slots never re-center.
            float centeredSlot = sichuanWallSingleRow ? (SichuanWallSlots - 1) * .5f - slot : 3 - slot / 2;
            bool upper = !sichuanWallSingleRow && slot % 2 == 0;
            Vector3 candidate = WallSlotPosition(position, centeredSlot) + rowOffset;
            sichuanWallTiles[originalSeat, slot] = CreateWallTile(sichuanWallRoot, $"Wall_{originalSeat}_{slot}",
                position, candidate, upper, 0);
        }
    }
}
