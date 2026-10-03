using System;
using Newtonsoft.Json;

public partial class NetworkManager {
    public event Action<Response> TitleResponseReceived;

    public async void RequestTitles(string requestId, int? titleId = null) {
        string kind = titleId.HasValue ? "title/equip" : "title/get";
        try {
            if (!IsWebSocketOpen) throw new InvalidOperationException("连接已断开，请重连后重试");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(new {
                type = kind, request_id = requestId, title_id = titleId
            }));
        } catch (Exception) {
            TitleResponseReceived?.Invoke(new Response {
                type = kind, request_id = requestId, success = false, message = "连接已断开，请重连后重试"
            });
        }
    }

    private void HandleTitleResponse(Response response) {
        if (response.success) {
            ConfigManager.ApplyTitleCatalog(response.title_catalog);
            if (response.title_state != null && UserDataManager.Instance != null
                && response.title_state.user_id == UserDataManager.Instance.UserId) {
                ConfigManager.ApplyTitleCatalog(response.title_state.catalog);
                UserDataManager.Instance.ApplyTitleState(response.title_state);
            }
            if (response.title_changes != null) {
                foreach (TitleChange change in response.title_changes)
                    GameSettings.NotifyTitleChanged(change.user_id, change.title_id);
            }
        }
        TitleResponseReceived?.Invoke(response);
    }
}
