using UnityEngine;
using Newtonsoft.Json;
using NativeWebSocket;
using System;

/// <summary>
/// 好友 / 关注 / 实时观战 网络管理器。
/// 与 MatchNetworkManager 同模式：单例 + Handle* 方法 + 主动发送方法。
/// 服务器消息按 type 转发到对应 UI（FriendPanel / RealtimeRequestWaitPanel / RealtimeRequestIncomingPanel / RealtimeSpectatorIndicator）。
/// </summary>
public class FriendNetworkManager : MonoBehaviour {
    public static FriendNetworkManager Instance { get; private set; }

    public event Action<Response> OnRealtimeRequestResult;        // A 发起后立即收到的回包
    public event Action<Response> OnRealtimeRequestIncoming;      // B 收到的请求
    public event Action<Response> OnRealtimeRequestTimeout;       // A 收到的超时
    public event Action<Response> OnRealtimeRequestDeclined;      // A 收到的拒绝
    public event Action<Response> OnRealtimeRequestRevoked;       // B 收到的撤回
    public event Action<Response> OnRealtimeStarted;              // A 收到的开始观战
    public event Action<Response> OnRealtimeKicked;               // A 被踢出
    public event Action<Response> OnRealtimeEnded;                // 对局结束
    public event Action<Response> OnRealtimeSpectatorsChanged;    // B 收到的观战者列表更新
    public event Action<Response> OnListRealtimeSpectatorsResp;   // B 主动获取观战者列表

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void HandleFriendMessage(Response response) {
        if (response == null || string.IsNullOrEmpty(response.type)) return;
        SyncFriendListCache(response);
        switch (response.type) {
            case "friend/list_following":
                FriendPanel.Instance?.OnFollowingListResponse(response);
                break;
            case "friend/add_following":
                FriendPanel.Instance?.OnAddFollowingResponse(response);
                break;
            case "friend/remove_following":
                FriendPanel.Instance?.OnRemoveFollowingResponse(response);
                break;
            case "friend/list_friends":
                FriendPanel.Instance?.OnFriendListResponse(response);
                break;
            case "friend/request_friend":
                FriendPanel.Instance?.OnRequestFriendResponse(response);
                break;
            case "friend/delete_friend":
                FriendPanel.Instance?.OnDeleteFriendResponse(response);
                break;
            case "friend/list_friend_requests":
                FriendPanel.Instance?.OnFriendRequestListResponse(response);
                break;
            case "friend/respond_friend_request":
                FriendPanel.Instance?.OnRespondFriendRequestResponse(response);
                break;
            case "friend/realtime_request_result":
                OnRealtimeRequestResult?.Invoke(response);
                break;
            case "friend/realtime_request_incoming":
                OnRealtimeRequestIncoming?.Invoke(response);
                RealtimeRequestIncomingPanel.Instance?.HandleIncoming(response);
                break;
            case "friend/realtime_request_timeout":
                OnRealtimeRequestTimeout?.Invoke(response);
                break;
            case "friend/realtime_request_declined":
                OnRealtimeRequestDeclined?.Invoke(response);
                break;
            case "friend/realtime_request_revoked":
                OnRealtimeRequestRevoked?.Invoke(response);
                RealtimeRequestIncomingPanel.Instance?.HandleRevoked(response);
                break;
            case "friend/realtime_request_cancel_result":
            case "friend/realtime_request_respond_result":
                // 这些是发起方/响应方收到的回包，UI 通常不需要额外处理（除非弹 tip）
                if (!response.success && !string.IsNullOrEmpty(response.message)) {
                    NotificationManager.Instance?.ShowTip("实时观战", false, response.message);
                }
                break;
            case "friend/realtime_started":
                OnRealtimeStarted?.Invoke(response);
                break;
            case "friend/realtime_kicked":
                OnRealtimeKicked?.Invoke(response);
                break;
            case "friend/realtime_ended":
                OnRealtimeEnded?.Invoke(response);
                break;
            case "friend/realtime_exit_result":
                if (!string.IsNullOrEmpty(response.message)) {
                    NotificationManager.Instance?.ShowTip("实时观战", response.success, response.message);
                }
                break;
            case "friend/realtime_kick_result":
                if (!response.success && !string.IsNullOrEmpty(response.message)) {
                    NotificationManager.Instance?.ShowTip("实时观战", false, response.message);
                }
                break;
            case "friend/realtime_spectators_changed":
                OnRealtimeSpectatorsChanged?.Invoke(response);
                RealtimeSpectatorIndicator.Instance?.HandleSpectatorsChanged(response);
                break;
            case "friend/list_realtime_spectators":
                OnListRealtimeSpectatorsResp?.Invoke(response);
                RealtimeSpectatorIndicator.Instance?.HandleSpectatorsChanged(response);
                break;
        }
    }

    private static void SyncFriendListCache(Response response) {
        if (response?.friend_list != null) {
            FriendRelationCache.ReplaceFromList(response.friend_list);
        }
    }

    // ----------------- 发送 -----------------

    private static WebSocket _GetWs() {
        if (NetworkManager.Instance == null) return null;
        var ws = NetworkManager.Instance.GetWebSocket();
        return (ws != null && ws.State == WebSocketState.Open) ? ws : null;
    }

    private static async void _Send(object msg) {
        var ws = _GetWs();
        if (ws == null) return;
        try {
            await ws.SendText(JsonConvert.SerializeObject(msg));
        } catch (Exception e) {
            Debug.LogError($"[FriendNetworkManager] 发送失败: {e.Message}");
        }
    }

    public void ListAllFriendPanels() {
        ListFollowing();
        ListFriends();
        ListFriendRequests();
    }

    public void ListFollowing() {
        _Send(new { type = "friend/list_following" });
    }

    public void AddFollowing(int friendUserId) {
        _Send(new { type = "friend/add_following", friend_user_id = friendUserId });
    }

    public void RemoveFollowing(int friendUserId) {
        _Send(new { type = "friend/remove_following", friend_user_id = friendUserId });
    }

    public void ListFriends() {
        _Send(new { type = "friend/list_friends" });
    }

    public void RequestFriend(int targetUserId) {
        _Send(new { type = "friend/request_friend", target_user_id = targetUserId });
    }

    public void DeleteFriend(int friendUserId) {
        _Send(new { type = "friend/delete_friend", friend_user_id = friendUserId });
    }

    public void ListFriendRequests() {
        _Send(new { type = "friend/list_friend_requests" });
    }

    public void RespondFriendRequest(int fromUserId, bool accept) {
        _Send(new { type = "friend/respond_friend_request", from_user_id = fromUserId, accept = accept });
    }

    public void RequestRealtime(int targetUserId) {
        _Send(new { type = "friend/request_realtime", target_user_id = targetUserId });
    }

    public void RespondRealtime(string requestId, bool accept) {
        _Send(new { type = "friend/respond_realtime", request_id = requestId, accept = accept });
    }

    public void CancelRealtime(string requestId) {
        _Send(new { type = "friend/cancel_realtime", request_id = requestId });
    }

    public void ExitRealtime() {
        _Send(new { type = "friend/exit_realtime" });
    }

    public void KickRealtime(int spectatorUserId) {
        _Send(new { type = "friend/kick_realtime", spectator_user_id = spectatorUserId });
    }

    public void ListRealtimeSpectators() {
        _Send(new { type = "friend/list_realtime_spectators" });
    }
}
