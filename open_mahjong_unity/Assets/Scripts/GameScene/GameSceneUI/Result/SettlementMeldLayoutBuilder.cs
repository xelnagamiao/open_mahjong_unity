using System.Collections.Generic;

/// <summary>结算副露布局，与 2D buildSettlementMeldGroups 使用相同的掩码语义。</summary>
public static class SettlementMeldLayoutBuilder {
    public sealed class TileSlot {
        public int TileId;
        public bool Sideways;
        public bool FaceDown;
        public int? StackedTileId;
    }

    public static List<List<TileSlot>> Build(int[][] masks) {
        var groups = new List<List<TileSlot>>();
        if (masks == null) return groups;
        foreach (int[] mask in masks) {
            if (mask == null) continue;
            var slots = new List<TileSlot>();
            var addedTiles = new List<int>();
            int concealedCount = 0;
            for (int i = 0; i + 1 < mask.Length; i += 2) {
                int mode = mask[i];
                int tile = mask[i + 1];
                if (mode == 4 || tile <= 10) continue;
                if (mode == 3) {
                    addedTiles.Add(tile);
                    continue;
                }
                if (mode == 2) concealedCount++;
                slots.Add(new TileSlot { TileId = tile, Sideways = mode == 1, FaceDown = mode == 2 });
            }
            if (slots.Count == 4 && concealedCount == 4 && addedTiles.Count == 0) {
                slots[1].FaceDown = false;
                slots[2].FaceDown = false;
            }
            TileSlot claimed = slots.Find(slot => slot.Sideways);
            foreach (int added in addedTiles) {
                if (claimed != null && !claimed.StackedTileId.HasValue) {
                    claimed.StackedTileId = added;
                } else {
                    // 旧/损坏掩码缺少来源位时保留牌，不静默丢失第四张。
                    slots.Add(new TileSlot { TileId = added });
                }
            }
            if (slots.Count > 0) groups.Add(slots);
        }
        return groups;
    }
}
