using System;
using Newtonsoft.Json;

public partial class NetworkManager {
    public event Action<Response> MailResponseReceived;

    public async void RequestMail(MailCommand command) {
        try {
            if (!IsWebSocketOpen) throw new InvalidOperationException();
            await GetWebSocket().SendText(JsonConvert.SerializeObject(command));
        } catch (Exception) {
            MailResponseReceived?.Invoke(new Response {
                type = command.type, request_id = command.request_id, success = false,
                mail_retryable = true, message = "连接已断开；重连后可重试原操作"
            });
        }
    }

    private void HandleMailResponse(Response response) {
        var state = response.inventory_state;
        if (response.success && state != null && UserDataManager.Instance != null
            && state.user_id == UserDataManager.Instance.UserId) {
            ConfigManager.ApplyInventoryCatalog(state.catalog);
            UserDataManager.Instance.ApplyInventoryState(state);
        }
        MailResponseReceived?.Invoke(response);
    }
}
