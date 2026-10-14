using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class Game3DManager {
    private readonly Dictionary<string, WashizuHandLayout> washizuRealtimeLayouts = new Dictionary<string, WashizuHandLayout>();
    private readonly Dictionary<string, Dictionary<int, (Tile3D tile, uint lease)>> washizuRealtimeTiles = new Dictionary<string, Dictionary<int, (Tile3D, uint)>>();
    private bool IsWashizuExpandedReplay => GameRecordManager.Instance != null
        && GameRecordManager.Instance.gameObject.activeSelf && GameRecordManager.Instance.IsWashizuRealtimeRecord()
        && RecordSetting.Instance != null && RecordSetting.Instance.IsShowCardsMode;

    private void SyncWashizuRecordOrderBeforeAnimation() {
        var record = GameRecordManager.Instance;
        if (record == null || !record.gameObject.activeSelf || !record.IsWashizuRealtimeRecord()) return;
        var layouts = new Dictionary<int, WashizuHandLayout>();
        foreach (var pair in record.indexToPosition) {
            if (pair.Value == "self") continue;
            layouts[pair.Key] = record.GetWashizuRecordLayout(pair.Value, IsWashizuExpandedReplay);
        }
        SetWashizuRealtimeLayouts(layouts, record.indexToPosition);
    }

    private Transform FindRemovedWashizuTile(string seat, Transform parent, int face) {
        if (!washizuRealtimeLayouts.TryGetValue(seat, out var layout)
            || !washizuRealtimeTiles.TryGetValue(seat, out var bindings)) return null;
        var removed = bindings.Where(e => !layout.keys.Contains(e.Key) && e.Value.tile != null
            && e.Value.tile.PoolLeaseVersion == e.Value.lease && e.Value.tile.transform.parent == parent).ToList();
        var match = removed.FirstOrDefault(e => e.Value.tile.GetTileId() == face);
        if (match.Value.tile != null) return match.Value.tile.transform;
        return removed.Count > 0 ? removed[0].Value.tile.transform : null;
    }

    public void SetWashizuRealtimeLayouts(Dictionary<int, WashizuHandLayout> layouts, Dictionary<int, string> positions) {
        if (layouts == null) return;
        foreach (var entry in layouts) {
            if (!positions.TryGetValue(entry.Key, out string seat) || seat == "self" || washizuRevealedSeats.Contains(seat)) continue;
            WashizuHandLayout layout = entry.Value;
            if (layout?.tiles == null || layout.keys == null || layout.tiles.Length != layout.keys.Length) continue;
            if (washizuRealtimeLayouts.TryGetValue(seat, out var previous)
                && (layout.round_index < previous.round_index || (layout.round_index == previous.round_index && layout.revision <= previous.revision))) continue;
            washizuRealtimeLayouts[seat] = layout;
            washizuPublicHands[seat] = (int[])layout.tiles.Clone();
            washizuDirtySeats.Add(seat);
        }
    }

    private IEnumerator AnimateWashizuRealtimeRow(string seat, PosPanel3D panel, WashizuHandLayout layout) {
        if (washizuRevealedSeats.Contains(seat)) yield break;
        bool expanded = IsWashizuExpandedReplay;
        Transform parent = expanded ? panel.ShowCardsPosition : panel.cardsPosition;
        if (parent.childCount != layout.tiles.Length) {
            washizuDirtySeats.Add(seat);
            yield break;
        }
        if (!washizuRealtimeTiles.TryGetValue(seat, out var bindings))
            washizuRealtimeTiles[seat] = bindings = new Dictionary<int, (Tile3D, uint)>();
        bool animate = bindings.Count > 0;
        var unused = parent.Cast<Transform>().Select(t => MahjongObjectPool.GetTile3D(t.gameObject)).ToList();
        var ordered = new Tile3D[layout.keys.Length];
        for (int i = 0; i < ordered.Length; i++) {
            if (bindings.TryGetValue(layout.keys[i], out var bound) && bound.tile != null
                && bound.tile.PoolLeaseVersion == bound.lease && bound.tile.transform.parent == parent
                && unused.Remove(bound.tile)) ordered[i] = bound.tile;
        }
        for (int i = 0; i < ordered.Length; i++) {
            if (ordered[i] != null) continue;
            Tile3D tile = unused.FirstOrDefault(t => t.GetTileId() == layout.tiles[i]) ?? unused[0];
            unused.Remove(tile);
            ordered[i] = tile;
        }
        bindings.Clear();
        var from = new Vector3[ordered.Length];
        var target = new Vector3[ordered.Length];
        GetRecordHandLayoutDirections(seat, out Vector3 direction, out _);
        InitializeTilePlacement();
        Vector3 start = HandRowOrigin(parent, direction);
        bool needsMovement = false;
        for (int i = 0; i < ordered.Length; i++) {
            var tile = ordered[i];
            bindings[layout.keys[i]] = (tile, tile.PoolLeaseVersion);
            tile.transform.SetSiblingIndex(i);
            from[i] = tile.transform.position;
            float offset = layout.draw_pinned && layout.keys[i] == layout.drawn_key
                ? HandDrawSlotOffset(ordered.Length - 1) : handStep * i;
            target[i] = PlaceTileOnTable(start + direction.normalized * offset, tile.transform.rotation);
            needsMovement |= (from[i] - target[i]).sqrMagnitude > .000001f;
            if (expanded) {
                tile.isRecordDrawSlotPinned = layout.draw_pinned && layout.keys[i] == layout.drawn_key;
                MahjongObjectPool.Instance.SetPresentationFace(tile.gameObject, layout.tiles[i]);
                tile.SetFrontFaceHalfTurn(false);
            }
        }
        if (!expanded) {
            ApplyWashizuStandingRow(panel, layout.tiles);
            RenderWashizuReadingRow(seat, panel, layout.tiles);
        } else ClearWashizuReadingRow(seat);
        FollowWashizuReadingRows();
        int generation = _handAnimationGeneration;
        // Draws and discards also advance the layout revision. An already placed
        // hand must not hold the discard queue for a movement that never occurs.
        float elapsed = 0, duration = animate && needsMovement ? .24f : 0;
        while (elapsed < duration) {
            if (generation != _handAnimationGeneration || washizuRevealedSeats.Contains(seat)) yield break;
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / duration));
            for (int i = 0; i < ordered.Length; i++) ordered[i].transform.position = Vector3.Lerp(from[i], target[i], progress);
            FollowWashizuReadingRows();
            yield return null;
        }
        for (int i = 0; i < ordered.Length; i++) ordered[i].transform.position = target[i];
        FollowWashizuReadingRows();
    }
}
