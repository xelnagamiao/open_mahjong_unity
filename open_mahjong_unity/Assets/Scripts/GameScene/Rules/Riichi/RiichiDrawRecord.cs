using System.Collections.Generic;

/// <summary>荒牌牌谱按当局座位的听牌申报标记，从已重建的终局手牌恢复听张。</summary>
public static class RiichiDrawRecord {
    public static Dictionary<int, int[]> BuildTenpaiTiles(IReadOnlyList<int> tenpaiFlags,
        IEnumerable<GameRecordManager.RecordPlayer> players) {
        var result = new Dictionary<int, int[]>();
        if (tenpaiFlags == null || players == null) return result;
        foreach (var player in players) {
            if (player == null || player.playerIndex < 0 || player.playerIndex >= tenpaiFlags.Count
                || tenpaiFlags[player.playerIndex] != 1) continue;
            var waiting = RiichiTips.Tingpai(new TingpaiQuery {
                Hand = player.tileList,
                Melds = player.combinationTiles,
            });
            if (waiting.Count == 0) continue;
            var tiles = new List<int>(waiting);
            tiles.Sort();
            result[player.playerIndex] = tiles.ToArray();
        }
        return result;
    }
}
