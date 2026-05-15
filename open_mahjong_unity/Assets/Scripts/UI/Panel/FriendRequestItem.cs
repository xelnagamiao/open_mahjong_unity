using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 好友申请条目：显示申请者基础信息，并提供接受 / 拒绝。
/// </summary>
public class FriendRequestItem : MonoBehaviour {
    [Header("展示")]
    [SerializeField] private Image avatar;
    [SerializeField] private TMP_Text uidText;
    [SerializeField] private TMP_Text usernameText;

    [Header("操作按钮")]
    [SerializeField] private Button acceptButton;
    [SerializeField] private Button declineButton;

    private FriendRequestInfo _info;
    private bool _listenersBound;

    public void Initialize(FriendRequestInfo info) {
        BindButtonsOnce();
        EnsureAvatarClickable();
        Refresh(info);
    }

    public void Refresh(FriendRequestInfo info) {
        _info = info;
        if (uidText != null) uidText.text = $"UID: {info.user_id}";
        if (usernameText != null) usernameText.text = info.username ?? "";
        LoadAvatar(info.profile_image_id);
        UpdateAvatarClickTarget(info.user_id);
    }

    private void BindButtonsOnce() {
        if (_listenersBound) return;
        if (acceptButton != null) acceptButton.onClick.AddListener(OnAccept);
        if (declineButton != null) declineButton.onClick.AddListener(OnDecline);
        _listenersBound = true;
    }

    private void EnsureAvatarClickable() {
        if (avatar == null) return;
        if (avatar.gameObject.GetComponent<ProfileOnClick>() == null) {
            avatar.gameObject.AddComponent<ProfileOnClick>();
        }
        avatar.raycastTarget = true;
    }

    private void UpdateAvatarClickTarget(int uid) {
        if (avatar == null) return;
        var click = avatar.gameObject.GetComponent<ProfileOnClick>();
        if (click != null) click.user_id = uid;
    }

    private void LoadAvatar(int profileImageId) {
        if (avatar == null) return;
        Sprite sprite = Resources.Load<Sprite>($"image/Profiles/{profileImageId}");
        if (sprite != null) avatar.sprite = sprite;
    }

    private void OnAccept() {
        if (_info == null) return;
        FriendNetworkManager.Instance?.RespondFriendRequest(_info.user_id, true);
    }

    private void OnDecline() {
        if (_info == null) return;
        FriendNetworkManager.Instance?.RespondFriendRequest(_info.user_id, false);
    }
}
