using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 好友面板：关注者 / 好友 / 好友申请三个子面板。
/// 关注者用于普通延时观战；好友用于普通观战、实时观战和双向删除；好友申请用于接受 / 拒绝。
/// </summary>
public class FriendPanel : MonoBehaviour {
    public static FriendPanel Instance { get; private set; }

    private enum Tab {
        Following,
        Friends,
        Requests,
    }

    [Header("页签按钮")]
    [SerializeField] private Button followingTabButton;
    [SerializeField] private Button friendsTabButton;
    [SerializeField] private Button requestsTabButton;

    [Header("子面板")]
    [SerializeField] private GameObject followingPanel;
    [SerializeField] private GameObject friendsPanel;
    [SerializeField] private GameObject requestsPanel;

    [Header("关注者面板")]
    [SerializeField] private TMP_InputField followingUidInput;
    [SerializeField] private Button addFollowingButton;
    [SerializeField] private TMP_Text followingCountText;
    [SerializeField] private Transform followingContentTransform;
    [SerializeField] private GameObject followingItemPrefab;

    [Header("好友面板")]
    [SerializeField] private TMP_InputField friendUidInput;
    [SerializeField] private Button requestFriendButton;
    [SerializeField] private TMP_Text friendCountText;
    [SerializeField] private Transform friendContentTransform;
    [SerializeField] private GameObject friendItemPrefab;

    [Header("好友申请面板")]
    [SerializeField] private TMP_Text requestCountText;
    [SerializeField] private Transform requestContentTransform;
    [SerializeField] private GameObject friendRequestItemPrefab;

    [Header("确认弹窗")]
    [SerializeField] private FriendDeleteConfirmPopup deleteConfirmPopup;

    [Header("轮询")]
    [SerializeField] private float pollingIntervalSeconds = 5f;

    private readonly Dictionary<int, FollowingItem> _followingItemsByUid = new Dictionary<int, FollowingItem>();
    private readonly Dictionary<int, FriendItem> _friendItemsByUid = new Dictionary<int, FriendItem>();
    private readonly Dictionary<int, FriendRequestItem> _requestItemsByUid = new Dictionary<int, FriendRequestItem>();
    private int _followingMax = 10;
    private int _friendMax = 20;
    private Coroutine _pollingCoroutine;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (followingTabButton != null) followingTabButton.onClick.AddListener(() => SwitchTab(Tab.Following));
        if (friendsTabButton != null) friendsTabButton.onClick.AddListener(() => SwitchTab(Tab.Friends));
        if (requestsTabButton != null) requestsTabButton.onClick.AddListener(() => SwitchTab(Tab.Requests));
        if (addFollowingButton != null) addFollowingButton.onClick.AddListener(OnClickAddFollowing);
        if (requestFriendButton != null) requestFriendButton.onClick.AddListener(OnClickRequestFriend);
        ClearAllItems();
    }

    private void OnEnable() {
        ClearAllItems();
        SwitchTab(Tab.Friends);
        UpdateCountTexts(0, 0, 0);
        FriendNetworkManager.Instance?.ListAllFriendPanels();
        if (_pollingCoroutine != null) StopCoroutine(_pollingCoroutine);
        _pollingCoroutine = StartCoroutine(CoPollFriendList());
    }

    private void OnDisable() {
        if (_pollingCoroutine != null) {
            StopCoroutine(_pollingCoroutine);
            _pollingCoroutine = null;
        }
    }

    private IEnumerator CoPollFriendList() {
        var wait = new WaitForSeconds(pollingIntervalSeconds);
        while (true) {
            yield return wait;
            FriendNetworkManager.Instance?.ListAllFriendPanels();
        }
    }

    private void SwitchTab(Tab tab) {
        if (followingPanel != null) followingPanel.SetActive(tab == Tab.Following);
        if (friendsPanel != null) friendsPanel.SetActive(tab == Tab.Friends);
        if (requestsPanel != null) requestsPanel.SetActive(tab == Tab.Requests);
    }

    private void OnClickAddFollowing() {
        if (followingUidInput == null || string.IsNullOrWhiteSpace(followingUidInput.text)) {
            NotificationManager.Instance?.ShowTip("关注", false, "请输入要关注的玩家 UID");
            return;
        }
        if (!int.TryParse(followingUidInput.text.Trim(), out int uid)) {
            NotificationManager.Instance?.ShowTip("关注", false, "UID 必须是数字");
            return;
        }
        FriendNetworkManager.Instance?.AddFollowing(uid);
    }

    private void OnClickRequestFriend() {
        if (friendUidInput == null || string.IsNullOrWhiteSpace(friendUidInput.text)) {
            NotificationManager.Instance?.ShowTip("好友", false, "请输入要申请的玩家 UID");
            return;
        }
        if (!int.TryParse(friendUidInput.text.Trim(), out int uid)) {
            NotificationManager.Instance?.ShowTip("好友", false, "UID 必须是数字");
            return;
        }
        FriendNetworkManager.Instance?.RequestFriend(uid);
    }

    public void ShowDeleteFriendConfirm(int userId, string username) {
        if (deleteConfirmPopup != null) {
            deleteConfirmPopup.Show(username, () => FriendNetworkManager.Instance?.DeleteFriend(userId));
        } else {
            FriendNetworkManager.Instance?.DeleteFriend(userId);
        }
    }

    public void OnFollowingListResponse(Response response) {
        ApplyFollowingList(response);
    }

    public void OnAddFollowingResponse(Response response) {
        if (!string.IsNullOrEmpty(response.message)) {
            NotificationManager.Instance?.ShowTip("关注", response.success, response.message);
        }
        if (response.success && followingUidInput != null) followingUidInput.text = string.Empty;
        ApplyFollowingList(response);
    }

    public void OnRemoveFollowingResponse(Response response) {
        if (!string.IsNullOrEmpty(response.message)) {
            NotificationManager.Instance?.ShowTip("关注", response.success, response.message);
        }
        ApplyFollowingList(response);
    }

    public void OnFriendListResponse(Response response) {
        ApplyFriendList(response);
    }

    public void OnRequestFriendResponse(Response response) {
        if (!string.IsNullOrEmpty(response.message)) {
            NotificationManager.Instance?.ShowTip("好友", response.success, response.message);
        }
        if (response.success && friendUidInput != null) friendUidInput.text = string.Empty;
        ApplyFriendList(response);
    }

    public void OnDeleteFriendResponse(Response response) {
        if (!string.IsNullOrEmpty(response.message)) {
            NotificationManager.Instance?.ShowTip("好友", response.success, response.message);
        }
        ApplyFriendList(response);
    }

    public void OnFriendRequestListResponse(Response response) {
        ApplyFriendRequestList(response);
    }

    public void OnRespondFriendRequestResponse(Response response) {
        if (!string.IsNullOrEmpty(response.message)) {
            NotificationManager.Instance?.ShowTip("好友申请", response.success, response.message);
        }
        ApplyFriendList(response);
        ApplyFriendRequestList(response);
    }

    private void ApplyFollowingList(Response response) {
        if (response.friend_max.HasValue) _followingMax = response.friend_max.Value;
        var list = response.friend_list;
        int count = list?.Length ?? 0;
        if (followingCountText != null) followingCountText.text = $"{count}/{_followingMax}";
        if (followingContentTransform == null || followingItemPrefab == null) return;

        var seen = new HashSet<int>();
        if (list != null) {
            foreach (var info in list) {
                if (info == null) continue;
                seen.Add(info.user_id);
                if (_followingItemsByUid.TryGetValue(info.user_id, out var existing) && existing != null) {
                    existing.Refresh(info);
                } else {
                    var go = Instantiate(followingItemPrefab, followingContentTransform);
                    var item = go.GetComponent<FollowingItem>();
                    item.Initialize(info);
                    _followingItemsByUid[info.user_id] = item;
                }
            }
        }
        RemoveMissing(_followingItemsByUid, seen);
    }

    private void ApplyFriendList(Response response) {
        FriendRelationCache.ReplaceFromList(response.friend_list);
        if (response.friend_max.HasValue) _friendMax = response.friend_max.Value;
        var list = response.friend_list;
        int count = list?.Length ?? 0;
        if (friendCountText != null) friendCountText.text = $"{count}/{_friendMax}";
        if (friendContentTransform == null || friendItemPrefab == null) return;

        var seen = new HashSet<int>();
        if (list != null) {
            foreach (var info in list) {
                if (info == null) continue;
                seen.Add(info.user_id);
                if (_friendItemsByUid.TryGetValue(info.user_id, out var existing) && existing != null) {
                    existing.Refresh(info);
                } else {
                    var go = Instantiate(friendItemPrefab, friendContentTransform);
                    var item = go.GetComponent<FriendItem>();
                    item.Initialize(info);
                    _friendItemsByUid[info.user_id] = item;
                }
            }
        }
        RemoveMissing(_friendItemsByUid, seen);
    }

    private void ApplyFriendRequestList(Response response) {
        var list = response.friend_request_list;
        int count = list?.Length ?? 0;
        if (requestCountText != null) requestCountText.text = $"{count}";
        if (requestContentTransform == null || friendRequestItemPrefab == null) return;

        var seen = new HashSet<int>();
        if (list != null) {
            foreach (var info in list) {
                if (info == null) continue;
                seen.Add(info.user_id);
                if (_requestItemsByUid.TryGetValue(info.user_id, out var existing) && existing != null) {
                    existing.Refresh(info);
                } else {
                    var go = Instantiate(friendRequestItemPrefab, requestContentTransform);
                    var item = go.GetComponent<FriendRequestItem>();
                    item.Initialize(info);
                    _requestItemsByUid[info.user_id] = item;
                }
            }
        }
        RemoveMissing(_requestItemsByUid, seen);
    }

    private static void RemoveMissing<T>(Dictionary<int, T> itemsByUid, HashSet<int> seen) where T : Component {
        var toRemove = new List<int>();
        foreach (var kv in itemsByUid) {
            if (!seen.Contains(kv.Key)) toRemove.Add(kv.Key);
        }
        foreach (int uid in toRemove) {
            if (itemsByUid.TryGetValue(uid, out var item) && item != null) Destroy(item.gameObject);
            itemsByUid.Remove(uid);
        }
    }

    private void ClearAllItems() {
        ClearContainer(followingContentTransform);
        ClearContainer(friendContentTransform);
        ClearContainer(requestContentTransform);
        _followingItemsByUid.Clear();
        _friendItemsByUid.Clear();
        _requestItemsByUid.Clear();
    }

    private static void ClearContainer(Transform container) {
        if (container == null) return;
        for (int i = container.childCount - 1; i >= 0; i--) {
            Transform c = container.GetChild(i);
            if (c != null) Destroy(c.gameObject);
        }
    }

    private void UpdateCountTexts(int followingCount, int friendCount, int requestCount) {
        if (followingCountText != null) followingCountText.text = $"{followingCount}/{_followingMax}";
        if (friendCountText != null) friendCountText.text = $"{friendCount}/{_friendMax}";
        if (requestCountText != null) requestCountText.text = $"{requestCount}";
    }
}
