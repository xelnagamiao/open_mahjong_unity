#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using Newtonsoft.Json;

/// <summary>备用 101/102 及旧 sign 0~4 的显示兼容回归；从 Editor bridge 显式运行。</summary>
public static class MeldStackValidation {
    public static string Run() {
        int checks = 0;
        void Check(bool value, string label) {
            if (!value) throw new InvalidOperationException("MeldStack: " + label);
            checks++;
        }
        int[] mixed = { 0, 11, 101, 12, 102, 13, 1, 21, 102, 22, 4, 0, 101, 23 };
        int[] copy = (int[])mixed.Clone();
        var stacks = MeldStackLayout.Build(mixed);
        Check(stacks.Select(tile => tile.AnchorIndex).SequenceEqual(new[] { 0, 0, 3, 3 }), "original-order anchors");
        Check(stacks.Select(tile => tile.Layer).SequenceEqual(new[] { 1, 2, 1, 2 }), "mixed layers and ignored empty slot");
        Check(stacks.Select(tile => tile.Sign).SequenceEqual(new[] { 101, 102, 102, 101 }), "actual sign identifiers");
        Check(stacks.Select(tile => tile.PairIndex).SequenceEqual(new[] { 1, 2, 4, 6 }), "pair indices before reverse");
        Check(mixed.SequenceEqual(copy), "source unchanged");
        Check(!MeldStackLayout.HasStacks(new[] { 0, 101, 1, 102 }), "tile ids are not signs");
        Check(MeldStackLayout.Build(null).Count == 0 && !MeldStackLayout.HasStacks(null), "null mask");
        Check(MeldStackLayout.Build(new[] { 101, 11, 102, 12 }).Count == 0, "orphan stacks ignored");
        Check(MeldStackLayout.Build(new[] { 0, 11, 101 }).Count == 0, "incomplete pair ignored");
        Check(MeldStackLayout.Build(new[] { 0, 11, 99, 12, 101, 13 }).Count == 0, "unknown sign prevents stale anchor");
        Check(MeldStackLayout.Build(new[] { 0, -1, 101, 12 }).Count == 0, "invalid base ignored");
        Check(MeldStackLayout.Build(new[] { 0, 11, 101, -1, 102, 12 })[0].Layer == 1, "invalid stack does not consume height");
        Check(MeldStackLayout.Build(new[] { 2, 0, 102, 0 })[0].TileId == 0, "unknown concealed tiles supported");
        for (int sign = 0; sign <= 3; sign++) {
            var attached = MeldStackLayout.Build(new[] { sign, 11, 101, 12, 102, 13 });
            Check(attached.Count == 2 && attached.All(tile => tile.AnchorIndex == 0), "base sign " + sign);
        }
        var groups = SettlementMeldLayoutBuilder.Build(new[] { mixed });
        Check(groups.Count == 1 && groups[0].Count == 2, "stacks do not add horizontal slots");
        Check(groups[0][0].StackedTiles[0].TileId == 12 && !groups[0][0].StackedTiles[0].FaceDown, "101 face up");
        Check(groups[0][0].StackedTiles[1].TileId == 13 && groups[0][0].StackedTiles[1].FaceDown, "102 face down");
        Check(groups[0][1].Sideways && groups[0][1].StackedTiles.Count == 2, "claimed orientation retained");
        var concealed = SettlementMeldLayoutBuilder.Build(new[] { new[] { 2, 0, 102, 0 } })[0][0];
        Check(concealed.FaceDown && concealed.StackedTiles[0].FaceDown, "no unknown face revealed");
        var added = SettlementMeldLayoutBuilder.Build(new[] { new[] { 0, 25, 0, 25, 3, 125, 101, 11, 102, 12, 1, 25 } })[0];
        Check(added.Count == 3 && added[2].StackedTileId == 125, "legacy added kong retained");
        Check(added[2].StackedTiles.All(tile => tile.OnAddedKong) && added[2].StackedTiles[1].Layer == 2, "stacks on sign 3");
        Check(SettlementMeldLayoutBuilder.Build(new[] { new[] { 101, 12 }, new[] { 0, 11 } })[0].Count == 1, "no cross-meld anchor");
        Check(GameRecordMeldCodec.ExtractHandTilesFromMask(mixed).SequenceEqual(new[] { 11 }), "display stacks do not remove hand tiles");
        Check(GameRecordMeldCodec.ExtractHandTilesFromMask(new[] { 0, 25, 2, 25, 3, 125, 1, 25 }).SequenceEqual(new[] { 25, 25, 125 }), "old hand extraction unchanged");
        var oldAdded = SettlementMeldLayoutBuilder.Build(new[] { new[] { 0, 25, 0, 25, 3, 125, 1, 25 } })[0];
        Check(oldAdded.Count == 3 && oldAdded[2].Sideways && oldAdded[2].StackedTileId == 125, "old sign 3 layout");
        Check(oldAdded.All(tile => tile.StackedTiles == null), "no reserve state for old masks");
        var oldConcealed = SettlementMeldLayoutBuilder.Build(new[] { new[] { 2, 47, 2, 47, 2, 47, 2, 47 } })[0];
        Check(oldConcealed.Select(tile => tile.FaceDown).SequenceEqual(new[] { true, false, false, true }), "old sign 2 settlement");
        var oldEmpty = SettlementMeldLayoutBuilder.Build(new[] { new[] { 0, 11, 4, 12, 1, 13 } })[0];
        Check(oldEmpty.Count == 2 && oldEmpty[1].Sideways, "old sign 4 empty");
        foreach (string action in new[] { "cl", "cm", "cr", "p", "g" }) {
            int[] mask = GameRecordMeldCodec.BuildMingpaiMask(action, 12, new[] { 11, 13, 14 }, "left");
            Check(!MeldStackLayout.HasStacks(mask), "reserve not emitted by " + action);
        }
        Check(!MeldStackLayout.HasStacks(GameRecordMeldCodec.BuildAngangMaskFromRemoved(new[] { 11, 11, 11, 11 }, "riichi")), "reserve not emitted by concealed kong");
        return JsonConvert.SerializeObject(new { passed = checks, signs = new[] { 101, 102 } });
    }
}
#endif
