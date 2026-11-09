using System.Collections.Generic;
using Salasasa.Home;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PartyIncomingPanel : MonoBehaviour {
    public BakedModalFade fade;
    public TMP_Text message, countdown;
    public Button accept, decline, close;
    readonly List<LobbyFriendInvite> pending = new List<LobbyFriendInvite>();
    LobbyFriendInvite current;
    bool responding;
    float requestedAt;
    void Awake() {
        accept.onClick.AddListener(() => Respond(true));
        decline.onClick.AddListener(() => Respond(false)); close.onClick.AddListener(() => Respond(false));
    }
    public void ShowInvite(LobbyFriendInvite invite) {
        if (invite.expires_at <= PartyNetworkManager.Now || pending.Exists(x => x.invite_id == invite.invite_id) || current?.invite_id == invite.invite_id) return;
        pending.Add(invite); if (current == null) Next();
    }
    public void Clear() { current = null; pending.Clear(); if (fade) fade.Hide(); }
    public void CloseInvite(string id) { pending.RemoveAll(x => x.invite_id == id); if (current?.invite_id == id) { current = null; Next(); } }
    void Next() {
        pending.RemoveAll(x => x.expires_at <= PartyNetworkManager.Now);
        if (pending.Count == 0) { current = null; fade.Hide(); return; }
        current = pending[0]; pending.RemoveAt(0); responding = false;
        message.text = current.from_username + "\n" + PlatformText.Get(current.kind == "room" ? "邀请你加入房间" : "邀请你组队");
        if (current.kind == "room") message.text += " " + current.room_id;
        accept.interactable = decline.interactable = close.interactable = true; fade.Show();
    }
    void Respond(bool yes) {
        if (current == null || responding) return;
        responding = true; requestedAt = Time.unscaledTime;
        accept.interactable = decline.interactable = close.interactable = false;
        PartyNetworkManager.Instance.Respond(current.invite_id, yes);
    }
    public void CompleteResponse(bool success) {
        if (!responding) return;
        responding = false;
        if (success) { current = null; Next(); }
        else accept.interactable = decline.interactable = close.interactable = true;
    }
    void Update() {
        if (current == null) return;
        if (UserDataManager.Instance && !string.IsNullOrEmpty(UserDataManager.Instance.GamestateId)
            || MatchNetworkManager.Instance && MatchNetworkManager.Instance.IsMatchCommitted) { Clear(); return; }
        if (responding && Time.unscaledTime - requestedAt > 15) CompleteResponse(false);
        int seconds = Mathf.Max(0, (int)(current.expires_at - PartyNetworkManager.Now));
        countdown.text = seconds + "s";
        if (seconds == 0) { current = null; Next(); }
    }
}
