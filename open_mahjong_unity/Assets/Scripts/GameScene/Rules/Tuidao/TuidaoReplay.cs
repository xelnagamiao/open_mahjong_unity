using System.Collections.Generic;
using Newtonsoft.Json;

public partial class GameRecordManager {
    private void RemoveTuidaoWonDiscard(IReadOnlyList<string> tick) {
        if (tick.Count < 8 || (tick[0] != "hu_first" && tick[0] != "hu_second" && tick[0] != "hu_third")) return;
        // 本规则只有一个赢家；既有紧凑 tick 在和牌张后写放铳者/收回标记。
        // 同时兼容带显式 multi_ron 占位的通用格式。抢杠没有收回牌河标记。
        int payerField = tick.Count >= 9 ? 7 : 6;
        if (tick[payerField + 1] != "1" || !int.TryParse(tick[payerField], out int payer)
            || !int.TryParse(tick[5], out int tile) || !indexToPosition.TryGetValue(payer, out string position)
            || !recordPlayer_to_info.TryGetValue(position, out RecordPlayer player)) return;
        if (player.discardTiles.Count > 0 && player.discardTiles[player.discardTiles.Count - 1] == tile)
            RemoveClaimedDiscardFromRecordRiver(player, tile, capturePendingRiichiHorizontal: false);
    }

    private void PlayTuidaoRecordAnnouncement(IReadOnlyList<string> tick) {
        if (RecordRuleManifest?.RuleId != TuidaoRuleBootstrap.RuleId || IsGuangdongMilRecord() || tick == null
            || tick.Count < 5 || tick[0] != "state" || tick[1] != "ready" || tick[4] != "T") return;
        if (int.TryParse(tick[2], out int seat) && indexToPosition.TryGetValue(seat, out string position)
            && recordPlayer_to_info.TryGetValue(position, out RecordPlayer player))
            SoundManager.Instance?.PlayActionSound(position, "riichi", player.voice_used);
    }

    private bool ApplyTuidaoRecordAction(IReadOnlyList<string> tick) {
        if (RecordRuleManifest?.RuleId != TuidaoRuleBootstrap.RuleId || IsGuangdongMilRecord() || tick == null) return false;
        RemoveTuidaoWonDiscard(tick);
        if (tick.Count < 3 || tick[0] != "tuidao" || tick[1] != "kong_score") return false;
        int[] changes = JsonConvert.DeserializeObject<int[]>(tick[2]);
        if (changes == null || changes.Length != 4) return true;
        var deltas = new Dictionary<int, int>();
        MapTickScoreChangesToDeltas(changes, deltas);
        ApplyScoreDeltas(deltas, out _, out _);
        return true;
    }
}
