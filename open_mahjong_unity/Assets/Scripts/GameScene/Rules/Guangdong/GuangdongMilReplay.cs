using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public partial class GameRecordManager {
    private GuangdongPublicState recordGuangdongState;
    private GuangdongResult recordGuangdongResult;
    private GuangdongHorses recordGuangdongHorses;
    private bool IsGuangdongMilRecord() => ReadGameTitleString(gameRecord?.gameTitle, "sub_rule", "") == GuangdongMilRules.SubRule;
    private void ResetGuangdongRecordState() {
        GuangdongServerTips.Clear(true);
        recordGuangdongState = IsGuangdongMilRecord() ? new GuangdongPublicState { edition = GuangdongMilRules.Version, ghost_discard_counts = new int[4], kong_ledger = new List<GuangdongKongEntry>() } : null;
        recordGuangdongResult = null; recordGuangdongHorses = null;
        GuangdongLedgerPanel.Show(recordGuangdongState, null);
    }
    private bool ApplyGuangdongRecordAction(IReadOnlyList<string> tick) {
        if (!IsGuangdongMilRecord() || tick == null || tick.Count == 0) return false;
        // Same one-winner compact hu tick as Tuidao; no physical discarded ghost can be claimed.
        RemoveTuidaoWonDiscard(tick);
        if (tick.Count < 2 || tick[0] != "guangdong") return false;
        switch (tick[1]) {
            case "ghost_discard":
                if (tick.Count >= 4 && int.TryParse(tick[2], out int seat) && seat >= 0 && seat < 4 && int.TryParse(tick[3], out int count))
                    recordGuangdongState.ghost_discard_counts[seat] = count;
                break;
            case "kong_score":
            case "refund_kongs":
                if (tick.Count >= 3) {
                    int[] changes = JsonConvert.DeserializeObject<int[]>(tick[2]);
                    if (changes != null && changes.Length == 4) {
                        var deltas = new Dictionary<int,int>(); MapTickScoreChangesToDeltas(changes, deltas);
                        ApplyScoreDeltas(deltas, out _, out _);
                        if (tick[1] == "refund_kongs") recordGuangdongResult = new GuangdongResult { draw = true, refund_changes = changes };
                        else if (tick.Count >= 6) recordGuangdongState.kong_ledger.Add(new GuangdongKongEntry {
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
        GuangdongLedgerPanel.Show(recordGuangdongState, recordGuangdongResult, recordGuangdongHorses);
        return true;
    }
}
