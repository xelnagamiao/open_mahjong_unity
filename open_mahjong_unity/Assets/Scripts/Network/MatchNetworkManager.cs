using System.Collections;
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
        if (!response.success) {
            NotificationManager.Instance.ShowTip("匹配", false, response.message);
            return;
        }
        // 凑满 4 人的最后一位会几乎同时收到 match_found 与 join_queue_done，且 NetworkManager
        // 每帧只处理一条消息。若此处立刻弹出排队面板，可能在下一帧 match_found 生效前盖住成功面板，
        // 或在 match_found 已处理后因 StartQueueing 重置 IsMatchFound 导致状态错乱。
        if (IsMatchUiLocked()) {
            ShowMatchFoundedUi();
            return;
        }
        StartCoroutine(ShowQueueingPanelNextFrameIfStillNeeded());
    }

    private bool IsMatchUiLocked() {
        return isMatchFoundLocked
            || (MatchStateManager.Instance != null && MatchStateManager.Instance.IsMatchFound);
    }

    private void ShowMatchFoundedUi() {
        MatchQueueingPanel.Instance?.HideImmediately();
        MatchFoundedPanel.Instance?.Show(MatchQueueDisplayText.GetQueueTitle(lastJoinedQueueType));
    }

    /// <summary>
    /// 延迟一帧再决定是否展示排队面板，给同批到达的 match_found 留出处理机会。
    /// </summary>
    private IEnumerator ShowQueueingPanelNextFrameIfStillNeeded() {
        yield return null;
        if (IsMatchUiLocked()) {
            ShowMatchFoundedUi();
            yield break;
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
        if (response.queue_status != null) {
            MatchPanel.Instance?.UpdateQueueStatus(response.queue_status);
        }
    }

    private void HandleMatchFound(Response response) {
        isMatchFoundLocked = true;
        ShowMatchFoundedUi();
    }

    /// <summary>
    /// 释放匹配承诺锁。进入对局后该锁的使命即完成（后续由对局会话守卫接管），
    /// 必须在此时释放，否则对局结束后无法再次匹配（需退出重进才能恢复）。
    /// </summary>
    public void ResetMatchLock() {
        isMatchFoundLocked = false;
    }

    // 发送加入匹配队列请求
    public async void SendJoinQueue(string queueType) {
        if (UserDataManager.Instance != null && UserDataManager.Instance.IsTourist) {
            NotificationManager.Instance?.ShowTip("匹配", false, "游客无法进行排位匹配，请先注册账号");
            return;
        }
        // 已匹配成功正在进入对局时，禁止再次匹配（与服务端承诺锁一致，避免被拉入第二桌）
        if (isMatchFoundLocked) {
            NotificationManager.Instance?.ShowTip("匹配", false, "已匹配到对局，正在进入游戏");
            return;
        }
        // 仍处于进行中的对局 / 观战会话时，禁止匹配（防止当前一局未结束就开始下一局）
        if (GameSessionGuard.HasExclusiveSession) {
            NotificationManager.Instance?.ShowTip("匹配", false, "当前对局尚未结束，无法匹配");
            return;
        }
        // 处于房间中（自定义房等）时禁止匹配，需先退出房间
        if (UserDataManager.Instance != null && UserDataManager.Instance.RoomId != UserDataManager.ROOM_ID_NONE) {
            NotificationManager.Instance?.ShowTip("匹配", false, "请先退出当前房间再进行排位匹配");
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
