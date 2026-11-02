using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public static class HongqueReplayKong {
    public static int[] AddedTiles(IReadOnlyList<string> tick) {
        if (tick == null || tick.Count < 4 || tick.Count > 5 || !int.TryParse(tick[1],out int first))
            throw new InvalidOperationException("虹雀杠牌记录不完整");
        var added = tick.Count == 5 ? JsonConvert.DeserializeObject<int[]>(tick[4]) : new[] {first};
        if (added == null || added.Length == 0 || added[0] != first) throw new InvalidOperationException("虹雀杠牌追加张无效");
        return added;
    }
    public static List<int> RemoveTiles(List<int> hand,IReadOnlyList<string> tick) {
        var removed=new List<int>();
        foreach (int tile in AddedTiles(tick)) {
            int index=hand.LastIndexOf(tile);
            if (index < 0) throw new InvalidOperationException("虹雀杠牌追加张不在手牌中");
            hand.RemoveAt(index);removed.Add(tile);
        }
        return removed;
    }
}
