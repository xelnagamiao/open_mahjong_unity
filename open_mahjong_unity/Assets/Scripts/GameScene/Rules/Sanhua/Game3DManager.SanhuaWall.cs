using System;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager {
    // 136 tiles in four sides of 17 stacks. The dealer and other players take 53 before play.
    private const int SanhuaSideTiles = 34;
    private const int SanhuaWallSize = 4 * SanhuaSideTiles;
    private const int SanhuaDealt = 53;
    private const int SanhuaRemaining = SanhuaWallSize - SanhuaDealt;
    private Transform sanhuaWallRoot;
    private readonly GameObject[] sanhuaWallTiles = new GameObject[SanhuaRemaining];
    private readonly string[] sanhuaWallSeats = new string[4];
    private int sanhuaWallStart, sanhuaWallIndicator, sanhuaWallFront, sanhuaWallBack;

    public void ShowSanhuaWall(SanhuaInfo info, int dealer, IDictionary<int, string> seats,
        int frontDraws, int backDraws) {
        int[] dice = info?.dice;
        if (dice == null || dice.Length != 2 || dice[0] < 1 || dice[0] > 6 || dice[1] < 1 || dice[1] > 6
            || dealer < 0 || dealer >= 4 || info.indicator <= 0 || !ValidWallSeats(seats)
            || frontDraws < 0 || backDraws < 0 || backDraws > SanhuaRemaining || frontDraws > SanhuaRemaining - backDraws) {
            ClearSanhuaWall();
            return;
        }
        int openingSide = (dealer + dice[0] + dice[1] - 1) % 4;
        int start = openingSide * SanhuaSideTiles + 2 * Math.Min(dice[0], dice[1]);
        bool sameWall = sanhuaWallRoot != null && start == sanhuaWallStart && info.indicator == sanhuaWallIndicator;
        for (int side = 0; sameWall && side < 4; side++) sameWall = sanhuaWallSeats[side] == seats[side];
        if (sameWall && frontDraws == sanhuaWallFront && backDraws == sanhuaWallBack) return;
        if (!sameWall) {
            ClearSanhuaWall();
            InitializeTilePlacement();
            if (!placementInitialized || MahjongObjectPool.Instance == null) return;
            sanhuaWallRoot = new GameObject("SanhuaWall").transform;
            sanhuaWallRoot.SetParent(transform, false);
            for (int side = 0; side < 4; side++) sanhuaWallSeats[side] = seats[side];
            sanhuaWallStart = start;
            sanhuaWallIndicator = info.indicator;
        }
        for (int i = 0; i < SanhuaRemaining; i++) {
            bool present = i >= frontDraws && i < SanhuaRemaining - backDraws;
            GameObject tile = sanhuaWallTiles[i];
            if (tile == null && present) {
                int slot = (start + SanhuaDealt + i) % SanhuaWallSize;
                string seat = sanhuaWallSeats[slot / SanhuaSideTiles];
                int stack = slot % SanhuaSideTiles / 2;
                int face = slot == openingSide * SanhuaSideTiles ? info.indicator : 0;
                sanhuaWallTiles[i] = CreateWallTile(sanhuaWallRoot, $"SanhuaWall_{slot}", seat,
                    WallSlotPosition(seat, stack - 8), slot % 2 == 0, face);
            } else if (tile != null && tile.activeSelf != present) {
                tile.SetActive(present);
            }
        }
        sanhuaWallFront = frontDraws;
        sanhuaWallBack = backDraws;
    }

    public void ClearSanhuaWall() {
        if (sanhuaWallRoot == null) return;
        sanhuaWallRoot.gameObject.SetActive(false);
        Destroy(sanhuaWallRoot.gameObject);
        sanhuaWallRoot = null;
        Array.Clear(sanhuaWallTiles, 0, sanhuaWallTiles.Length);
    }
}
