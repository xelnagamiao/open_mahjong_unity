using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class Game3DManager {
    private Coroutine exchangeSixClaimDisplay;
    private readonly List<GameObject> exchangeSixKongPreviews = new List<GameObject>();
    private readonly List<ClaimTileGlow> exchangeSixKongGlows = new List<ClaimTileGlow>();

    public void SyncChangshaExchangeClaims(ChangshaExchangeSixState snapshot, Dictionary<int, string> positions = null) {
        if (GetComponent<ChangshaClaimDisplayLifetime>() == null) gameObject.AddComponent<ChangshaClaimDisplayLifetime>();
        ClearChangshaExchangeClaims();
        ClearChangshaOpenKongTilesImmediate();
        var pending = snapshot.public_state.pending_kong;
        if (pending != null && snapshot.public_state.kong_tiles?.Length > 0) {
            string source = (positions ?? TableMirror.Current.IndexToPosition)[pending.player_index];
            exchangeSixClaimDisplay = StartCoroutine(ShowChangshaPendingKong(source, pending,
                (int[])snapshot.public_state.kong_tiles.Clone()));
        } else if ((snapshot.phase == "claims" || snapshot.phase == "claims_recheck")
                   && (snapshot.private_state.action_list ?? Array.Empty<string>()).Any(action => action != "pass")) {
            int target = snapshot.public_state.claim_tiles?.FirstOrDefault() ?? 0;
            if (target > 0) ShowClaimGlow(target);
        }
    }

    private IEnumerator ShowChangshaPendingKong(string source, ChangshaExchangeSixPendingKong pending, int[] tiles) {
        yield return null;
        var panel = GetPosPanel(source);
        var river = panel.discardsPosition;
        int previousCount = -1;
        if (!pending.discarded) {
            CreateChangshaKongPreviews(source, pending.river_start, tiles);
            ShowChangshaKongGlows(exchangeSixKongPreviews.ToArray());
        }
        while (true) {
            if (HasPendingHandAnimWork(source)) { yield return null; continue; }
            // 只在河牌数量改变且动画完成后检查一次，等待时不逐帧扫描整条牌河。
            if (river.childCount != previousCount) {
                previousCount = river.childCount;
                if (previousCount >= pending.river_start + tiles.Length) {
                    var targets = new GameObject[tiles.Length];
                    bool arrived = true;
                    for (int index = 0; index < tiles.Length; index++) {
                        var tile = river.GetChild(pending.river_start + index).GetComponent<Tile3D>();
                        if (tile == null || tile.GetTileId() != tiles[index]) { arrived = false; break; }
                        targets[index] = tile.gameObject;
                    }
                    if (arrived) {
                        ClearChangshaKongPreviews();
                        ShowChangshaKongGlows(targets);
                        exchangeSixClaimDisplay = null;
                        yield break;
                    }
                }
            }
            yield return null;
        }
    }

    private void CreateChangshaKongPreviews(string source, int start, int[] tiles) {
        var river = GetPosPanel(source).discardsPosition;
        Vector3 along = source == "left" ? BackDirection : source == "top" ? LeftDirection
            : source == "right" ? FrontDirection : RightDirection;
        Vector3 next = source == "left" ? LeftDirection : source == "top" ? FrontDirection
            : source == "right" ? RightDirection : BackDirection;
        Quaternion rotation = RiverTileWorldRotation(source);
        for (int index = 0; index < tiles.Length; index++) {
            var tile = MahjongObjectPool.Instance.CreatePresentationTile(tiles[index]);
            if (tile == null) continue;
            tile.SetActive(true);
            int row = (start + index) / 6, column = (start + index) % 6;
            // 预览不在真实牌河层级中，已有河牌之后的空位需补上虚拟牌宽，不能全叠在末张上。
            int existingColumns = Mathf.Min(column, Mathf.Max(0, river.childCount - row * 6));
            float offset = ComputeRowCenterOffset(river, row, existingColumns, 6, false)
                + (column - existingColumns) * widthSpacing;
            tile.transform.rotation = rotation;
            tile.transform.position = PlaceTileOnTable(DiscardRowOrigin(river, along)
                + along.normalized * offset
                + next.normalized * heightSpacing * row, rotation);
            tile.name = "ChangshaPendingKongTile";
            exchangeSixKongPreviews.Add(tile);
        }
    }

    private void ShowChangshaKongGlows(GameObject[] targets) {
        foreach (var glow in exchangeSixKongGlows) if (glow != null) glow.Hide();
        var prefab = claimTileGlowPrefab != null ? claimTileGlowPrefab
            : Resources.Load<GameObject>("Effects/ClaimTileGlow/ClaimTileGlow");
        if (prefab == null) return;
        while (exchangeSixKongGlows.Count < targets.Length)
            exchangeSixKongGlows.Add(Instantiate(prefab).GetComponent<ClaimTileGlow>());
        for (int index = 0; index < targets.Length; index++) exchangeSixKongGlows[index]?.AttachTo(targets[index]);
    }

    private void ClearChangshaKongPreviews() {
        foreach (var preview in exchangeSixKongPreviews) {
            if (preview == null) continue;
            // CreatePresentationTile创建独立实例，不能归还到四张实体牌池。
            preview.SetActive(false);
            Destroy(preview);
        }
        exchangeSixKongPreviews.Clear();
    }

    public void ClearChangshaExchangeClaims() {
        if (exchangeSixClaimDisplay != null) StopCoroutine(exchangeSixClaimDisplay);
        exchangeSixClaimDisplay = null;
        ClearChangshaKongPreviews();
        foreach (var glow in exchangeSixKongGlows) if (glow != null) {
            glow.Hide();
            Destroy(glow.gameObject);
        }
        exchangeSixKongGlows.Clear();
        HideClaimGlow();
    }
}
