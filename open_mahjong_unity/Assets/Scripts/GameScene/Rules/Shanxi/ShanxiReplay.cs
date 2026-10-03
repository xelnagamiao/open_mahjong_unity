using System;
using System.Collections.Generic;

public partial class GameRecordManager {
    private bool recordShanxiTailSingle;
    private bool IsShanxiRecord() => ReadGameTitleString(gameRecord?.gameTitle, "rule", "") == "shanxi";
    private void RestoreShanxiRonDiscard(IReadOnlyList<string> tick) {
        if (!IsShanxiRecord() || tick == null || tick.Count < 9
            || (tick[0] != "hu_first" && tick[0] != "hu_second" && tick[0] != "hu_third")
            || tick[8] != "1" || !int.TryParse(tick[7], out int payer)
            || !int.TryParse(tick[5], out int tile)
            || !indexToPosition.TryGetValue(payer, out string seat)
            || !recordPlayer_to_info.TryGetValue(seat, out RecordPlayer player)) return;
        // 和牌张被唯一赢家取得。回退/跳转和顺序播放复用同一个逻辑，保持实体牌守恒。
        if (player.discardTiles.Count > 0 && player.discardTiles[player.discardTiles.Count - 1] == tile)
            RemoveClaimedDiscardFromRecordRiver(player, tile, capturePendingRiichiHorizontal: false);
    }
    private void ConsumeShanxiRecordWallTile(string action) {
        if (currentTilesList.Count == 0) return;
        int position = action == "d" ? 0 : currentTilesList.Count - (recordShanxiTailSingle ? 1 : 2);
        if (position < 0) throw new InvalidOperationException("山西补牌记录超出牌墙");
        int original = currentOriginalIndices[position];
        currentTilesList.RemoveAt(position);
        currentOriginalIndices.RemoveAt(position);
        if (action == "d") consumedFromFront++;
        else {
            consumedBackIndices.Add(original);
            recordShanxiTailSingle = !recordShanxiTailSingle;
            recordDeadWallCount = recordShanxiTailSingle ? 15 : 14;
        }
    }
}
