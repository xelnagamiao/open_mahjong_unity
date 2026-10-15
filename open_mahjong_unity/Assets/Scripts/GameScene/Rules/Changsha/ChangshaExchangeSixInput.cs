using Newtonsoft.Json;
using UnityEngine;

internal static class ChangshaExchangeSixInput {
    public static async void Send(string action, int tick, int target = 0, int[] selected = null) {
        if (NormalGameStateManager.Instance.IsRealtimeSpectator) return;
        var request = new SendActionRequest {
            type = "gamestate/" + (RuleRegistry.Current?.OutboundChannel ?? "GB") + "/send_action",
            gamestate_id = UserDataManager.Instance.GamestateId,
            action = action, action_tick = tick, targetTile = target, selectedTiles = selected,
        };
        try {
            await NetworkManager.Instance.GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (System.Exception error) {
            Debug.LogError("换六张操作发送失败: " + error.Message);
        }
    }
}
