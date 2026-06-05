using UnityEngine;
using Newtonsoft.Json;
using NativeWebSocket;

public class MatchNetworkManager : MonoBehaviour {
    public static MatchNetworkManager Instance { get; private set; }
    private bool isMatchFoundLocked;
    /// <summary>最近一次请求加入的队列标识，用于 UI 展示（不依赖服务器 message 文案）。</summary>
    private string lastJoinedQueueType;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void HandleMatchMessage(Response response) {
        switch (response.type) {
            case "match/join_queue_done":
                HandleJoinQueueDone(response);
                break;
            case "match/leave_queue_done":
                HandleLeaveQueueDone(response);
                break;
            case "match/queue_status":
                HandleQueueStatus(response);
                break;
            case "match/match_found":
                HandleMatchFound(response);
                break;
        }
    }

    private void HandleJoinQueueDone(Response response) {
        if (response.success) {
            if (!isMatchFoundLocked) {
                MatchQueueingPanel.Instance?.Show(MatchQueueDisplayText.GetQueueTitle(lastJoinedQueueType));
            }
        } else {
            NotificationManager.Instance.ShowTip("匹配", false, response.message);
        }
    }

    private void HandleLeaveQueueDone(Response response) {
        isMatchFoundLocked = false;
        MatchQueueingPanel.Instance?.Hide();
    }

    private void HandleQueueStatus(Response response) {
        if (response.queue_status != null) {
            MatchPanel.Instance?.UpdateQueueStatus(response.queue_status);
        }
    }

    private void HandleMatchFound(Response response) {
        isMatchFoundLocked = true;
        MatchFoundedPanel.Instance?.Show(MatchQueueDisplayText.GetQueueTitle(lastJoinedQueueType));
    }

    // 发送加入匹配队列请求
    public async void SendJoinQueue(string queueType) {
        if (UserDataManager.Instance != null && UserDataManager.Instance.IsTourist) {
            NotificationManager.Instance?.ShowTip("匹配", false, "游客无法进行排位匹配，请先注册账号");
            return;
        }
        isMatchFoundLocked = false;
        lastJoinedQueueType = queueType;
        if (NetworkManager.Instance == null) {
            Debug.LogWarning("[MatchNetworkManager] NetworkManager 不存在，无法发送加入匹配请求");
            return;
        }
        var ws = NetworkManager.Instance.GetWebSocket();
        if (ws == null || ws.State != WebSocketState.Open) {
            Debug.LogWarning("[MatchNetworkManager] WebSocket未连接，无法发送加入匹配请求");
            return;
        }
        var msg = new { type = "match/join_queue", queue_type = queueType };
        string json = JsonConvert.SerializeObject(msg);
        try {
            await ws.SendText(json);
        } catch (System.Exception e) {
            Debug.LogError($"[MatchNetworkManager] 发送加入匹配请求失败: {e.Message}");
        }
    }

    // 发送离开匹配队列请求
    public async void SendLeaveQueue() {
        if (NetworkManager.Instance == null) return;
        var ws = NetworkManager.Instance.GetWebSocket();
        if (ws == null || ws.State != WebSocketState.Open) return;
        var msg = new { type = "match/leave_queue" };
        try {
            await ws.SendText(JsonConvert.SerializeObject(msg));
        } catch (System.Exception e) {
            Debug.LogError($"[MatchNetworkManager] 发送离开匹配请求失败: {e.Message}");
        }
    }

    // 发送获取队列状态请求
    public async void SendGetQueueStatus() {
        if (NetworkManager.Instance == null) return;
        var ws = NetworkManager.Instance.GetWebSocket();
        if (ws == null || ws.State != WebSocketState.Open) return;
        var msg = new { type = "match/get_queue_status" };
        string json = JsonConvert.SerializeObject(msg);
        try {
            await ws.SendText(json);
        } catch (System.Exception e) {
            Debug.LogError($"[MatchNetworkManager] 发送队列状态请求失败: {e.Message}");
        }
    }
}
