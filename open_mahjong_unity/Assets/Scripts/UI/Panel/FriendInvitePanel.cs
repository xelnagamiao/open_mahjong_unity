using System;
using System.Collections.Generic;
using Salasasa.Home;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One friend picker, parameterized by party or room invitation context.</summary>
public sealed class FriendInvitePanel : MonoBehaviour {
    public BakedModalFade fade;
    public TMP_Text title, subtitle, empty;
    public TMP_InputField search;
    public RectTransform content;
    public FriendInviteRow rowTemplate;
    public Button close;
    readonly List<FriendInviteRow> rows = new List<FriendInviteRow>();
    string kind, roomId, roomInstanceId;
    float nextRefresh, nextPoll, pendingSince;
    int pendingUser;
    public bool CanInvite => kind == "room" || !PartyNetworkManager.Instance.HasParty
        || PartyNetworkManager.Instance.IsLeader && PartyNetworkManager.Instance.MemberCount < PartyNetworkManager.Instance.Party.capacity;
    void Awake() {
        ConfigureScrolling();
        close.onClick.AddListener(Hide); search.onValueChanged.AddListener(_ => Filter());
    }
    public void ConfigureScrolling() {
        var scroll = content ? content.GetComponentInParent<UnityEngine.UI.ScrollRect>() : null;
        if (!scroll) return;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.scrollSensitivity = 90; scroll.inertia = true;
        if (!scroll.verticalScrollbar) {
            var track = new GameObject("VerticalScrollbar", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Scrollbar));
            track.layer = scroll.gameObject.layer;
            var rect = (RectTransform)track.transform; rect.SetParent(scroll.transform, false);
            rect.anchorMin = new Vector2(1, 0); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(1, .5f);
            rect.anchoredPosition = new Vector2(-2, 0); rect.sizeDelta = new Vector2(16, -8);
            track.GetComponent<UnityEngine.UI.Image>().color = new Color(.09f, .10f, .12f);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            handle.layer = track.layer;
            var handleRect = (RectTransform)handle.transform; handleRect.SetParent(rect, false);
            handleRect.anchorMin = Vector2.zero; handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = new Vector2(2, 2); handleRect.offsetMax = new Vector2(-2, -2);
            var image = handle.GetComponent<UnityEngine.UI.Image>(); image.color = new Color(.77f, .64f, .39f);
            var bar = track.GetComponent<UnityEngine.UI.Scrollbar>(); bar.handleRect = handleRect; bar.targetGraphic = image;
            bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            bar.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            scroll.verticalScrollbar = bar;
        }
        scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.Permanent;
        scroll.verticalScrollbar.gameObject.SetActive(true);
        var grid = content.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (grid && grid.constraint == UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount) {
            var padding = grid.padding;
            grid.padding = new RectOffset(padding.left, Mathf.Max(24, padding.right), padding.top, padding.bottom);
            int columns = Mathf.Max(1, grid.constraintCount);
            float width = ((RectTransform)scroll.transform).rect.width - grid.padding.horizontal - grid.spacing.x * (columns - 1);
            if (width > 0) grid.cellSize = new Vector2(width / columns, grid.cellSize.y);
        }
    }
    void OnEnable() { FriendRelationCache.OnChanged += Rebuild; PartyNetworkManager.Instance.Changed += RefreshRows; }
    void OnDisable() { FriendRelationCache.OnChanged -= Rebuild; if (PartyNetworkManager.Instance) PartyNetworkManager.Instance.Changed -= RefreshRows; }
    public void ShowParty() { roomInstanceId = null; Show("party", null); }
    public void ShowRoom(RoomInfo room) { if (room != null) { roomInstanceId = room.instance_id; Show("room", room.room_id); } }
    void Show(string context, string id) {
        kind = context; roomId = id; pendingUser = 0;
        title.text = PlatformText.Get("邀请好友");
        subtitle.text = context == "room" ? PlatformText.Get("邀请好友加入房间") + " · " + id : PlatformText.Get("邀请好友组队");
        search.SetTextWithoutNotify(""); fade.Show(); Rebuild();
        FriendNetworkManager.Instance.ListFriends();
    }
    public void Hide() => fade.Hide();
    public LobbyFriendInvite Sent(int uid) => PartyNetworkManager.Instance.Outgoing(uid, kind, roomId);
    public bool IsPending(int uid) => pendingUser == uid && Time.unscaledTime - pendingSince < 10;
    public void RequestFinished() { pendingUser = 0; if (isActiveAndEnabled) RefreshRows(); }
    public void Invite(FriendInfo friend) {
        var sent = Sent(friend.user_id);
        if (sent != null) PartyNetworkManager.Instance.Revoke(sent.invite_id);
        else { pendingUser = friend.user_id; pendingSince = Time.unscaledTime; PartyNetworkManager.Instance.Invite(friend.user_id, kind, roomId, roomInstanceId); }
        RefreshRows();
    }
    void Update() {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 1; RefreshRows();
        if (Time.unscaledTime >= nextPoll) { nextPoll = Time.unscaledTime + 5; FriendNetworkManager.Instance.ListFriends(); }
    }
    void Rebuild() {
        foreach (var row in rows) Destroy(row.gameObject); rows.Clear();
        var friends = new List<FriendInfo>(FriendRelationCache.Friends);
        friends.Sort((a, b) => { int d = StateOrder(a.state).CompareTo(StateOrder(b.state)); return d != 0 ? d : string.Compare(a.username, b.username, StringComparison.CurrentCulture); });
        foreach (var friend in friends) {
            var row = Instantiate(rowTemplate, content, false); row.gameObject.SetActive(true); row.Bind(this, friend); rows.Add(row);
        }
        Filter();
    }
    static int StateOrder(string state) => state == "online" ? 0 : state == "in_game" ? 1 : 2;
    void Filter() {
        int count = 0;
        foreach (var row in rows) {
            bool visible = string.IsNullOrWhiteSpace(search.text) || row.Info.username.IndexOf(search.text, StringComparison.CurrentCultureIgnoreCase) >= 0
                || row.Info.user_id.ToString().Contains(search.text);
            row.gameObject.SetActive(visible); if (visible) count++;
        }
        empty.gameObject.SetActive(count == 0);
        empty.text = PlatformText.Get(FriendRelationCache.IsLoaded ? "暂无可显示的好友" : "正在获取好友列表…");
    }
    void RefreshRows() { foreach (var row in rows) row.Refresh(); }
}
