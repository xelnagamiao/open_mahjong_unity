using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public partial class GameRecordManager {
    internal RuleReplayModule CreateGuangdongMilReplayModule() => new RuleReplayModule("guangdong") {
        Reset = ResetGuangdongRecordState,
        Clear = () => {
            GuangdongServerTips.Clear(true);
            recordGuangdongState = null; recordGuangdongResult = null; recordGuangdongHorses = null;
        },
        ApplyBeforeMutation = ApplyGuangdongRecordAction,
        AfterSilentMutation = tick => {
            SyncRecordBoardScores();
            if (indexToPosition.TryGetValue(currentPlayerIndex, out string position))
                BoardCanvas.Instance?.ShowCurrentPlayer(position, GetRecordRemainTiles());
        },
    };

    private GuangdongPublicState recordGuangdongState;
    private GuangdongResult recordGuangdongResult;
    private GuangdongHorses recordGuangdongHorses;
    private bool IsGuangdongMilRecord() => GuangdongMilRules.IsMil(ReadGameTitleString(gameRecord?.gameTitle, "sub_rule", ""));
    private void ResetGuangdongRecordState() {
        GuangdongServerTips.Clear(true);
        var subRule = ReadGameTitleString(gameRecord?.gameTitle, "sub_rule", "");
        recordGuangdongState = GuangdongMilRules.IsMil(subRule) ? new GuangdongPublicState {
            edition = subRule == GuangdongMilRules.Doubles ? GuangdongMilRules.DoublesVersion : GuangdongMilRules.Version,
            ghost_discard_counts = new int[4], kong_ledger = new List<GuangdongKongEntry>()
        } : null;
        recordGuangdongResult = null; recordGuangdongHorses = null;
        RefreshTaiwanRecordPlayerTags();
    }
    private void RefreshGuangdongRecordTeamScores() {
        if (recordGuangdongState?.edition != GuangdongMilRules.DoublesVersion || RoundPanel.Instance == null) return;
        var scores = new Dictionary<int, int>();
        foreach (var player in recordPlayerList) scores[player.playerIndex] = player.score;
        RoundPanel.Instance.UpdateSichuanSettlement(new ShowResultInfo { liuju_status_final = true, player_to_score = scores });
    }

    private bool ApplyGuangdongRecordAction(IReadOnlyList<string> tick) {
        if (!IsGuangdongMilRecord() || tick == null || tick.Count == 0) return false;
        if (tick[0] == "liuju") RefreshGuangdongRecordTeamScores();
        if (ReadGameTitleString(gameRecord?.gameTitle, "sub_rule", "") == GuangdongMilRules.SubRule)
            RemoveTuidaoWonDiscard(tick);
        if (tick.Count < 2 || tick[0] != "guangdong") return false;
        switch (tick[1]) {
            case "ghost_discard":
                if (tick.Count >= 4 && int.TryParse(tick[2], out int seat) && seat >= 0 && seat < 4 && int.TryParse(tick[3], out int count))
                    recordGuangdongState.ghost_discard_counts[seat] = count;
                break;
            case "kong_score":
            case "penalty":
            case "refund_kongs":
                if (tick[1] == "penalty" && tick.Count >= 5 && (tick[4] == "false_win" || tick[4] == "second_chicken")
                    && int.TryParse(tick[3], out int penalized) && indexToPosition.TryGetValue(penalized, out var position)
                    && recordPlayer_to_info.TryGetValue(position, out var player) && !player.tagList.Contains("peida"))
                    player.tagList.Add("peida");
                if (tick.Count >= 3) {
                    int[] changes = JsonConvert.DeserializeObject<int[]>(tick[2]);
                    if (changes != null && changes.Length == 4) {
                        var deltas = new Dictionary<int,int>(); MapTickScoreChangesToDeltas(changes, deltas);
                        ApplyScoreDeltas(deltas, out _, out _);
                        if (tick[1] == "refund_kongs") recordGuangdongResult = new GuangdongResult { draw = true, refund_changes = changes };
                        else if (tick[1] == "kong_score" && tick.Count >= 6) recordGuangdongState.kong_ledger.Add(new GuangdongKongEntry {
                            kind=tick[3], payer=int.TryParse(tick[4],out int payer)?payer:(int?)null,
                            tile=int.TryParse(tick[5],out int tile)?tile:(int?)null, changes=changes,
                        });
                    }
                }
                break;
            case "horses":
                if (tick.Count >= 3) {
                    recordGuangdongHorses = JsonConvert.DeserializeObject<GuangdongHorses>(tick[2]);
                    int drawn = Math.Min(currentTilesList.Count, recordGuangdongHorses?.tiles?.Length ?? 0);
                    currentTilesList.RemoveRange(0, drawn); currentOriginalIndices.RemoveRange(0, drawn); consumedFromFront += drawn;
                }
                break;
            case "settlement":
                if (tick.Count >= 3) {
                    recordGuangdongResult = JsonConvert.DeserializeObject<GuangdongResult>(tick[2]);
                    if (recordGuangdongResult != null && recordGuangdongResult.horses == null) recordGuangdongResult.horses = recordGuangdongHorses;
                }
                break;
            case "tips":
                if (tick.Count >= 4 && int.TryParse(tick[2],out int tipSeat)) {
                    GuangdongServerTips.Accept(JsonConvert.DeserializeObject<GuangdongTips>(tick[3]), true, tipSeat);
                    if (isActiveAndEnabled) RefreshRecordTips();
                }
                break;
        }
        RefreshTaiwanRecordPlayerTags();
        return true;
    }
}
