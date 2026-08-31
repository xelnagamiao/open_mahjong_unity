using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 赛事/基地详情：节点全部在场景中绘制，运行时只填数据、克隆已挂载的房间/观战/牌谱预制体。
/// </summary>
public class EventDetailPanel : MonoBehaviour {
    public static EventDetailPanel Instance { get; private set; }

    private enum Page { Home, Rooms, Spectate, Records }

    [Header("导航")]
    [SerializeField] private Button backButton;
    [SerializeField] private Button homeNav;
    [SerializeField] private Button roomsNav;
    [SerializeField] private Button spectateNav;
    [SerializeField] private Button recordsNav;
    [SerializeField] private Image homeNavImage;
    [SerializeField] private Image roomsNavImage;
    [SerializeField] private Image spectateNavImage;
    [SerializeField] private Image recordsNavImage;

    [Header("导航颜色")]
    [InspectorName("选中底色")]
    [SerializeField] private Color navActiveColor = new Color(1f, 0.62f, 0.08f, 1f);
    [InspectorName("未选底色")]
    [SerializeField] private Color navIdleColor = new Color(0.13f, 0.13f, 0.13f, 1f);
    [InspectorName("选中文字")]
    [SerializeField] private Color navActiveLabelColor = new Color(0.12f, 0.06f, 0.02f, 1f);
    [InspectorName("未选文字")]
    [SerializeField] private Color navIdleLabelColor = Color.white;

    [Header("页面")]
    [SerializeField] private GameObject homePage;
    [SerializeField] private GameObject roomsPage;
    [SerializeField] private GameObject spectatePage;
    [SerializeField] private GameObject recordsPage;

    [Header("描述页")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text announceText;
    [SerializeField] private Button registerButton;
    [SerializeField] private Button readyButton;
    [SerializeField] private TMP_Text registerLabel;
    [SerializeField] private TMP_Text readyLabel;
    [SerializeField] private TMP_Text waitingCountText;
    [SerializeField] private Image readyImage;
    [InspectorName("等待中按钮")]
    [SerializeField] private Color readyWaitingColor = Color.white;
    [InspectorName("等待中文字")]
    [SerializeField] private Color readyWaitingLabelColor = Color.white;

    [Header("状态颜色")]
    [InspectorName("进行中")]
    [SerializeField] private Color statusActiveColor = new Color(0.02f, 0.72f, 0.32f, 1f);
    [InspectorName("未开赛")]
    [SerializeField] private Color statusIdleColor = new Color(0.90f, 0.72f, 0.08f, 1f);
    [InspectorName("已关闭")]
    [SerializeField] private Color statusClosedColor = new Color(0.77f, 0.34f, 0.34f, 1f);
    [InspectorName("未报名")]
    [SerializeField] private Color qualifyNoneColor = new Color(0.90f, 0.72f, 0.08f, 1f);
    [InspectorName("已报名")]
    [SerializeField] private Color qualifyOkColor = new Color(0.02f, 0.72f, 0.32f, 1f);

    [Header("加入房间")]
    [SerializeField] private Transform roomContent;
    [SerializeField] private TMP_Text roomsEmptyHint;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private GameObject roomItemPrefab;
    [SerializeField] private CreatePanel venueCreatePanel;

    [Header("观战 / 牌谱")]
    [SerializeField] private Transform spectateContent;
    [SerializeField] private TMP_Text spectateEmptyHint;
    [SerializeField] private GameObject spectateItemPrefab;
    [SerializeField] private Transform recordContent;
    [SerializeField] private TMP_Text recordsEmptyHint;
    [SerializeField] private GameObject recordItemPrefab;

    [Header("报名弹窗")]
    [SerializeField] private GameObject registerPopup;
    [SerializeField] private TMP_InputField contactInput;
    [SerializeField] private TMP_InputField remarkInput;
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private Button submitRegisterButton;
    [SerializeField] private Button cancelRegisterButton;

    private string _eventId;
    private string _kind = "event";
    private EventDetailInfo _detail;
    private Page _page = Page.Home;
    private Coroutine _pageFade;
    private Coroutine _venueFade;
    private Color _readyIdleColor = Color.white;
    private Color _readyLabelIdleColor = Color.white;
    private readonly List<GameObject> _roomSpawned = new List<GameObject>();
    private readonly List<GameObject> _spectateSpawned = new List<GameObject>();
    private readonly List<GameObject> _recordSpawned = new List<GameObject>();

    private void Awake() {
        Instance = this;
        registerPopup.SetActive(false);
        venueCreatePanel.gameObject.SetActive(false);
        _readyIdleColor = readyImage.color;
        _readyLabelIdleColor = readyLabel.color;
        backButton.onClick.AddListener(() => EventLobbyPanel.Instance?.ShowLobby());
        homeNav.onClick.AddListener(() => ShowPage(Page.Home));
        roomsNav.onClick.AddListener(() => ShowPage(Page.Rooms));
        spectateNav.onClick.AddListener(() => ShowPage(Page.Spectate));
        recordsNav.onClick.AddListener(() => ShowPage(Page.Records));
        registerButton.onClick.AddListener(OnRegisterClicked);
        readyButton.onClick.AddListener(OnReadyClicked);
        createRoomButton.onClick.AddListener(OpenVenueCreatePanel);
        submitRegisterButton.onClick.AddListener(SubmitRegister);
        cancelRegisterButton.onClick.AddListener(() => registerPopup.SetActive(false));
    }

    private void OnEnable() {
        if (EventNetworkManager.Instance == null) return;
        EventNetworkManager.Instance.OnEventDetailUpdated += OnDetail;
        EventNetworkManager.Instance.OnVenueRoomsUpdated += OnRooms;
        EventNetworkManager.Instance.OnEventRecordsUpdated += OnRecords;
    }

    private void OnDisable() {
        if (EventNetworkManager.Instance != null) {
            EventNetworkManager.Instance.OnEventDetailUpdated -= OnDetail;
            EventNetworkManager.Instance.OnVenueRoomsUpdated -= OnRooms;
            EventNetworkManager.Instance.OnEventRecordsUpdated -= OnRecords;
        }
    }

    public void Open(string eventId, string kind) {
        _eventId = eventId;
        _kind = string.IsNullOrEmpty(kind) ? "event" : kind;
        _detail = null;
        HideVenueCreate();
        ShowPage(Page.Home, true);
    }

    public bool HasOpenVenue => !string.IsNullOrEmpty(_eventId);

    public void RefreshVisiblePage() {
        if (string.IsNullOrEmpty(_eventId) || !gameObject.activeSelf) return;
        FetchPageData(_page);
    }

    public void ShowRoomsAfterCreate() {
        HideVenueCreate();
        ShowPage(Page.Rooms, true);
    }

    public void HideVenueCreate() {
        if (_venueFade != null) {
            StopCoroutine(_venueFade);
            _venueFade = null;
        }
        if (venueCreatePanel != null) {
            WindowFadeTransition.Snap(venueCreatePanel.gameObject, (GameObject)null);
            venueCreatePanel.CloseVenueMode();
        }
    }

    public void CloseVenueCreateFaded() {
        if (_venueFade != null) {
            StopCoroutine(_venueFade);
            _venueFade = null;
        }
        _venueFade = StartCoroutine(CloseVenueCreateRoutine());
    }

    private IEnumerator OpenVenueCreateRoutine() {
        venueCreatePanel.OpenForVenue(_eventId);
        yield return WindowFadeTransition.FadeOverlayIn(
            venueCreatePanel.gameObject, WindowFadeTransition.DurationSeconds);
        _venueFade = null;
    }

    private IEnumerator CloseVenueCreateRoutine() {
        if (venueCreatePanel.gameObject.activeSelf) {
            yield return WindowFadeTransition.FadeOverlayOut(
                venueCreatePanel.gameObject, WindowFadeTransition.DurationSeconds);
        }
        venueCreatePanel.CloseVenueMode();
        EventLobbyPanel.Instance?.OnVenueCreateClosed();
        _venueFade = null;
    }

    public void OnSpectatorList(SpectatorInfo[] list) {
        if (!isActiveAndEnabled || _page != Page.Spectate) return;
        ClearSpawned(spectateContent, _spectateSpawned, spectateEmptyHint);
        int count = 0;
        if (spectateItemPrefab != null && list != null) {
            foreach (var item in list) {
                if (item == null) continue;
                if (!string.IsNullOrEmpty(_eventId) && item.event_id != _eventId) continue;
                var go = Instantiate(spectateItemPrefab, spectateContent);
                go.SetActive(true);
                var binder = go.GetComponent<SpectatorPrefab>();
                binder.InitializeSpectatorItem(
                    item.rule,
                    item.sub_rule,
                    item.player1_name,
                    item.player2_name,
                    item.player3_name,
                    item.player4_name,
                    item.gamestate_id
                );
                _spectateSpawned.Add(go);
                count++;
            }
        }
        ShowEmptyHint(spectateEmptyHint, count == 0, "暂无可观战对局");
    }

    private void OnDetail() {
        _detail = EventNetworkManager.Instance != null ? EventNetworkManager.Instance.CurrentDetail : null;
        ApplyHome();
        createRoomButton.gameObject.SetActive(true);
    }

    private void OnRooms() {
        if (_page != Page.Rooms) return;
        RenderRooms();
    }

    private void OnRecords() {
        if (_page != Page.Records) return;
        RenderRecords();
    }

    private void ApplyHome() {
        bool isBase = (_detail != null ? _detail.kind : _kind) == "base";
        titleText.text = _detail != null ? (_detail.name ?? "") : "";
        descText.text = _detail != null && !string.IsNullOrEmpty(_detail.description) ? _detail.description : "暂无介绍";
        statusText.text = BuildStatusText(_detail);
        if (_detail == null || _detail.announcements == null || _detail.announcements.Length == 0) {
            announceText.text = "暂无公告";
        } else {
            var lines = new List<string>();
            foreach (var a in _detail.announcements) {
                if (a == null) continue;
                lines.Add($"【{a.title}】\n{a.body}");
            }
            announceText.text = lines.Count > 0 ? string.Join("\n\n", lines) : "暂无公告";
        }
        registerLabel.text = isBase ? "申请加入基地" : "报名比赛";
        registerButton.interactable = true;
        var reg = _detail != null ? _detail.registration : null;
        if (reg != null && reg.status == "pending") {
            registerLabel.text = "取消报名";
        } else if (reg != null && reg.status == "approved") {
            registerLabel.text = "已报名";
            registerButton.interactable = false;
        }
        if (_detail != null && _detail.is_ready) {
            readyLabel.text = isBase ? "基地等待中" : "比赛等待中";
        } else {
            readyLabel.text = "加入等待";
        }
        ApplyReadyVisual(_detail != null && _detail.is_ready);
        int n = _detail != null ? Mathf.Max(0, _detail.ready_count) : 0;
        waitingCountText.text = $"等待玩家：{n}";
    }

    private void ApplyReadyVisual(bool waiting) {
        readyImage.color = waiting ? readyWaitingColor : _readyIdleColor;
        readyLabel.color = waiting ? readyWaitingLabelColor : _readyLabelIdleColor;
    }

    private string BuildStatusText(EventDetailInfo detail) {
        string statusKey = detail != null ? detail.status : "";
        string statusLabel;
        Color statusColor;
        switch (statusKey) {
            case "active":
                statusLabel = "进行中";
                statusColor = statusActiveColor;
                break;
            case "closed":
                statusLabel = "已关闭";
                statusColor = statusClosedColor;
                break;
            default:
                statusLabel = "未开赛";
                statusColor = statusIdleColor;
                break;
        }

        bool qualified = detail != null && detail.registration != null
            && (detail.registration.status == "approved" || detail.registration.status == "pending");
        string qualifyLabel = qualified ? "已报名" : "未报名";
        Color qualifyColor = qualified ? qualifyOkColor : qualifyNoneColor;

        string start = FirstDay(detail != null ? detail.planned_start_at : null, detail != null ? detail.created_at : null);
        string end = FirstDay(detail != null ? detail.planned_end_at : null, detail != null ? detail.closed_at : null);
        if (string.IsNullOrEmpty(start)) start = "未定";
        if (string.IsNullOrEmpty(end)) end = "未定";

        return
            $"{Colorize("状态：" + statusLabel, statusColor)}\n" +
            $"开始时间：{start}\n" +
            $"结束时间：{end}\n" +
            $"{Colorize("资格：" + qualifyLabel, qualifyColor)}\n" +
            BuildEntryConfigText(detail);
    }

    private string BuildEntryConfigText(EventDetailInfo detail) {
        EventEntrySummary summary = detail != null ? detail.entry_summary : null;
        return
            $"{FormatSwitch("自动通过报名", summary != null && summary.auto_approve)}\n" +
            $"{FormatCreateRoomPermission(summary)}\n" +
            FormatSwitch("允许未报名玩家进入队列", summary != null && summary.unregistered_can_ready);
    }

    private string FormatCreateRoomPermission(EventEntrySummary summary) {
        string perm = summary != null ? summary.ResolvedCreateRoomPermission() : "admin";
        string label = perm == "all" ? "所有" : perm == "registered" ? "已报名" : "管理员";
        Color color = perm == "all" ? qualifyOkColor : perm == "registered" ? statusIdleColor : qualifyNoneColor;
        return Colorize($"创建房间权限：{label}", color);
    }

    private string FormatSwitch(string label, bool on) {
        string value = on ? "是" : "否";
        Color color = on ? qualifyOkColor : qualifyNoneColor;
        return Colorize($"{label}：{value}", color);
    }

    private static string FirstDay(string preferred, string fallback) {
        string a = FormatDay(preferred);
        return !string.IsNullOrEmpty(a) ? a : FormatDay(fallback);
    }

    private static string FormatDay(string raw) {
        if (string.IsNullOrEmpty(raw)) return "";
        if (DateTime.TryParse(raw, out DateTime d)) return d.ToString("yyyy-MM-dd");
        return raw.Length >= 10 ? raw.Substring(0, 10) : raw;
    }

    private static string Colorize(string text, Color color) {
        return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";
    }

    private void ShowPage(Page page, bool instant = false) {
        Page previous = _page;
        SetNav(homeNavImage, page == Page.Home);
        SetNav(roomsNavImage, page == Page.Rooms);
        SetNav(spectateNavImage, page == Page.Spectate);
        SetNav(recordsNavImage, page == Page.Records);
        if (instant || previous == page) {
            _page = page;
            if (_pageFade != null) {
                StopCoroutine(_pageFade);
                _pageFade = null;
            }
            ApplyPageActive(page);
            FetchPageData(page);
            return;
        }
        if (_pageFade != null) {
            StopCoroutine(_pageFade);
            _pageFade = null;
            _page = page;
            ApplyPageActive(page);
            FetchPageData(page);
            return;
        }
        _page = page;
        _pageFade = StartCoroutine(FadeToPageRoutine(previous, page));
    }

    private void FetchPageData(Page page) {
        if (page == Page.Home) {
            if (!string.IsNullOrEmpty(_eventId)) EventNetworkManager.Instance?.GetEventDetail(_eventId);
        } else if (page == Page.Rooms) {
            EventNetworkManager.Instance?.ListVenueRooms(_eventId);
        } else if (page == Page.Spectate) {
            GameStateNetworkManager.Instance?.GetSpectatorList();
        } else if (page == Page.Records) {
            EventNetworkManager.Instance?.ListEventRecords(_eventId);
        }
    }

    private GameObject PageGo(Page page) {
        switch (page) {
            case Page.Home: return homePage;
            case Page.Rooms: return roomsPage;
            case Page.Spectate: return spectatePage;
            default: return recordsPage;
        }
    }

    private void ApplyPageActive(Page page) {
        homePage.SetActive(page == Page.Home);
        roomsPage.SetActive(page == Page.Rooms);
        spectatePage.SetActive(page == Page.Spectate);
        recordsPage.SetActive(page == Page.Records);
        WindowFadeTransition.Normalize(homePage);
        WindowFadeTransition.Normalize(roomsPage);
        WindowFadeTransition.Normalize(spectatePage);
        WindowFadeTransition.Normalize(recordsPage);
    }

    private IEnumerator FadeToPageRoutine(Page fromPage, Page toPage) {
        yield return WindowFadeTransition.CrossFade(
            PageGo(fromPage), PageGo(toPage), WindowFadeTransition.DurationSeconds);
        FetchPageData(toPage);
        _pageFade = null;
    }

    private void SetNav(Image image, bool active) {
        image.color = active ? navActiveColor : navIdleColor;
        TMP_Text label = image.GetComponentInChildren<TMP_Text>(true);
        if (label == null) {
            label = image.transform.parent.GetComponentInChildren<TMP_Text>(true);
        }
        if (label != null) {
            label.color = active ? navActiveLabelColor : navIdleLabelColor;
        }
    }

    private static bool IsLoggedIn() {
        return UserDataManager.Instance.UserId > 0 && !UserDataManager.Instance.IsTourist;
    }

    private void OnRegisterClicked() {
        if (!IsLoggedIn()) {
            NotificationManager.Instance.ShowTip("event", false, "请先登录后再报名");
            return;
        }
        var reg = _detail != null ? _detail.registration : null;
        if (reg != null && reg.status == "approved") {
            NotificationManager.Instance.ShowTip("event", false, "已报名，无法取消");
            return;
        }
        if (reg != null && reg.status == "pending") {
            EventNetworkManager.Instance.CancelRegister(_eventId);
            return;
        }
        registerPopup.SetActive(true);
    }

    private void SubmitRegister() {
        EventNetworkManager.Instance.Register(
            _eventId,
            contactInput.text,
            remarkInput.text,
            joinCodeInput.text
        );
        registerPopup.SetActive(false);
    }

    private void OnReadyClicked() {
        if (!IsLoggedIn()) {
            NotificationManager.Instance.ShowTip("event", false, "请先登录后再加入等待");
            return;
        }
        if (_detail != null && _detail.is_ready) EventNetworkManager.Instance.Unready(_eventId);
        else EventNetworkManager.Instance.Ready(_eventId);
    }

    private void RenderRooms() {
        ClearSpawned(roomContent, _roomSpawned, roomsEmptyHint);
        var rooms = EventNetworkManager.Instance != null ? EventNetworkManager.Instance.VenueRooms : null;
        int count = 0;
        if (roomItemPrefab != null && rooms != null) {
            foreach (var room in rooms) {
                if (room == null) continue;
                var go = Instantiate(roomItemPrefab, roomContent);
                go.SetActive(true);
                go.GetComponent<RoomItem>().SetRoomInfo(room);
                _roomSpawned.Add(go);
                count++;
            }
        }
        ShowEmptyHint(roomsEmptyHint, count == 0, "暂无房间");
    }

    private void RenderRecords() {
        ClearSpawned(recordContent, _recordSpawned, recordsEmptyHint);
        var list = EventNetworkManager.Instance != null ? EventNetworkManager.Instance.EventRecords : null;
        int count = 0;
        if (recordItemPrefab != null && list != null) {
            foreach (var rec in list) {
                if (rec == null) continue;
                var go = Instantiate(recordItemPrefab, recordContent);
                go.SetActive(true);
                var item = go.GetComponent<RecordPrefab>();
                item.InitializeRecordItem(
                    rec.game_id,
                    rec.sub_rule ?? "",
                    rec.match_type ?? "",
                    rec.created_at,
                    rec.players,
                    rec.is_favorite
                );
                _recordSpawned.Add(go);
                count++;
            }
        }
        ShowEmptyHint(recordsEmptyHint, count == 0, "暂无牌谱");
    }

    private bool CanCreateRoom() {
        if (_detail == null) return false;
        if (_detail.is_admin) return true;
        string perm = _detail.entry_summary != null
            ? _detail.entry_summary.ResolvedCreateRoomPermission()
            : "admin";
        if (perm == "all") return true;
        if (perm != "registered") return false;
        return _detail.registration != null && _detail.registration.status == "approved";
    }

    private string CreateRoomDeniedTip() {
        string perm = _detail != null && _detail.entry_summary != null
            ? _detail.entry_summary.ResolvedCreateRoomPermission()
            : "admin";
        if (perm == "admin") {
            bool isBase = (_detail != null ? _detail.kind : _kind) == "base";
            return isBase ? "本基地仅限管理员创建房间" : "本赛事仅限管理员创建房间";
        }
        return "报名后可以创建房间";
    }

    private void OpenVenueCreatePanel() {
        if (!IsLoggedIn()) {
            NotificationManager.Instance.ShowTip("event", false, "请先登录后再创建房间");
            return;
        }
        if (_detail == null) {
            NotificationManager.Instance.ShowTip("event", false, "场馆信息加载中");
            return;
        }
        if (!CanCreateRoom()) {
            NotificationManager.Instance.ShowTip("event", false, CreateRoomDeniedTip());
            return;
        }
        if (UserDataManager.Instance.RoomId != UserDataManager.ROOM_ID_NONE) {
            NotificationManager.Instance.ShowTip("create_room", false, "必须先退出当前房间才能创建房间");
            return;
        }
        if (venueCreatePanel.IsVenueMode && venueCreatePanel.gameObject.activeSelf && _venueFade == null) {
            return;
        }
        if (_venueFade != null) {
            StopCoroutine(_venueFade);
            _venueFade = null;
        }
        WindowFadeTransition.Snap(venueCreatePanel.gameObject, (GameObject)null);
        _venueFade = StartCoroutine(OpenVenueCreateRoutine());
    }

    private static void ShowEmptyHint(TMP_Text hint, bool empty, string text) {
        if (hint == null) return;
        hint.text = text;
        hint.gameObject.SetActive(empty);
    }

    private static void ClearSpawned(Transform parent, List<GameObject> spawned, TMP_Text emptyHint) {
        foreach (GameObject go in spawned) {
            if (go != null) Destroy(go);
        }
        spawned.Clear();
        if (parent == null) return;
        for (int i = 0; i < parent.childCount; i++) {
            Transform child = parent.GetChild(i);
            if (emptyHint != null && child.gameObject == emptyHint.gameObject) continue;
            child.gameObject.SetActive(false);
        }
    }
}
