using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>Replicated party state; the server owns membership, readiness and queue admission.</summary>
public sealed class PartyNetworkManager : MonoBehaviour {
    public const string Queue = "sichuan_doubles_elo_banzhuang";
    public static PartyNetworkManager Instance { get; private set; }
    public FriendInvitePanel friendPicker;
    public PartyIncomingPanel incomingPanel;
    public LobbyParty Party { get; private set; }
    public event Action Changed;
    readonly Dictionary<int, LobbyFriendInvite> outgoing = new Dictionary<int, LobbyFriendInvite>();
    public bool HasParty => Party != null && !string.IsNullOrEmpty(Party.party_id);
    public bool IsLeader => HasParty && Party.leader_user_id == UserDataManager.Instance.UserId;
    public bool IsQueued => HasParty && Party.state == "queueing";
    public bool IsCommitted => HasParty && Party.state == "in_game";
    public int MemberCount => HasParty ? Party.members.Length : 1;
    public bool HasPendingPartyInvite {
        get { foreach (var invite in outgoing.Values) if (invite.kind == "party" && invite.expires_at > Now) return true; return false; }
    }
    public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }
    public void ResetState() {
        Party = null; outgoing.Clear(); incomingPanel?.Clear();
        if (friendPicker) friendPicker.Hide(); Changed?.Invoke();
    }
    public void Handle(Response response) {
        if (response.party != null) {
            var next = response.party;
            if (Party == null || next.party_id != Party.party_id || next.revision >= Party.revision) Party = next;
        }
        if (response.match_revision.HasValue) MatchNetworkManager.Instance?.SynchronizePartyMatch(response);
        var invite = response.party_invite;
        if (invite != null) {
            if (response.type == "party/incoming") incomingPanel.ShowInvite(invite);
            else if (response.type == "party/invite_closed") {
                outgoing.Remove(invite.to_user_id); incomingPanel.CloseInvite(invite.invite_id);
                if (invite.status == "declined") Tip(response.message);
            } else if (response.type == "party/invite" && response.success) outgoing[invite.to_user_id] = invite;
            else if (invite.status == "revoked") outgoing.Remove(invite.to_user_id);
            else if (invite.status == "accepted" && invite.kind == "room") {
                string url = RoomInvitation.BuildUrl(ConfigManager.webUrl, invite.room_id, invite.room_instance_id);
                if (url != null) SharedRoomInvite.Instance.Open(url);
            }
        }
        if (response.type == "party/invite" || response.type == "party/revoke") friendPicker?.RequestFinished();
        if (response.type == "party/respond") incomingPanel.CompleteResponse(response.success);
        if (response.type == "party/notice" && response.success) Tip(response.message);
        if (!response.success) Tip(response.message);
        Changed?.Invoke();
    }
    public LobbyFriendInvite Outgoing(int uid, string kind, string roomId) {
        return outgoing.TryGetValue(uid, out var invite) && invite.expires_at > Now && invite.kind == kind
            && (kind != "room" || invite.room_id == roomId) ? invite : null;
    }
    public void ShowFriends() {
        if (IsQueued || IsCommitted) { Tip("请先取消匹配，或完成当前对局"); return; }
        if (HasParty && (!IsLeader || MemberCount >= Party.capacity)) { Tip("仅队长可以邀请，且队伍需要有空位"); return; }
        friendPicker.ShowParty();
    }
    public void RequestQueue() {
        if (IsQueued) { Send("cancel"); return; }
        if (!HasParty || MemberCount != 2) { Tip("进入2v2匹配需要邀请好友组队"); return; }
        if (IsCommitted) { Tip("已匹配到对局，请完成当前对局"); return; }
        Send("queue");
    }
    public void Ready(bool ready) => SendObject(new { type = "party/ready", ready });
    public void Invite(int uid, string kind, string roomId, string roomInstanceId) => SendObject(new { type = "party/invite", target_user_id = uid, kind, room_id = roomId, room_instance_id = roomInstanceId });
    public void Respond(string id, bool accept) => SendObject(new { type = "party/respond", invite_id = id, accept });
    public void Revoke(string id) => SendObject(new { type = "party/revoke", invite_id = id });
    public void MemberAction(string action, int uid) => SendObject(new { type = "party/" + action, target_user_id = uid });
    public void Send(string action) => SendObject(new { type = "party/" + action });
    async void SendObject(object request) {
        if (!NetworkManager.Instance || !NetworkManager.Instance.IsWebSocketOpen) { Tip("连接已断开，请稍后重试"); return; }
        try { await NetworkManager.Instance.GetWebSocket().SendText(JsonConvert.SerializeObject(request)); }
        catch (Exception e) { Tip("队伍请求发送失败"); Debug.LogWarning(e.Message); }
    }
    public static void Tip(string text) { if (!string.IsNullOrEmpty(text)) NotificationManager.Instance?.ShowTip("组队与邀请", false, text); }
}
