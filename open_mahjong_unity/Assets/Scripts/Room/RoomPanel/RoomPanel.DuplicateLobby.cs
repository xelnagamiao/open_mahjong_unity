using System.Linq;
using UnityEngine;

public partial class RoomPanel {
    [SerializeField] private SichuanDuplicateLobbyView duplicateLobbyView;

    private bool RefreshDuplicateLobby(RoomInfo info) {
        bool enabled = info.duplicate_lobby?.version == 1;
        TableText.Set(startButton.GetComponentInChildren<TMPro.TMP_Text>(true), enabled
            && info.duplicate_status == "interrupted" && string.IsNullOrEmpty(info.event_id) ? "重开未完成副" : "开始游戏");
        if (duplicateLobbyView) duplicateLobbyView.gameObject.SetActive(enabled);
        if (!enabled) return false;
        if (!duplicateLobbyView) throw new System.InvalidOperationException("八人复式房间布局尚未烘焙");
        foreach (var player in new[] { playerPanel1, playerPanel2, playerPanel3, playerPanel4 })
            player.gameObject.SetActive(false);
        if (hostNameText) hostNameText.gameObject.SetActive(false);
        HideDoublesTeams();
        HideBotHintTexts();
        if (botSpeedDropdown) botSpeedDropdown.transform.parent.gameObject.SetActive(false);

        duplicateLobbyView.Refresh(info);
        bool isHost = info.host_user_id == UserDataManager.Instance.UserId;
        selfReady = IsSeatReady(UserDataManager.Instance.UserId, info);
        startButton.gameObject.SetActive(isHost);
        startButton.interactable = isHost && SichuanDuplicateLobbyView.CanStart(info);
        if (readyButton) {
            readyButton.gameObject.SetActive(!isHost);
            readyButton.interactable = !isHost && info.duplicate_lobby.can_edit
                && (info.seat_list?.Contains(UserDataManager.Instance.UserId) ?? false);
        }
        if (readyButtonText) readyButtonText.text = selfReady ? "取消准备" : "准备";
        roomConfigContainer.SetRoomConfig(info);
        return true;
    }
}
