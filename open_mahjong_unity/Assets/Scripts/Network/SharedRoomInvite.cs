using System;
using System.Collections;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>Retains a room link across login and enters through the normal password flow.</summary>
public sealed class SharedRoomInvite : MonoBehaviour {
    public static SharedRoomInvite Instance { get; private set; }
    RoomInvitation.Target pending;
    string requestId;
    int requestOwner;
    bool waitingForLoginSync, joining;
    Coroutine timeout;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() {
        if (Instance) return;
        var host = new GameObject(nameof(SharedRoomInvite));
        DontDestroyOnLoad(host);
        Instance = host.AddComponent<SharedRoomInvite>();
    }
    void Awake() {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Application.deepLinkActivated += Open;
    }
    IEnumerator Start() {
        yield return null;
        if (RoomInvitation.HasInviteParameters(Application.absoluteURL)) { Open(Application.absoluteURL); yield break; }
        var args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length; i++)
            if (args[i] == "--room-url" && i + 1 < args.Length) { Open(args[i + 1]); yield break; }
    }
    void OnDestroy() {
        Application.deepLinkActivated -= Open;
        if (Instance == this) Instance = null;
    }
    bool LoggedIn => UserDataManager.Instance && UserDataManager.Instance.UserId > 0
        && NetworkManager.Instance && NetworkManager.Instance.IsWebSocketOpen
        && WindowsManager.Instance && WindowsManager.Instance.GetCurrentWindow() != "login";

    public void Open(string url) {
        if (!RoomInvitation.HasInviteParameters(url)) return;
        if (!RoomInvitation.TryParse(url, out var target)) { Tip(RoomInvitation.InvalidMessage); return; }
        if (joining) {
            if (pending.RoomId == target.RoomId && pending.InstanceId == target.InstanceId && LoggedIn) Send(true, "");
            else Tip("上一次加入结果尚未确认，请先重试原邀请。");
            return;
        }
        Cancel(); pending = target;
        if (LoggedIn && !AutoReconnect.IsActive) Resolve();
        else waitingForLoginSync = true;
    }
    public static void OnLoginSucceeded() {
        if (!Instance || Instance.pending == null) return;
        Instance.StopTimeout(); Instance.requestId = null; Instance.joining = false;
        Instance.waitingForLoginSync = true;
    }
    public static void OnLobbySynchronized() {
        if (!Instance || !Instance.waitingForLoginSync || Instance.pending == null) return;
        Instance.waitingForLoginSync = false;
        Instance.Resolve();
    }
    void Resolve() {
        if (pending == null) return;
        if (!LoggedIn) { waitingForLoginSync = true; return; }
        if (RoomNetworkManager.Instance.IsCurrentInvitation(pending.RoomId, pending.InstanceId)) {
            if (string.IsNullOrEmpty(UserDataManager.Instance.GamestateId)) WindowsManager.Instance.SwitchWindow("room");
            Cancel(); return;
        }
        string blocked = EntryBlockedReason();
        if (blocked != null) { Fail(blocked); return; }
        Send(false, "");
    }
    string EntryBlockedReason() {
        if (!LoggedIn) return "请先登录";
        if (AutoReconnect.IsActive) return "正在恢复连接，请稍后重试。";
        if (!string.IsNullOrEmpty(UserDataManager.Instance.GamestateId)) return "您正在对局中，无法加入房间";
        if (LobbyStateGuard.IsInRoom) return "请先退出当前房间";
        if (LobbyStateGuard.IsInMatchQueue) return "正在匹配队列中，请先取消匹配再加入房间。";
        return null;
    }
    void Enter(bool needsPassword) {
        var target = pending;
        PasswordJoinPanel.TryJoin(target.RoomId, needsPassword,
            password => { if (pending == target) Join(password); },
            () => { if (pending == target) Cancel(); });
    }
    void Join(string password) {
        if (pending == null) return;
        string blocked = EntryBlockedReason();
        if (blocked != null) { Fail(blocked); return; }
        RoomNetworkManager.Instance.BeginInvitationEntry(pending.RoomId, pending.InstanceId);
        joining = true;
        Send(true, password ?? "");
    }
    async void Send(bool join, string password) {
        StopTimeout();
        string id = requestId = Guid.NewGuid().ToString("N");
        requestOwner = UserDataManager.Instance.UserId;
        timeout = StartCoroutine(WaitForResponse(id, join));
        try {
            await NetworkManager.Instance.GetWebSocket().SendText(JsonConvert.SerializeObject(new {
                type = join ? "room/join_invite" : "room/resolve_invite",
                duplicate_lobby_version = 1,
                room_id = pending.RoomId, room_instance_id = pending.InstanceId,
                invite_request_id = id, password = join ? password : "",
            }));
        } catch (Exception) {
            if (requestId != id) return;
            StopTimeout(); Tip("连接中断，请检查网络后重试原邀请。");
        }
    }
    IEnumerator WaitForResponse(string id, bool join) {
        yield return new WaitForSecondsRealtime(15);
        timeout = null;
        if (requestId == id) Tip(join ? "加入结果尚未确认，请重试原邀请。" : "获取房间信息超时，请重试。");
    }
    public void Receive(Response response) {
        if (pending == null || response.invite_request_id != requestId || !UserDataManager.Instance
            || requestOwner != UserDataManager.Instance.UserId) return;
        StopTimeout(); requestId = null;
        if (response.type == "room/join_invite") {
            joining = false;
            if (!response.success) {
                RoomNetworkManager.Instance.CancelPendingRoomEntry();
                if (response.message == "密码错误") { Tip(response.message); Enter(true); }
                else Fail(response.message);
                return;
            }
            if (response.room_id != pending.RoomId || response.room_instance_id != pending.InstanceId
                || response.room_info == null || response.room_info.room_id != pending.RoomId
                || response.room_info.instance_id != pending.InstanceId) {
                RoomNetworkManager.Instance.CancelPendingRoomEntry(); Fail(RoomInvitation.InvalidMessage); return;
            }
            RoomNetworkManager.Instance.CompleteInvitationEntry(response);
            Cancel();
            NotificationManager.Instance.ShowTip("join_room", true, "加入房间成功");
            return;
        }
        if (!response.success || response.invite_info == null) { Fail(response.message); return; }
        var info = response.invite_info;
        if (info.room_id != pending.RoomId || info.instance_id != pending.InstanceId) { Fail(RoomInvitation.InvalidMessage); return; }
        Enter(info.has_password);
    }
    void Fail(string message) { Cancel(); Tip(message); }
    static void Tip(string message) => NotificationManager.Instance?.ShowTip("join_room", false, message);
    void Cancel() {
        if (joining) return;
        StopTimeout(); pending = null; requestId = null; waitingForLoginSync = false;
    }
    void StopTimeout() { if (timeout != null) StopCoroutine(timeout); timeout = null; }
}
