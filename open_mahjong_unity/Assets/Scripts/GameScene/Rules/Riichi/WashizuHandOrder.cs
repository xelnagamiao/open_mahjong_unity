using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Replay presentation order, kept separate from the calculator's draw-last hand.</summary>
public sealed class WashizuHandOrder {
    private List<(int key, int tile)> entries;
    private int[] logical;
    private int nextKey, revision, drawnKey;
    private bool hasDraw, pinned;

    public WashizuHandOrder(IEnumerable<int> hand, bool legacyGrouped = false) {
        logical = hand.ToArray();
        IEnumerable<int> initial = legacyGrouped
            ? logical.OrderBy(t => !WashizuTile.IsTransparent(t)).ThenBy(WashizuTile.Face) : logical;
        entries = initial.Select(t => (++nextKey, t)).ToList();
    }

    public void Reconcile(IEnumerable<int> hand, bool draw) {
        int[] next = hand.ToArray();
        draw &= next.Length > 0;
        if (next.SequenceEqual(logical) && draw == hasDraw) return;
        var remaining = next.ToList();
        foreach (var group in entries.GroupBy(e => e.tile).ToArray()) {
            int remove = group.Count() - next.Count(t => t == group.Key);
            foreach (var entry in group.OrderBy(e => e.key == drawnKey).Take(Math.Max(0, remove)).ToArray())
                entries.Remove(entry);
        }
        foreach (var entry in entries) remaining.Remove(entry.tile);
        bool added = remaining.Count > 0;
        foreach (int tile in remaining) entries.Add((++nextKey, tile));
        if (draw) {
            if (!entries.Any(e => e.key == drawnKey && e.tile == next[next.Length - 1]))
                drawnKey = entries.Last(e => e.tile == next[next.Length - 1]).key;
            if (added || !hasDraw) pinned = true;
        } else { drawnKey = 0; pinned = false; }
        logical = next; hasDraw = draw; revision++;
    }

    public bool Reorder(int[] order, int drawIndex, bool pinDraw, int[] physicalKeys = null) {
        if (order == null || !order.OrderBy(t => t).SequenceEqual(logical.OrderBy(t => t))) return false;
        if (hasDraw) {
            if (drawIndex < 0 || drawIndex >= order.Length || order[drawIndex] != logical[logical.Length - 1]
                || (pinDraw && drawIndex != order.Length - 1)) return false;
        } else if (drawIndex != -1 || pinDraw) return false;
        if (physicalKeys != null) {
            var faces = entries.ToDictionary(e => e.key, e => e.tile);
            if (physicalKeys.Length != order.Length || physicalKeys.Distinct().Count() != order.Length
                || physicalKeys.Where((key, i) => !faces.TryGetValue(key, out int face) || face != order[i]).Any()
                || (hasDraw && physicalKeys[drawIndex] != drawnKey)) return false;
            entries = physicalKeys.Select(key => (key, faces[key])).ToList();
            pinned = pinDraw; revision++; return true;
        }
        var remaining = new List<(int key, int tile)>(entries);
        var drawn = entries.FirstOrDefault(e => e.key == drawnKey);
        if (hasDraw) remaining.Remove(drawn);
        var result = new List<(int key, int tile)>();
        for (int i = 0; i < order.Length; i++) {
            var entry = i == drawIndex ? drawn : remaining.First(e => e.tile == order[i]);
            remaining.Remove(entry); result.Add(entry);
        }
        entries = result; pinned = pinDraw; revision++;
        return true;
    }

    public WashizuHandLayout Snapshot(int round, bool reveal) => new WashizuHandLayout {
        round_index = round, revision = revision,
        tiles = entries.Select(e => reveal || WashizuTile.IsTransparent(e.tile) ? e.tile : 0).ToArray(),
        keys = entries.Select(e => e.key).ToArray(), drawn_key = drawnKey, draw_pinned = pinned,
    };

    public int NoteCut(int tile, bool fromDraw, int physicalKey = 0) {
        var chosen = entries.FirstOrDefault(e => e.tile == tile &&
            (physicalKey != 0 ? e.key == physicalKey : fromDraw ? e.key == drawnKey : e.key != drawnKey));
        int index = chosen.key != 0 ? entries.IndexOf(chosen) : -1;
        if (index >= 0) entries.RemoveAt(index);
        return index;
    }

    public static int[] OrderSubset(IEnumerable<int> subset, IEnumerable<int> order) {
        var remaining = subset.ToList();
        var result = new List<int>();
        foreach (int tile in order) if (remaining.Remove(tile)) result.Add(tile);
        result.AddRange(remaining);
        return result.ToArray();
    }
}
