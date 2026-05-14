using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 好友/关注 面板：UID 输入框 + 添加按钮 + "0/10" 文本 + 滚动列表。
/// 服务器消息通过 FriendNetworkManager 事件订阅。
/// 进入面板：清空旧条目、注册 5s 轮询；退出面板：停止轮询。
/// 轮询返回的列表用于动态刷新条目状态文本与按钮可点状态，避免每次都销毁重建。
/// </summary>
public class FriendPanel : MonoBehaviour {
    public static FriendPanel Instance { get; private set; }

    [Header("添加好友")]
    [SerializeField] private TMP_InputField uidInput;
    [SerializeField] private Button addButton;
    [SerializeField] private TMP_Text countText;

    [Header("列表")]
    [SerializeField] private Transform contentTransform;
    [SerializeField] private GameObject friendItemPrefab;

    [Header("轮询")]
    [SerializeField] private float pollingIntervalSeconds = 5f;

    private readonly Dictionary<int, FriendItem> _itemsByUid = new Dictionary<int, FriendItem>();
    private int _friendMax = 10;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (addButton != null) addButton.onClick.AddListener(OnClickAdd);
    }

    private void OnEnable() {
        ClearItems();
        if (countText != null) countText.text = $"0/{_friendMax}";
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.OnFriendListResponse += HandleListResponse;
            FriendNetworkManager.Instance.OnAddFriendResponse += HandleAddResponse;
            FriendNetworkManager.Instance.OnRemoveFriendResponse += HandleRemoveResponse;
        }
        if (NetworkPollingManager.Instance != null) {
            NetworkPollingManager.Instance.StartFriendListPolling(pollingIntervalSeconds);
        }
    }

    private void OnDisable() {
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.OnFriendListResponse -= HandleListResponse;
            FriendNetworkManager.Instance.OnAddFriendResponse -= HandleAddResponse;
            FriendNetworkManager.Instance.OnRemoveFriendResponse -= HandleRemoveResponse;
        }
        if (NetworkPollingManager.Instance != null) {
            NetworkPollingManager.Instance.StopFriendListPolling();
        }
    }

    private void OnClickAdd() {
        if (uidInput == null || string.IsNullOrWhiteSpace(uidInput.text)) {
            NotificationManager.Instance?.ShowTip("关注", false, "请输入要关注的玩家 UID");
            return;
        }
        if (!int.TryParse(uidInput.text.Trim(), out int uid)) {
            NotificationManager.Instance?.ShowTip("关注", false, "UID 必须是数字");
            return;
        }
        FriendNetworkManager.Instance?.AddFriend(uid);
    }

    private void HandleListResponse(Response response) {
        ApplyList(response);
    }

    private void HandleAddResponse(Response response) {
        if (!string.IsNullOrEmpty(response.message)) {
            NotificationManager.Instance?.ShowTip("关注", response.success, response.message);
        }
        if (response.success && uidInput != null) uidInput.text = string.Empty;
        ApplyList(response);
    }

    private void HandleRemoveResponse(Response response) {
        if (!string.IsNullOrEmpty(response.message)) {
            NotificationManager.Instance?.ShowTip("关注", response.success, response.message);
        }
        ApplyList(response);
    }

    /// <summary>
    /// 把后端最新的好友列表 diff 到 UI：
    /// - 新增 uid → Instantiate 新 FriendItem
    /// - 已存在 uid → FriendItem.Refresh(info) 原地更新状态文本与按钮可点
    /// - 缺失 uid → 销毁
    /// </summary>
    private void ApplyList(Response response) {
        if (response.friend_max.HasValue) _friendMax = response.friend_max.Value;
        var list = response.friend_list;
        int count = list?.Length ?? 0;
        if (countText != null) countText.text = $"{count}/{_friendMax}";
        if (contentTransform == null || friendItemPrefab == null) return;

        var seen = new HashSet<int>();
        if (list != null) {
            foreach (var info in list) {
                if (info == null) continue;
                seen.Add(info.user_id);
                if (_itemsByUid.TryGetValue(info.user_id, out var existing) && existing != null) {
                    existing.Refresh(info);
                } else {
                    var go = Instantiate(friendItemPrefab, contentTransform);
                    var item = go.GetComponent<FriendItem>();
                    if (item != null) {
                        item.Initialize(info);
                        _itemsByUid[info.user_id] = item;
                    } else {
                        Destroy(go);
                    }
                }
            }
        }

        var toRemove = new List<int>();
        foreach (var kv in _itemsByUid) {
            if (!seen.Contains(kv.Key)) toRemove.Add(kv.Key);
        }
        foreach (var uid in toRemove) {
            if (_itemsByUid.TryGetValue(uid, out var it) && it != null) Destroy(it.gameObject);
            _itemsByUid.Remove(uid);
        }
    }

    private void ClearItems() {
        foreach (var kv in _itemsByUid) {
            if (kv.Value != null) Destroy(kv.Value.gameObject);
        }
        _itemsByUid.Clear();
    }
}
