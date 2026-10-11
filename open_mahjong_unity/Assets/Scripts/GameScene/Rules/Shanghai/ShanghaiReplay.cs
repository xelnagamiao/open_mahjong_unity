using System;
using System.Collections.Generic;

public partial class GameRecordManager {
    internal RuleReplayModule CreateShanghaiReplayModule() => new RuleReplayModule("lajihe_eight_flowers") {
        ApplyBeforeMutation = ApplyLajiheRecordAction,
        AnnounceBeforeAction = (tick, position, voice) => PlayShanghaiRecordAnnouncement(tick),
        AfterSilentMutation = tick => SyncRecordBoardScores(),
    };

    private bool ApplyLajiheRecordAction(IReadOnlyList<string> tick) {
        if (ReadGameTitleString(gameRecord?.gameTitle, "sub_rule", "") != LajiheHandCalculator.SubRule
            || tick == null || tick.Count == 0) return false;
        if (tick[0] == "lajihe_eight_flowers" && tick.Count >= 5) {
            var changes = Newtonsoft.Json.JsonConvert.DeserializeObject<int[]>(tick[4]);
            var deltas = new Dictionary<int, int>();
            MapTickScoreChangesToDeltas(changes, deltas);
            ApplyScoreDeltas(deltas, out _, out _);
            return true;
        }
        if (tick[0] == "state" && tick.Count >= 4 && tick[1] == "lajihe_passed_win") {
            int index = int.Parse(tick[2]);
            if (indexToPosition.TryGetValue(index, out string position) && recordPlayer_to_info.TryGetValue(position, out RecordPlayer player)) {
                player.tagList.Remove("lajihe_passed_win");
                if (tick[3] == "T") player.tagList.Add("lajihe_passed_win");
            }
            return true;
        }
        return false;
    }

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
