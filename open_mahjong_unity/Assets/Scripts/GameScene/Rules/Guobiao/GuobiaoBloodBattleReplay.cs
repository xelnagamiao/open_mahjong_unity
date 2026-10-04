using System.Collections.Generic;

public partial class GameRecordManager {
    private Dictionary<int, int[]> bloodRecordRevealedHands;

    // Keep the winning tile separate from the frozen concealed hand, just as live snapshots do.
    private void CaptureBloodBattleRecordWin(List<string> tick, string action, bool removeRiver) {
        if (!IsBloodBattleRecord()) return;
        int winner = ParseTickInt(tick, 1);
        if (!indexToPosition.TryGetValue(winner, out string position)) return;
        RecordPlayer player = recordPlayer_to_info[position];
        RecordHuHandBuilder.ParseSichuanHuExtras(tick, out int tile, out bool multi, out int? source, out bool recycle);
        tile = ResolveRecordHepaiTile(action, winner, tile, player);
        player.bloodWinTile = tile;
        player.bloodWinZimo = action == "hu_self";
        player.bloodWinMulti = multi;
        if (player.tileList.Count > 0 && player.tileList[player.tileList.Count - 1] == tile) {
            player.tileList.RemoveAt(player.tileList.Count - 1);
        }
        player.showHandDrawSlotActive = false;
        player.hasRonWinningTile = false;
        bool qianggang = ContainsSichuanQianggangFan(ParseHuFanList(tick, 3));
        string sourcePosition = ResolveRecordRonDiscarderPosition(source);
        if (qianggang && recordPlayer_to_info.TryGetValue(sourcePosition ?? "", out RecordPlayer from)) {
            for (int i = 0; i < from.combinationTiles.Count; i++) {
                if (from.combinationTiles[i] != $"g{tile}") continue;
                var mask = new List<int>(from.combinationMasks[i]);
                for (int j = 0; j < mask.Count; j += 2) {
                    if (mask[j] != 3) continue;
                    mask.RemoveRange(j, 2);
                    from.combinationMasks[i] = mask.ToArray();
                    from.combinationTiles[i] = $"k{tile}";
                    break;
                }
            }
        }
        if (removeRiver && !qianggang && action != "hu_self" && (recycle || !multi)) {
            SyncRecordRonDiscardRemoved(sourcePosition, tile);
        }
    }

    private void RestoreBloodBattleReplayMarker(string position, RecordPlayer player) {
        if (IsBloodBattleRecord() && player.isHu && player.bloodWinTile >= 10 && bloodRecordRevealedHands == null) {
            Game3DManager.Instance.RestoreBloodBattleWinMarker(position, player.bloodWinTile, player.bloodWinZimo, player.bloodWinMulti);
        }
    }
}
