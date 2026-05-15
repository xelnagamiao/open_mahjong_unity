using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 关注者列表条目：只提供普通延时观战和取消关注，不提供实时观战。
/// </summary>
public class FollowingItem : MonoBehaviour {
    [Header("展示")]
    [SerializeField] private Image avatar;
    [SerializeField] private TMP_Text uidText;
    [SerializeField] private TMP_Text usernameText;
    [SerializeField] private TMP_Text stateText;

    [Header("状态颜色")]
    [SerializeField] private Color offlineColor = Color.white;
    [SerializeField] private Color onlineColor = new Color(0.30f, 0.85f, 0.30f, 1f);
    [SerializeField] private Color inGameColor = new Color(0.35f, 0.65f, 1f, 1f);

    [Header("操作按钮")]
    [SerializeField] private Button spectateButton;
    [SerializeField] private Button unfollowButton;

    private FriendInfo _info;
    private bool _listenersBound;

    public void Initialize(FriendInfo info) {
        if (uidText != null) uidText.text = $"UID: {info.user_id}";
        BindButtonsOnce();
        EnsureAvatarClickable();
        Refresh(info);
    }

    public void Refresh(FriendInfo info) {
        _info = info;
        if (usernameText != null) usernameText.text = info.username ?? "";
        if (stateText != null) {
            stateText.text = StateDisplay(info.state);
            stateText.color = StateColor(info.state);
        }
        LoadAvatar(info.profile_image_id);
        UpdateAvatarClickTarget(info.user_id);

        bool inGame = IsInGameState(info.state);
        bool hasGamestateId = !string.IsNullOrEmpty(info.gamestate_id);
        if (spectateButton != null) {
            spectateButton.gameObject.SetActive(true);
            spectateButton.interactable = inGame && hasGamestateId;
        }
        if (unfollowButton != null) {
            unfollowButton.gameObject.SetActive(true);
            unfollowButton.interactable = true;
        }
    }

    private void BindButtonsOnce() {
        if (_listenersBound) return;
        if (spectateButton != null) spectateButton.onClick.AddListener(OnClickSpectate);
        if (unfollowButton != null) unfollowButton.onClick.AddListener(OnClickUnfollow);
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

    private static bool IsInGameState(string state) {
        return !string.IsNullOrEmpty(state) && state.Trim().Equals("in_game", StringComparison.OrdinalIgnoreCase);
    }

    private static string StateDisplay(string state) {
        if (IsInGameState(state)) return "对局中";
        if (string.IsNullOrEmpty(state)) return "";
        string s = state.Trim().ToLowerInvariant();
        return s switch {
            "offline" => "离线",
            "online" => "在线",
            _ => state,
        };
    }

    private Color StateColor(string state) {
        if (IsInGameState(state)) return inGameColor;
        if (string.IsNullOrEmpty(state)) return offlineColor;
        string s = state.Trim().ToLowerInvariant();
        return s switch {
            "online" => onlineColor,
            "offline" => offlineColor,
            _ => offlineColor,
        };
    }

    private void OnClickSpectate() {
        if (_info == null || string.IsNullOrEmpty(_info.gamestate_id)) {
            NotificationManager.Instance?.ShowTip("观战", false, "对方当前不允许普通观战");
            return;
        }
        GameStateNetworkManager.Instance?.AddSpectator(_info.gamestate_id);
    }

    private void OnClickUnfollow() {
        if (_info == null) return;
        FriendNetworkManager.Instance?.RemoveFollowing(_info.user_id);
    }
}
