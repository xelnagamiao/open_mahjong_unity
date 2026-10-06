using System.Collections.Generic;

/// <summary>结算副露布局，与 2D buildSettlementMeldGroups 使用相同的掩码语义。</summary>
public static class SettlementMeldLayoutBuilder {
    public sealed class TileSlot {
        public int TileId;
        public bool Sideways;
        public bool FaceDown;
        public int? StackedTileId;
        public List<StackedTile> StackedTiles;
    }

    public sealed class StackedTile {
        public int TileId;
        public bool FaceDown;
        public int Layer;
        public bool OnAddedKong;
    }

    public static List<List<TileSlot>> Build(int[][] masks) {
        var groups = new List<List<TileSlot>>();
        if (masks == null) return groups;
        foreach (int[] mask in masks) {
            if (mask == null) continue;
            var slots = new List<TileSlot>();
            var addedTiles = new List<int>();
            // 备用叠牌按原始 pair 索引归属；旧掩码不分配这张表。
            Dictionary<int, TileSlot> anchors = MeldStackLayout.HasStacks(mask)
                ? new Dictionary<int, TileSlot>() : null;
            var addedIndices = anchors != null ? new List<int>() : null;
            int concealedCount = 0;
            for (int i = 0; i + 1 < mask.Length; i += 2) {
                int mode = mask[i];
                int tile = mask[i + 1];
                if (MeldStackLayout.IsStack(mode)) continue;
                if (mode == 4 || (tile <= 10 && !(anchors != null && tile == 0 && mode >= 0 && mode <= 3))) continue;
                if (mode == 3) {
                    addedTiles.Add(tile);
                    addedIndices?.Add(i / 2);
                    continue;
                }
                if (mode == 2) concealedCount++;
                var slot = new TileSlot { TileId = tile, Sideways = mode == 1, FaceDown = mode == 2 || tile == 0 };
                slots.Add(slot);
                if (anchors != null) anchors[i / 2] = slot;
            }
            if (slots.Count == 4 && concealedCount == 4 && addedTiles.Count == 0) {
                slots[1].FaceDown = slots[1].TileId == 0;
                slots[2].FaceDown = slots[2].TileId == 0;
            }
            TileSlot claimed = slots.Find(slot => slot.Sideways);
            var onAddedKong = anchors != null ? new HashSet<int>() : null;
            for (int index = 0; index < addedTiles.Count; index++) {
                int added = addedTiles[index];
                if (claimed != null && !claimed.StackedTileId.HasValue) {
                    claimed.StackedTileId = added;
                    if (anchors != null) {
                        anchors[addedIndices[index]] = claimed;
                        onAddedKong.Add(addedIndices[index]);
                    }
                } else {
                    // 旧/损坏掩码缺少来源位时保留牌，不静默丢失第四张。
                    var slot = new TileSlot { TileId = added, FaceDown = added == 0 };
                    slots.Add(slot);
                    if (anchors != null) anchors[addedIndices[index]] = slot;
                }
            }
            if (anchors != null) {
                foreach (var stack in MeldStackLayout.Build(mask)) {
                    if (!anchors.TryGetValue(stack.AnchorIndex, out TileSlot slot)) continue;
                    if (slot.StackedTiles == null) slot.StackedTiles = new List<StackedTile>();
                    slot.StackedTiles.Add(new StackedTile {
                        TileId = stack.TileId, FaceDown = stack.Sign == MeldStackLayout.FaceDown || stack.TileId == 0,
                        Layer = stack.Layer, OnAddedKong = onAddedKong.Contains(stack.AnchorIndex),
                    });
                }
            }
            if (slots.Count > 0) groups.Add(slots);
        }
        return groups;
    }
}
