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
        if (!string.IsNullOrEmpty(response.my_queue)) {
            lastJoinedQueueType = response.my_queue;
        }
        if (!response.success) {
            NotificationManager.Instance.ShowTip("匹配", false, response.message);
            if (!string.IsNullOrEmpty(response.my_queue)) {
                RestoreQueueingFromServer(response.my_queue);
            }
            return;
        }
        if (IsMatchUiLocked()) return;
        CoroutineManager.Ensure();
        CoroutineManager.Instance.RunNextFrame(ShowQueueingPanelIfStillNeeded, CoroutineKeys.MatchQueueingPanelDelay);
    }

    private void RestoreQueueingFromServer(string queueType) {
        if (IsBoundToLiveGame() || IsMatchUiLocked()) return;
        lastJoinedQueueType = queueType;
        MatchStateManager.Instance.EnsureQueueing(MatchQueueDisplayText.GetQueueTitle(queueType));
        MatchQueueingPanel.Instance?.RestoreIfQueueing();
    }

    private bool IsMatchUiLocked() {
        return isMatchFoundLocked
            || MatchStateManager.Instance.IsMatchFound;
    }

    /// <summary>
    /// 已经进桌，或本地仍绑定 gamestate（对局挂后台、重连拆桌后等待 game_start）。
    /// </summary>
    private static bool IsBoundToLiveGame() {
        if (GameSessionGuard.HasExclusiveSession) return true;
        var udm = UserDataManager.Instance;
        return udm != null && !string.IsNullOrEmpty(udm.GamestateId);
    }

    private void ShowMatchFoundedUi() {
        if (IsBoundToLiveGame()) return;
        MatchQueueingPanel.Instance?.HideImmediately();
        MatchFoundedPanel.Instance?.Show(MatchQueueDisplayText.GetQueueTitle(lastJoinedQueueType));
    }

    private void ShowQueueingPanelIfStillNeeded() {
        if (IsBoundToLiveGame() || IsMatchUiLocked()) return;
        MatchQueueingPanel.Instance?.Show(MatchQueueDisplayText.GetQueueTitle(lastJoinedQueueType));
    }

    private void HandleLeaveQueueDone(Response response) {
        ClearLocalMatchState();
    }

    /// <summary>
    /// 清理本地匹配 UI / 排队状态（不向服务端发 leave_queue）。
    /// 用于断线后服务端已静默移出队列、但客户端收不到 leave_queue_done 的场景
    /// （例如安卓后台断线后的 AutoReconnect）。
    /// </summary>
    public void ClearLocalMatchState() {
        isMatchFoundLocked = false;
        lastJoinedQueueType = null;
        CoroutineManager.Ensure();
        CoroutineManager.Instance.StopNamed(CoroutineKeys.MatchQueueingPanelDelay);
        MatchStateManager.Instance.StopQueueing();
        MatchQueueingPanel.Instance?.HideImmediately();
        MatchFoundedPanel.Instance?.StopCountdownAndHide();
    }

    private void HandleQueueStatus(Response response) {
        ApplyServerMatchState(response);
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

    /// <summary>
    /// 用服务端 my_queue 恢复「正在排队」。
    /// match_committed 是整局锁，不是进桌倒计时；匹配成功面板只由 match/match_found 打开。
    /// my_queue 为空时不清理本地（避免 join 与 poll 竞态）。
    /// </summary>
    private void ApplyServerMatchState(Response response) {
        if (IsBoundToLiveGame() || IsMatchUiLocked()) return;
        if (string.IsNullOrEmpty(response.my_queue)) return;
        lastJoinedQueueType = response.my_queue;
        MatchStateManager.Instance.EnsureQueueing(MatchQueueDisplayText.GetQueueTitle(response.my_queue));
        if (MatchPanel.Instance != null
            && MatchPanel.Instance.isActiveAndEnabled
            && MatchQueueingPanel.Instance != null
            && !MatchQueueingPanel.Instance.gameObject.activeSelf) {
            MatchQueueingPanel.Instance.RestoreIfQueueing();
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
        if (UserDataManager.Instance.IsTourist) {
            NotificationManager.Instance.ShowTip("匹配", false, "游客无法进行排位匹配，请先注册账号");
            return;
        }
        if (isMatchFoundLocked) {
            NotificationManager.Instance.ShowTip("匹配", false, "已匹配到对局，正在进入游戏");
            return;
        }
        if (GameSessionGuard.HasExclusiveSession) {
            NotificationManager.Instance.ShowTip("匹配", false, "对局进行中，无法进入匹配");
            return;
        }
        if (LobbyStateGuard.BlockIfInRoomForMatch()) {
            return;
        }
        GameRecordManager.AbandonDelayedSpectatorSessionOnServer();
        if (MatchStateManager.Instance.IsQueueing) {
            NotificationManager.Instance.ShowTip("匹配", false, "您已在匹配队列中");
            return;
        }
        isMatchFoundLocked = false;
        lastJoinedQueueType = queueType;
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
