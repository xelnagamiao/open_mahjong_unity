using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 好友/关注 面板：UID 输入框 + 添加按钮 + "0/10" 文本 + 滚动列表。
/// 服务器消息通过 FriendNetworkManager 事件订阅。
/// 进入面板：清空 content 下全部子物体（含未纳入字典的残留）、注册 5s 轮询；退出面板：停止轮询。
/// 首轮请求在下一帧发出，避免与 Destroy 同帧导致列表与旧子物体叠加。
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
    private Coroutine _deferredPollingRoutine;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (addButton != null) addButton.onClick.AddListener(OnClickAdd);
    }

    private void OnEnable() {
        ClearContentAndItems();
        if (countText != null) countText.text = $"0/{_friendMax}";
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.OnFriendListResponse += HandleListResponse;
            FriendNetworkManager.Instance.OnAddFriendResponse += HandleAddResponse;
            FriendNetworkManager.Instance.OnRemoveFriendResponse += HandleRemoveResponse;
        }
        if (_deferredPollingRoutine != null) StopCoroutine(_deferredPollingRoutine);
        _deferredPollingRoutine = StartCoroutine(CoStartPollingAfterLayout());
    }

    private IEnumerator CoStartPollingAfterLayout() {
        yield return null;
        _deferredPollingRoutine = null;
        if (NetworkPollingManager.Instance != null) {
            NetworkPollingManager.Instance.StartFriendListPolling(pollingIntervalSeconds);
        }
    }

    private void OnDisable() {
        if (_deferredPollingRoutine != null) {
            StopCoroutine(_deferredPollingRoutine);
            _deferredPollingRoutine = null;
        }
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

    /// <summary>
    /// 清空字典并销毁 content 下所有子物体，避免旧版本残留或未跟踪的条目留在 ScrollView 里。
    /// </summary>
    private void ClearContentAndItems() {
        _itemsByUid.Clear();
        if (contentTransform == null) return;
        for (int i = contentTransform.childCount - 1; i >= 0; i--) {
            Transform c = contentTransform.GetChild(i);
            if (c != null) Destroy(c.gameObject);
        }
    }
}
