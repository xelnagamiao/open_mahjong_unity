using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 双向好友列表条目脚本，对应 FriendPanel 的好友预制体。
/// 观战按钮受普通观战权限控制，实时观战按钮只受是否在对局中控制。
/// 状态文本颜色：离线白 / 在线绿 / 对局中蓝（可在编辑器修改）。
/// 头像点击：复用 ProfileOnClick 行为，发起 Guobiao 战绩请求。
/// </summary>
public class FriendItem : MonoBehaviour {
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
    [SerializeField] private Button spectateButton;          // 普通延迟观战
    [SerializeField] private Button realtimeSpectateButton;  // 实时观战
    [SerializeField] private Button deleteFriendButton;      // 删除好友

    private FriendInfo _info;
    private bool _listenersBound;

    /// <summary>
    /// 首次创建条目时调用：绑定按钮事件 + 头像点击组件 + 写入展示。
    /// 后续轮询请走 <see cref="Refresh"/>，避免反复 RemoveAllListeners / Add 造成的事件冲突。
    /// </summary>
    public void Initialize(FriendInfo info) {
        _info = info;
        if (uidText != null) uidText.text = $"UID: {info.user_id}";
        BindButtonsOnce();
        EnsureAvatarClickable();
        Refresh(info);
    }

    /// <summary>
    /// 仅刷新与 state 相关的可视/可交互内容（用户名/头像/状态文本/按钮可点）。
    /// 由 FriendPanel 在收到轮询返回的最新 friend_list 时调用。
    /// </summary>
    public void Refresh(FriendInfo info) {
        if (info == null) return;
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
        // 三枚按钮始终保留显示：玩家不在对局中只把观战 / 实时观战置灰（interactable=false），
        // 避免按钮在 SetActive 切换时引起 LayoutGroup 重排，导致玩家进入对局后按钮"看不见"激活。
        if (spectateButton != null) {
            spectateButton.gameObject.SetActive(true);
            spectateButton.interactable = inGame && hasGamestateId;
        }
        if (realtimeSpectateButton != null) {
            realtimeSpectateButton.gameObject.SetActive(true);
            realtimeSpectateButton.interactable = inGame;
        }
        if (deleteFriendButton != null) {
            deleteFriendButton.gameObject.SetActive(true);
            deleteFriendButton.interactable = true;
        }
    }

    private static bool IsInGameState(string state) {
        if (string.IsNullOrEmpty(state)) return false;
        return state.Trim().Equals("in_game", StringComparison.OrdinalIgnoreCase);
    }

    private void BindButtonsOnce() {
        if (_listenersBound) return;
        if (spectateButton != null) spectateButton.onClick.AddListener(OnClickSpectate);
        if (realtimeSpectateButton != null) realtimeSpectateButton.onClick.AddListener(OnClickRealtime);
        if (deleteFriendButton != null) deleteFriendButton.onClick.AddListener(OnClickDeleteFriend);
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
            "offline" => offlineColor,
            "online" => onlineColor,
            _ => offlineColor,
        };
    }

    private void LoadAvatar(int profileImageId) {
        if (avatar == null) return;
        Sprite sprite = Resources.Load<Sprite>($"image/Profiles/{profileImageId}");
        if (sprite != null) avatar.sprite = sprite;
    }

    private void OnClickSpectate() {
        if (_info == null || string.IsNullOrEmpty(_info.gamestate_id)) {
            NotificationManager.Instance?.ShowTip("观战", false, "对方当前不在对局中");
            return;
        }
        GameStateNetworkManager.Instance?.AddSpectator(_info.gamestate_id);
    }

    private void OnClickRealtime() {
        if (_info == null) return;
        if (!IsInGameState(_info.state)) {
            NotificationManager.Instance?.ShowTip("实时观战", false, "对方当前不在对局中");
            return;
        }
        if (LobbyStateGuard.BlockIfInMatchQueueForSpectator()) return;
        if (GameSessionGuard.BlockIfExclusiveSession("进入实时观战")) return;
        if (RealtimeRequestWaitPanel.Instance != null) {
            RealtimeRequestWaitPanel.Instance.ShowWaiting(_info.user_id, _info.username);
        }
        FriendNetworkManager.Instance?.RequestRealtime(_info.user_id);
    }

    private void OnClickDeleteFriend() {
        if (_info == null) return;
        FriendPanel.Instance?.ShowDeleteFriendConfirm(_info.user_id, _info.username);
    }
}
