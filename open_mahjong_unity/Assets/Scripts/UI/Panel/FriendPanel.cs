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
    public const int DefaultFriendCapacity = 80;
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
    [SerializeField] private GameObject requestTabBadge;
    [SerializeField] private Image requestTabBadgeImage;
    [SerializeField] private TMP_Text requestTabBadgeText;
    [SerializeField] private Color requestTabBadgeColor = new Color(0.91f, 0.22f, 0.22f, 1f);

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

    private FriendList<FriendInfo, FollowingItem> following;
    private FriendList<FriendInfo, FriendItem> friends;
    private FriendList<FriendRequestInfo, FriendRequestItem> requests;
    private int listOwner;

    private sealed class FriendList<TInfo, TItem> where TInfo : class where TItem : Component {
        private readonly ViewportCardList<int, TItem> view;
        private readonly System.Func<TInfo, int> uid;
        private readonly System.Func<TInfo, TInfo, bool> same;
        private Dictionary<int, TInfo> data = new();
        private readonly List<int> keys = new();
        private readonly float height;

        public FriendList(Transform container, GameObject prefab, System.Func<TInfo, int> uid,
                          System.Func<TInfo, TInfo, bool> same, System.Action<TItem, TInfo> bind) {
            this.uid = uid;
            this.same = same;
            var content = (RectTransform)container;
            var scroll = content.GetComponentInParent<ScrollRect>(true);
            var layout = prefab.GetComponent<LayoutElement>();
            height = layout && layout.preferredHeight > 0 ? layout.preferredHeight : ((RectTransform)prefab.transform).rect.height;
            view = new(scroll, content, _ => Object.Instantiate(prefab, content).GetComponent<TItem>(),
                (item, index) => bind(item, data[keys[index]]));
        }

        public void Replace(TInfo[] list, bool reset = false) {
            var next = new Dictionary<int, TInfo>();
            keys.Clear();
            if (list != null) foreach (var info in list) {
                if (info == null) continue;
                int id = uid(info);
                if (!next.ContainsKey(id)) keys.Add(id);
                next[id] = info;
                if (data.TryGetValue(id, out var previous) && !same(previous, info)) view.Invalidate(id);
            }
            data = next;
            view.SetRows(keys, _ => 0, _ => height, resetScroll: reset);
        }

        public void Tick() => view.Tick();
        public int Count => data.Count;
    }

    private static bool SameFriend(FriendInfo a, FriendInfo b) =>
        a.username == b.username && a.profile_image_id == b.profile_image_id && a.state == b.state && a.gamestate_id == b.gamestate_id;

    private int _followingMax = 10;
    private int _friendMax = DefaultFriendCapacity;
    private Coroutine _pollingCoroutine;
    private Tab _currentTab = Tab.Friends;

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
        following = new(followingContentTransform, followingItemPrefab, x => x.user_id, SameFriend, (row, info) => row.Initialize(info));
        friends = new(friendContentTransform, friendItemPrefab, x => x.user_id, SameFriend, (row, info) => row.Initialize(info));
        requests = new(requestContentTransform, friendRequestItemPrefab, x => x.user_id,
            (a, b) => a.username == b.username && a.profile_image_id == b.profile_image_id && a.created_at == b.created_at,
            (row, info) => row.Initialize(info));
        ApplyRequestTabBadgeColor();
        RefreshRequestTabBadge();
    }

#if UNITY_EDITOR
    private void OnValidate() {
        ApplyRequestTabBadgeColor();
    }
#endif

    private void OnEnable() {
        UnreadBadgeStore.OnChanged += RefreshRequestTabBadge;
        int owner = UserDataManager.Instance ? UserDataManager.Instance.UserId : 0;
        if (owner != listOwner || owner == 0) {
            listOwner = owner;
            following.Replace(null, true);
            friends.Replace(null, true);
            requests.Replace(null, true);
            _followingMax = 10;
            _friendMax = DefaultFriendCapacity;
        }
        SwitchTab(Tab.Friends);
        UpdateCountTexts(following.Count, friends.Count, requests.Count);
        RefreshRequestTabBadge();
        StartLivePolling();
    }

    private void OnDisable() {
        UnreadBadgeStore.OnChanged -= RefreshRequestTabBadge;
        if (_pollingCoroutine != null) {
            StopCoroutine(_pollingCoroutine);
            _pollingCoroutine = null;
        }
    }

    private IEnumerator CoPollFriendList() {
        var wait = new WaitForSeconds(pollingIntervalSeconds);
        while (true) {
            yield return wait;
            FriendNetworkManager.Instance.ListAllFriendPanels();
        }
    }

    private void StartLivePolling() {
        FriendNetworkManager.Instance.ListAllFriendPanels();
        if (_pollingCoroutine != null) StopCoroutine(_pollingCoroutine);
        _pollingCoroutine = StartCoroutine(CoPollFriendList());
    }

    private void SwitchTab(Tab tab) {
        _currentTab = tab;
        if (followingPanel != null) followingPanel.SetActive(tab == Tab.Following);
        if (friendsPanel != null) friendsPanel.SetActive(tab == Tab.Friends);
        if (requestsPanel != null) requestsPanel.SetActive(tab == Tab.Requests);
        if (tab == Tab.Requests) {
            UnreadBadgeStore.MarkFriendRequestsKnown();
        }
    }

    private void RefreshRequestTabBadge() {
        ApplyRequestTabBadgeColor();
        int count = UnreadBadgeStore.FriendUnread;
        bool show = count > 0;
        if (requestTabBadge != null) requestTabBadge.SetActive(show);
        if (requestTabBadgeText != null && show) requestTabBadgeText.text = count.ToString();
    }

    private void ApplyRequestTabBadgeColor() {
        if (requestTabBadgeImage != null) requestTabBadgeImage.color = requestTabBadgeColor;
    }

    private void OnClickAddFollowing() {
        if (followingUidInput == null || string.IsNullOrWhiteSpace(followingUidInput.text)) {
            NotificationManager.Instance.ShowTip("关注", false, "请输入要关注的玩家 UID");
            return;
        }
        if (!int.TryParse(followingUidInput.text.Trim(), out int uid)) {
            NotificationManager.Instance.ShowTip("关注", false, "UID 必须是数字");
            return;
        }
        FriendNetworkManager.Instance.AddFollowing(uid);
    }

    private void OnClickRequestFriend() {
        if (friendUidInput == null || string.IsNullOrWhiteSpace(friendUidInput.text)) {
            NotificationManager.Instance.ShowTip("好友", false, "请输入要申请的玩家 UID");
            return;
        }
        if (!int.TryParse(friendUidInput.text.Trim(), out int uid)) {
            NotificationManager.Instance.ShowTip("好友", false, "UID 必须是数字");
            return;
        }
        FriendNetworkManager.Instance.RequestFriend(uid);
    }

    public void ShowDeleteFriendConfirm(int userId, string username) {
        if (deleteConfirmPopup != null) {
            deleteConfirmPopup.Show(username, () => FriendNetworkManager.Instance.DeleteFriend(userId));
        } else {
            FriendNetworkManager.Instance.DeleteFriend(userId);
        }
    }

    public void OnFollowingListResponse(Response response) {
        ApplyFollowingList(response);
    }

    public void OnAddFollowingResponse(Response response) {
        if (!string.IsNullOrEmpty(response.message)) {
            NotificationManager.Instance.ShowTip("关注", response.success, response.message);
        }
        if (response.success && followingUidInput != null) followingUidInput.text = string.Empty;
        ApplyFollowingList(response);
    }

    public void OnRemoveFollowingResponse(Response response) {
        if (!string.IsNullOrEmpty(response.message)) {
            NotificationManager.Instance.ShowTip("关注", response.success, response.message);
        }
        ApplyFollowingList(response);
    }

    public void OnFriendListResponse(Response response) {
        ApplyFriendList(response);
    }

    public void OnRequestFriendResponse(Response response) {
        if (!string.IsNullOrEmpty(response.message)) {
            NotificationManager.Instance.ShowTip("好友", response.success, response.message);
        }
        if (response.success && friendUidInput != null) friendUidInput.text = string.Empty;
        ApplyFriendList(response);
        if (response.friend_request_list != null) ApplyFriendRequestList(response);
    }

    public void OnDeleteFriendResponse(Response response) {
        if (!string.IsNullOrEmpty(response.message)) {
            NotificationManager.Instance.ShowTip("好友", response.success, response.message);
        }
        ApplyFriendList(response);
    }

    public void OnFriendRequestListResponse(Response response) {
        ApplyFriendRequestList(response);
    }

    public void OnRespondFriendRequestResponse(Response response) {
        if (!string.IsNullOrEmpty(response.message)) {
            NotificationManager.Instance.ShowTip("好友申请", response.success, response.message);
        }
        ApplyFriendList(response);
        ApplyFriendRequestList(response);
    }

    private void LateUpdate() {
        switch (_currentTab) {
            case Tab.Following: following.Tick(); break;
            case Tab.Friends: friends.Tick(); break;
            case Tab.Requests: requests.Tick(); break;
        }
    }

    private void ApplyFollowingList(Response response) {
        if (response.friend_max.HasValue) _followingMax = response.friend_max.Value;
        following.Replace(response.friend_list);
        if (followingCountText) followingCountText.text = $"{following.Count}/{_followingMax}";
    }

    private void ApplyFriendList(Response response) {
        FriendRelationCache.ReplaceFromList(response.friend_list);
        if (response.friend_max.HasValue) _friendMax = response.friend_max.Value;
        friends.Replace(response.friend_list);
        if (friendCountText) friendCountText.text = $"{friends.Count}/{_friendMax}";
    }

    private void ApplyFriendRequestList(Response response) {
        requests.Replace(response.friend_request_list);
        if (requestCountText) requestCountText.text = requests.Count.ToString();
    }

    private void UpdateCountTexts(int followingCount, int friendCount, int requestCount) {
        if (followingCountText != null) followingCountText.text = $"{followingCount}/{_followingMax}";
        if (friendCountText != null) friendCountText.text = $"{friendCount}/{_friendMax}";
        if (requestCountText != null) requestCountText.text = $"{requestCount}";
    }
}
