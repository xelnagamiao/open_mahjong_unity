using System;
using System.Collections.Generic;

public partial class GameRecordManager {
    private void PlayShanghaiRecordAnnouncement(IReadOnlyList<string> tick) {
        if (RecordRuleManifest?.RuleId != "shanghai" || tick == null || tick.Count < 5
            || tick[0] != "state" || tick[1] != "ready"
            || !string.Equals(tick[4], "T", StringComparison.OrdinalIgnoreCase)) return;
        string subRule = ReadGameTitleString(gameRecord?.gameTitle, "sub_rule", "shanghai/qiaoma");
        if (!string.IsNullOrEmpty(subRule) && subRule != "shanghai/qiaoma") return;
        if (!int.TryParse(tick[2], out int seat) || !indexToPosition.TryGetValue(seat, out string position)
            || !recordPlayer_to_info.TryGetValue(position, out RecordPlayer player)
            || player.tagList.Contains("declared_ready")) return;
        // 仅顺序播放时调用；GotoAction/观战快照重建只恢复状态，保持静音。
        SoundManager.Instance?.PlayActionSound(position, "riichi", player.voice_used);
    }
}
