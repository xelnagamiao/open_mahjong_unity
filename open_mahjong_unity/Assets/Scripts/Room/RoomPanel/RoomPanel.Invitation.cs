using UnityEngine;

public partial class RoomPanel {
    [SerializeField] private UnityEngine.UI.Button inviteButton;
    [SerializeField] private RoomInvitePanel invitePanel;

    void InitializeInvitation() {
        if (inviteButton) inviteButton.onClick.AddListener(ShowInvitation);
    }
    void ShowInvitation() {
        if (lastRoomInfo == null || !invitePanel) return;
        if (!RoomInvitation.TryIdentity(lastRoomInfo.room_id, lastRoomInfo.instance_id, out _)) {
            NotificationManager.Instance.ShowTip("room/invite", false, "房间信息尚未同步，请稍后重试。"); return;
        }
        invitePanel.ShowShare(lastRoomInfo);
    }
    void RefreshInvitation() {
        if (inviteButton) inviteButton.interactable = lastRoomInfo != null
            && RoomInvitation.TryIdentity(lastRoomInfo.room_id, lastRoomInfo.instance_id, out _);
        if (invitePanel) invitePanel.UpdateSharedRoom(lastRoomInfo);
    }
}
