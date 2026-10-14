using System.Collections.Generic;
using Newtonsoft.Json;

public partial class GameRecordManager {
    private readonly Dictionary<int, WashizuHandOrder> washizuRecordOrders = new Dictionary<int, WashizuHandOrder>();
    private int washizuRecordCutIndex = -1;
    public bool IsWashizuRealtimeRecord() => WashizuTile.IsRealtimeSubRule(ReadGameTitleString(gameRecord?.gameTitle, "sub_rule", ""));

    private bool LegacyGroupedRealtimeRecord => ReadGameTitleString(gameRecord?.gameTitle, "sub_rule", "") == WashizuTile.RealtimeSubRule
        && ReadGameTitleString(gameRecord?.gameTitle, "realtime_initial_order", "") != "deal";

    internal RuleReplayModule CreateRealtimeTransparentReplayModule() => new RuleReplayModule {
        Reset = () => washizuRecordOrders.Clear(),
        Clear = () => washizuRecordOrders.Clear(),
        InitializePlayers = InitializeWashizuRecordOrders,
        ApplyBeforeMutation = ApplyWashizuOrderTick,
        AfterVisibleMutation = RefreshWashizuOrderTick,
        StateActions = { "wo" },
    };

    private void InitializeWashizuRecordOrders() {
        washizuRecordOrders.Clear();
        if (!IsWashizuRealtimeRecord()) return;
        foreach (var player in recordPlayerList)
            washizuRecordOrders[player.playerIndex] = new WashizuHandOrder(player.tileList, LegacyGroupedRealtimeRecord);
    }

    public WashizuHandLayout GetWashizuRecordLayout(string position, bool reveal = false) {
        if (!IsWashizuRealtimeRecord() || !recordPlayer_to_info.TryGetValue(position, out var player)) return null;
        if (!washizuRecordOrders.TryGetValue(player.playerIndex, out var order))
            washizuRecordOrders[player.playerIndex] = order = new WashizuHandOrder(player.tileList, LegacyGroupedRealtimeRecord);
        // A ron tile stays in the river; keep its hand display in the separate
        // winning slot so replay rendering borrows a presentation copy.
        order.Reconcile(player.tileList, player.showHandDrawSlotActive || player.hasRonWinningTile);
        return order.Snapshot(currentRoundIndex, reveal);
    }

    private bool ApplyWashizuOrderTick(IReadOnlyList<string> tick) {
        if (!IsWashizuRealtimeRecord()) return false;
        // Reconcile before every mutation as well as when rendering. Seeking must
        // retain the same physical identities as frame-by-frame playback.
        foreach (var position in recordPlayer_to_info.Keys) GetWashizuRecordLayout(position);
        if (tick[0] == "c" && washizuRecordOrders.TryGetValue(currentPlayerIndex, out var cutting)) {
            int key = tick.Count > 5 && tick[4] == "wk" && int.TryParse(tick[5], out int value) ? value : 0;
            washizuRecordCutIndex = cutting.NoteCut(int.Parse(tick[1]), tick[2] == "T", key);
        }
        if (tick[0] != "wo") return false;
        if (tick.Count < 5 || !int.TryParse(tick[1], out int actor)
            || !washizuRecordOrders.TryGetValue(actor, out var order)) return true;
        try {
            order.Reorder(JsonConvert.DeserializeObject<int[]>(tick[2]), int.Parse(tick[3]), tick[4] == "T",
                tick.Count > 5 ? JsonConvert.DeserializeObject<int[]>(tick[5]) : null);
        } catch (System.FormatException) { }
          catch (JsonException) { }
        return true;
    }

    private void RefreshWashizuOrderTick(IReadOnlyList<string> tick) {
        if (tick[0] != "wo" || !IsWashizuRealtimeRecord()) return;
        GameCanvas.Instance?.ApplyWashizuRecordOrder(GetWashizuRecordLayout("self", true));
    }
}
