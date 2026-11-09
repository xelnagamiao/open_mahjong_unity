using System;
using Newtonsoft.Json;

public partial class NetworkManager {
    public event Action<Response> CommerceResponseReceived;

    public void RefreshWallet() {
        if (UserDataManager.Instance != null && UserDataManager.Instance.UserId > 0 && IsWebSocketOpen)
            RequestCommerce(new ShopCommand { type = "wallet/get", request_id = Guid.NewGuid().ToString("N") });
    }

    public async void RequestCommerce(ShopCommand command) {
        try {
            if (!IsWebSocketOpen) throw new InvalidOperationException();
            await GetWebSocket().SendText(JsonConvert.SerializeObject(command));
        } catch (Exception) {
            CommerceResponseReceived?.Invoke(new Response {
                type = command.type, request_id = command.request_id, success = false,
                commerce_retryable = true, message = "连接已断开；重连后可重试原购买"
            });
        }
    }

    private void HandleCommerceResponse(Response response) {
        var user = UserDataManager.Instance;
        if (response.success && user != null) {
            user.ApplyWalletState(response.wallet_state);
            if (response.inventory_state != null && response.inventory_state.user_id == user.UserId) {
                ConfigManager.ApplyInventoryCatalog(response.inventory_state.catalog);
                user.ApplyInventoryState(response.inventory_state);
            }
        }
        CommerceResponseReceived?.Invoke(response);
    }
}
