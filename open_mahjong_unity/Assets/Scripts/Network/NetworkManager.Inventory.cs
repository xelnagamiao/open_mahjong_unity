using System;
using Newtonsoft.Json;

public partial class NetworkManager {
    public event Action<Response> InventoryResponseReceived;

    public async void RequestInventory(InventoryCommand command) {
        try {
            if (!IsWebSocketOpen) throw new InvalidOperationException();
            await GetWebSocket().SendText(JsonConvert.SerializeObject(command));
        } catch (Exception) {
            InventoryResponseReceived?.Invoke(new Response {
                type = command.type, request_id = command.request_id, success = false,
                inventory_retryable = true, message = "连接已断开；重连后可重试原操作"
            });
        }
    }

    private void HandleInventoryResponse(Response response) {
        if (response.success) {
            ConfigManager.ApplyInventoryCatalog(response.inventory_catalog);
            var state = response.inventory_state;
            if (state != null && UserDataManager.Instance != null && state.user_id == UserDataManager.Instance.UserId) {
                ConfigManager.ApplyInventoryCatalog(state.catalog);
                UserDataManager.Instance.ApplyInventoryState(state);
            }
            if (response.inventory_appearances != null)
                foreach (var appearance in response.inventory_appearances) GameSettings.NotifyAppearanceChanged(appearance);
        }
        InventoryResponseReceived?.Invoke(response);
    }
}
