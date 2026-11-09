using Salasasa.Home;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class FriendInviteRow : MonoBehaviour {
    public Image avatar;
    public TMP_Text username, state, actionLabel;
    public Button action;
    public FriendInfo Info { get; private set; }
    FriendInvitePanel owner;
    public void Bind(FriendInvitePanel panel, FriendInfo info) {
        owner = panel; Info = info; username.text = info.username;
        avatar.sprite = ConfigManager.GetProfileSprite(info.profile_image_id);
        action.onClick.AddListener(() => owner.Invite(Info)); Refresh();
    }
    public void Refresh() {
        if (Info == null) return;
        var sent = owner.Sent(Info.user_id);
        string status = Info.state == "online" ? "在线" : Info.state == "in_game" ? "对局中" : "离线";
        state.text = PlatformText.Get(status);
        state.color = Info.state == "online" ? new Color(.55f, .8f, .65f) : new Color(.6f, .63f, .68f);
        actionLabel.text = PlatformText.Get(sent != null ? "撤销邀请" : owner.IsPending(Info.user_id) ? "发送中" : "邀请");
        action.interactable = sent != null || Info.state == "online" && !owner.IsPending(Info.user_id) && owner.CanInvite;
    }
}

