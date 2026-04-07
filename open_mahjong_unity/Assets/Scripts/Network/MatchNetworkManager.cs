using UnityEngine;
using Newtonsoft.Json;

public class MatchNetworkManager : MonoBehaviour {
    public static MatchNetworkManager Instance { get; private set; }

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
            MatchingWidget.Instance?.Show(response.message);
        } else {
            NotificationManager.Instance.ShowTip("匹配", false, response.message);
        }
    }

    private void HandleLeaveQueueDone(Response response) {
        MatchingWidget.Instance?.Hide();
    }

    private void HandleQueueStatus(Response response) {
        if (response.queue_status != null) {
            MatchPanel.Instance?.UpdateQueueStatus(response.queue_status);
        }
    }

    private void HandleMatchFound(Response response) {
        MatchingWidget.Instance?.Hide();
        MatchPanel.Instance?.ShowMatchFoundAnimation(response.message);
    }

    // 发送加入匹配队列请求
    public void SendJoinQueue(string queueType) {
        var msg = new { type = "match/join_queue", queue_type = queueType };
        NetworkManager.Instance.GetWebSocket()?.SendText(JsonConvert.SerializeObject(msg));
    }

    // 发送离开匹配队列请求
    public void SendLeaveQueue() {
        var msg = new { type = "match/leave_queue" };
        NetworkManager.Instance.GetWebSocket()?.SendText(JsonConvert.SerializeObject(msg));
    }

    // 发送获取队列状态请求
    public void SendGetQueueStatus() {
        var msg = new { type = "match/get_queue_status" };
        NetworkManager.Instance.GetWebSocket()?.SendText(JsonConvert.SerializeObject(msg));
    }
}
