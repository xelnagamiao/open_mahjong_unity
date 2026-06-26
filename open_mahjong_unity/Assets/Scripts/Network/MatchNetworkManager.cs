using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using NativeWebSocket;

public enum MatchQueueStatusConsumer {
    MenuTotalCount,
    MatchPanelDetail,
}

public class MatchNetworkManager : MonoBehaviour {
    public static MatchNetworkManager Instance { get; private set; }
    private bool isMatchFoundLocked;
    private string lastJoinedQueueType;
    private readonly Queue<MatchQueueStatusConsumer> pendingQueueStatusConsumers = new Queue<MatchQueueStatusConsumer>();

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
        if (!response.success) {
            NotificationManager.Instance.ShowTip("匹配", false, response.message);
            return;
        }
        if (IsMatchUiLocked()) {
            ShowMatchFoundedUi();
            return;
        }
        CoroutineManager.Ensure();
        CoroutineManager.Instance.RunNextFrame(ShowQueueingPanelIfStillNeeded, CoroutineKeys.MatchQueueingPanelDelay);
    }

    private bool IsMatchUiLocked() {
        return isMatchFoundLocked
            || (MatchStateManager.Instance != null && MatchStateManager.Instance.IsMatchFound);
    }

    private void ShowMatchFoundedUi() {
        MatchQueueingPanel.Instance?.HideImmediately();
        MatchFoundedPanel.Instance?.Show(MatchQueueDisplayText.GetQueueTitle(lastJoinedQueueType));
    }

    private void ShowQueueingPanelIfStillNeeded() {
        if (IsMatchUiLocked()) {
            ShowMatchFoundedUi();
            return;
        }
        MatchQueueingPanel.Instance?.Show(MatchQueueDisplayText.GetQueueTitle(lastJoinedQueueType));
    }

    private void HandleLeaveQueueDone(Response response) {
        isMatchFoundLocked = false;
        MatchStateManager.Instance?.StopQueueing();
        MatchQueueingPanel.Instance?.HideImmediately();
        MatchFoundedPanel.Instance?.StopCountdownAndHide();
    }

    private void HandleQueueStatus(Response response) {
        if (response.queue_status == null) return;
        if (pendingQueueStatusConsumers.Count == 0) return;

        switch (pendingQueueStatusConsumers.Dequeue()) {
            case MatchQueueStatusConsumer.MenuTotalCount:
                MeunPanel.Instance?.UpdateMatchPlayerCount(response.queue_status);
                break;
            case MatchQueueStatusConsumer.MatchPanelDetail:
                MatchPanel.Instance?.UpdateQueueStatus(response.queue_status);
                break;
        }
    }

    private void HandleMatchFound(Response response) {
        isMatchFoundLocked = true;
        ShowMatchFoundedUi();
    }

    public void ResetMatchLock() {
        isMatchFoundLocked = false;
    }

    public async void SendJoinQueue(string queueType) {
        if (UserDataManager.Instance != null && UserDataManager.Instance.IsTourist) {
            NotificationManager.Instance?.ShowTip("匹配", false, "游客无法进行排位匹配，请先注册账号");
            return;
        }
        if (isMatchFoundLocked) {
            NotificationManager.Instance?.ShowTip("匹配", false, "已匹配到对局，正在进入游戏");
            return;
        }
        if (GameSessionGuard.HasExclusiveSession) {
            NotificationManager.Instance?.ShowTip("匹配", false, "当前对局尚未结束，无法匹配");
            return;
        }
        if (LobbyStateGuard.BlockIfInRoomForMatch()) {
            return;
        }
        GameRecordManager.Instance?.AbandonDelayedSpectatorSessionOnServer();
        if (MatchStateManager.Instance != null && MatchStateManager.Instance.IsQueueing) {
            NotificationManager.Instance?.ShowTip("匹配", false, "您已在匹配队列中");
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
        try {
            await ws.SendText(JsonConvert.SerializeObject(msg));
        } catch (System.Exception e) {
            Debug.LogError($"[MatchNetworkManager] 发送加入匹配请求失败: {e.Message}");
        }
    }

    public async void SendLeaveQueue() {
        if (NetworkManager.Instance == null) return;
        var ws = NetworkManager.Instance.GetWebSocket();
        if (ws == null || ws.State != WebSocketState.Open) return;
        try {
            await ws.SendText(JsonConvert.SerializeObject(new { type = "match/leave_queue" }));
        } catch (System.Exception e) {
            Debug.LogError($"[MatchNetworkManager] 发送离开匹配请求失败: {e.Message}");
        }
    }

    public void RequestQueueStatusForMenu() {
        RequestQueueStatus(MatchQueueStatusConsumer.MenuTotalCount);
    }

    public void RequestQueueStatusForMatchPanel() {
        RequestQueueStatus(MatchQueueStatusConsumer.MatchPanelDetail);
    }

    private async void RequestQueueStatus(MatchQueueStatusConsumer consumer) {
        if (NetworkManager.Instance == null) return;
        var ws = NetworkManager.Instance.GetWebSocket();
        if (ws == null || ws.State != WebSocketState.Open) return;

        pendingQueueStatusConsumers.Enqueue(consumer);
        try {
            await ws.SendText(JsonConvert.SerializeObject(new { type = "match/get_queue_status" }));
        } catch (System.Exception e) {
            if (pendingQueueStatusConsumers.Count > 0
                && pendingQueueStatusConsumers.Peek() == consumer) {
                pendingQueueStatusConsumers.Dequeue();
            }
            Debug.LogError($"[MatchNetworkManager] 发送队列状态请求失败: {e.Message}");
        }
    }
}
