using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 赛事/基地详情：节点全部在场景中绘制，运行时只填数据、克隆场景里的房间/牌谱示例预制体。
/// </summary>
public class EventDetailPanel : MonoBehaviour {
    public static EventDetailPanel Instance { get; private set; }

    private const float ItemScale = 0.85f;

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
    [SerializeField] private Color navActiveColor = new Color(1f, 0.62f, 0.08f, 1f);
    [SerializeField] private Color navIdleColor = new Color(0.13f, 0.13f, 0.13f, 1f);
    [SerializeField] private Color navActiveLabelColor = new Color(0.12f, 0.06f, 0.02f, 1f);
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
    [SerializeField] private Color readyWaitingColor = Color.white;
    [SerializeField] private Color readyWaitingLabelColor = Color.white;

    [Header("状态颜色")]
    [SerializeField] private Color statusActiveColor = new Color(0.02f, 0.72f, 0.32f, 1f);
    [SerializeField] private Color statusIdleColor = new Color(0.90f, 0.72f, 0.08f, 1f);
    [SerializeField] private Color statusClosedColor = new Color(0.77f, 0.34f, 0.34f, 1f);
    [SerializeField] private Color qualifyNoneColor = new Color(0.90f, 0.72f, 0.08f, 1f);
    [SerializeField] private Color qualifyOkColor = new Color(0.02f, 0.72f, 0.32f, 1f);

    [Header("加入房间")]
    [SerializeField] private Transform roomContent;
    [SerializeField] private Transform readyContent;
    [SerializeField] private GameObject adminBar;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button seatButton;
    [SerializeField] private GameObject roomItemPrefab;
    [SerializeField] private CreatePanel venueCreatePanel;

    [Header("观战 / 牌谱")]
    [SerializeField] private Transform spectateContent;
    [SerializeField] private Transform recordContent;
    [SerializeField] private GameObject recordItemPrefab;

    [Header("行模板（观战/空提示）")]
    [SerializeField] private GameObject actionRowTemplate;
    [SerializeField] private GameObject emptyRowTemplate;

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
    private readonly List<int> _selectedReady = new List<int>();
    private Color _readyIdleColor = Color.white;
    private Color _readyLabelIdleColor = Color.white;

    private void Awake() {
        Instance = this;
        HideSceneTemplate(actionRowTemplate);
        HideSceneTemplate(emptyRowTemplate);
        if (registerPopup != null) registerPopup.SetActive(false);
        if (adminBar != null) adminBar.SetActive(false);
        if (readyContent != null) readyContent.gameObject.SetActive(false);
        if (readyImage == null && readyButton != null) {
            readyImage = readyButton.targetGraphic as Image;
        }
        if (readyImage != null) _readyIdleColor = readyImage.color;
        if (readyLabel != null) _readyLabelIdleColor = readyLabel.color;
        if (backButton != null) backButton.onClick.AddListener(() => EventLobbyPanel.Instance?.ShowLobby());
        if (homeNav != null) homeNav.onClick.AddListener(() => ShowPage(Page.Home));
        if (roomsNav != null) roomsNav.onClick.AddListener(() => ShowPage(Page.Rooms));
        if (spectateNav != null) spectateNav.onClick.AddListener(() => ShowPage(Page.Spectate));
        if (recordsNav != null) recordsNav.onClick.AddListener(() => ShowPage(Page.Records));
        if (registerButton != null) registerButton.onClick.AddListener(OnRegisterClicked);
        if (readyButton != null) readyButton.onClick.AddListener(OnReadyClicked);
        if (createRoomButton != null) createRoomButton.onClick.AddListener(OpenVenueCreatePanel);
        if (seatButton != null) seatButton.onClick.AddListener(SeatSelected);
        if (submitRegisterButton != null) submitRegisterButton.onClick.AddListener(SubmitRegister);
        if (cancelRegisterButton != null) cancelRegisterButton.onClick.AddListener(() => registerPopup.SetActive(false));
    }

    private static void HideSceneTemplate(GameObject template) {
        if (template != null && template.scene.IsValid()) template.SetActive(false);
    }

    private void OnEnable() {
        if (EventNetworkManager.Instance != null) {
            EventNetworkManager.Instance.OnEventDetailUpdated += OnDetail;
            EventNetworkManager.Instance.OnVenueRoomsUpdated += OnRooms;
            EventNetworkManager.Instance.OnEventRecordsUpdated += OnRecords;
        }
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
        HideVenueCreate();
        ShowPage(Page.Home);
        EventNetworkManager.Instance?.GetEventDetail(eventId);
    }

    public void ShowRoomsAfterCreate() {
        HideVenueCreate();
        ShowPage(Page.Rooms);
        if (!string.IsNullOrEmpty(_eventId)) {
            EventNetworkManager.Instance?.ListVenueRooms(_eventId);
        }
    }

    public void HideVenueCreate() {
        if (venueCreatePanel != null && venueCreatePanel.IsVenueMode) {
            venueCreatePanel.CloseVenueMode();
        } else if (venueCreatePanel != null) {
            venueCreatePanel.gameObject.SetActive(false);
        }
    }

    public void OnSpectatorList(SpectatorInfo[] list) {
        if (!isActiveAndEnabled || _page != Page.Spectate) return;
        ClearSpawned(spectateContent);
        if (list == null || list.Length == 0) {
            AddEmptyRow(spectateContent, "暂无可观战对局");
            return;
        }
        int count = 0;
        foreach (var item in list) {
            if (item == null) continue;
            if (!string.IsNullOrEmpty(_eventId) && item.event_id != _eventId) continue;
            count++;
            var captured = item;
            AddActionRow(spectateContent, $"{item.player1_name} / {item.player2_name} / {item.player3_name} / {item.player4_name}", "观战", () => {
                GameStateNetworkManager.Instance.AddSpectator(captured.gamestate_id);
            });
        }
        if (count == 0) AddEmptyRow(spectateContent, "暂无可观战对局");
    }

    private void OnDetail() {
        _detail = EventNetworkManager.Instance != null ? EventNetworkManager.Instance.CurrentDetail : null;
        ApplyHome();
        if (adminBar != null) adminBar.SetActive(CanCreateRoom() || (_detail != null && _detail.is_admin));
        if (createRoomButton != null) createRoomButton.gameObject.SetActive(CanCreateRoom());
        if (seatButton != null) seatButton.gameObject.SetActive(_detail != null && _detail.is_admin);
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
        string name = _detail != null ? (_detail.name ?? "") : "";
        if (titleText != null) titleText.text = name;
        if (descText != null) descText.text = _detail != null && !string.IsNullOrEmpty(_detail.description) ? _detail.description : "暂无介绍";
        if (statusText != null) {
            statusText.richText = true;
            statusText.text = BuildStatusText(_detail);
        }
        if (announceText != null) {
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
        }
        if (registerLabel != null) {
            registerLabel.text = isBase ? "申请加入基地" : "报名比赛";
            var reg = _detail != null ? _detail.registration : null;
            if (reg != null && (reg.status == "pending" || reg.status == "approved")) {
                registerLabel.text = "取消报名";
            }
        }
        if (readyLabel != null) {
            if (_detail != null && _detail.is_ready) {
                readyLabel.text = isBase ? "基地等待中" : "比赛等待中";
            } else {
                readyLabel.text = "加入等待";
            }
        }
        ApplyReadyVisual(_detail != null && _detail.is_ready);
        if (waitingCountText != null) {
            int n = _detail != null ? Mathf.Max(0, _detail.ready_count) : 0;
            waitingCountText.text = $"等待玩家：{n}";
        }
        if (registerButton != null) registerButton.interactable = true;
        if (readyButton != null) readyButton.interactable = true;
    }

    private void ApplyReadyVisual(bool waiting) {
        if (readyImage != null) {
            readyImage.color = waiting ? readyWaitingColor : _readyIdleColor;
        }
        if (readyLabel != null) {
            readyLabel.color = waiting ? readyWaitingLabelColor : _readyLabelIdleColor;
        }
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
            Colorize("资格：" + qualifyLabel, qualifyColor);
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

    private void ShowPage(Page page) {
        _page = page;
        if (homePage != null) homePage.SetActive(page == Page.Home);
        if (roomsPage != null) roomsPage.SetActive(page == Page.Rooms);
        if (spectatePage != null) spectatePage.SetActive(page == Page.Spectate);
        if (recordsPage != null) recordsPage.SetActive(page == Page.Records);
        SetNav(homeNavImage, page == Page.Home);
        SetNav(roomsNavImage, page == Page.Rooms);
        SetNav(spectateNavImage, page == Page.Spectate);
        SetNav(recordsNavImage, page == Page.Records);
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

    private void SetNav(Image image, bool active) {
        if (image != null) image.color = active ? navActiveColor : navIdleColor;
        if (image == null) return;
        TMP_Text label = image.GetComponentInChildren<TMP_Text>(true);
        if (label == null && image.transform.parent != null) {
            label = image.transform.parent.GetComponentInChildren<TMP_Text>(true);
        }
        if (label != null) {
            label.color = active ? navActiveLabelColor : navIdleLabelColor;
        }
    }

    private static bool IsLoggedIn() {
        return UserDataManager.Instance != null && UserDataManager.Instance.UserId > 0 && !UserDataManager.Instance.IsTourist;
    }

    private void OnRegisterClicked() {
        if (!IsLoggedIn()) {
            NotificationManager.Instance.ShowTip("event", false, "请先登录后再报名");
            return;
        }
        var reg = _detail != null ? _detail.registration : null;
        if (reg != null && (reg.status == "pending" || reg.status == "approved")) {
            EventNetworkManager.Instance.CancelRegister(_eventId);
            return;
        }
        if (registerPopup != null) registerPopup.SetActive(true);
    }

    private void SubmitRegister() {
        EventNetworkManager.Instance.Register(
            _eventId,
            contactInput != null ? contactInput.text : "",
            remarkInput != null ? remarkInput.text : "",
            joinCodeInput != null ? joinCodeInput.text : ""
        );
        if (registerPopup != null) registerPopup.SetActive(false);
    }

    private void OnReadyClicked() {
        if (!IsLoggedIn()) {
            NotificationManager.Instance.ShowTip("event", false, "请先登录后再加入等待");
            return;
        }
        bool approved = _detail != null && _detail.registration != null && _detail.registration.status == "approved";
        bool admin = _detail != null && _detail.is_admin;
        if (!approved && !admin) {
            NotificationManager.Instance.ShowTip("event", false, "报名通过后才能加入等待");
            return;
        }
        if (_detail != null && _detail.is_ready) EventNetworkManager.Instance.Unready(_eventId);
        else EventNetworkManager.Instance.Ready(_eventId);
    }

    private void RenderRooms() {
        ClearSpawned(roomContent);
        var rooms = EventNetworkManager.Instance != null ? EventNetworkManager.Instance.VenueRooms : null;
        if (rooms == null || rooms.Length == 0) {
            AddEmptyRow(roomContent, "暂无房间");
            return;
        }
        GameObject prefab = roomItemPrefab;
        if (prefab == null && RoomListPanel.Instance != null) prefab = RoomListPanel.Instance.RoomItemPrefab;
        foreach (var room in rooms) {
            if (room == null) continue;
            if (prefab != null) {
                var go = Instantiate(prefab, roomContent);
                go.SetActive(true);
                go.transform.localScale = Vector3.one * ItemScale;
                var item = go.GetComponent<RoomItem>();
                if (item != null) item.SetRoomInfo(room);
            } else {
                var captured = room;
                AddActionRow(roomContent, $"{captured.room_name}  {captured.room_id}", "加入", () => {
                    JoinRoom(captured.room_id, captured.has_password);
                });
            }
        }
    }

    private static void JoinRoom(string roomId, bool needPassword) {
        if (RoomListPanel.Instance != null) {
            RoomListPanel.Instance.JoinClicked(roomId, needPassword);
            return;
        }
        if (needPassword) {
            NotificationManager.Instance.ShowTip("join_room", false, "该房间需要密码，请从房间页加入");
            return;
        }
        RoomNetworkManager.Instance.JoinRoom(roomId, "");
    }

    private void RenderRecords() {
        ClearSpawned(recordContent);
        var list = EventNetworkManager.Instance != null ? EventNetworkManager.Instance.EventRecords : null;
        if (list == null || list.Length == 0) {
            AddEmptyRow(recordContent, "暂无牌谱");
            return;
        }
        foreach (var rec in list) {
            if (rec == null) continue;
            if (recordItemPrefab != null) {
                var go = Instantiate(recordItemPrefab, recordContent);
                go.SetActive(true);
                go.transform.localScale = Vector3.one * ItemScale;
                var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
                layout.minHeight = 182f;
                layout.preferredHeight = 182f;
                var item = go.GetComponent<RecordPrefab>() ?? go.GetComponentInChildren<RecordPrefab>(true);
                if (item != null) {
                    item.InitializeRecordItem(
                        rec.game_id,
                        rec.sub_rule ?? "",
                        rec.match_type ?? "",
                        rec.created_at,
                        rec.players,
                        rec.is_favorite
                    );
                }
            } else {
                string captured = rec.game_id;
                AddActionRow(recordContent, rec.game_id, "查看", () => {
                    DataNetworkManager.Instance.GetRecordById(captured);
                });
            }
        }
        if (recordContent is RectTransform rt) {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }

    private bool CanCreateRoom() {
        if (_detail == null) return false;
        if (_detail.is_admin) return true;
        if (_detail.kind != "base") return false;
        bool memberCreate = _detail.entry_summary != null && _detail.entry_summary.member_can_create_room;
        bool approved = _detail.registration != null && _detail.registration.status == "approved";
        return memberCreate && approved;
    }

    private void OpenVenueCreatePanel() {
        if (!CanCreateRoom()) {
            NotificationManager.Instance.ShowTip("event", false, "当前没有建房权限");
            return;
        }
        if (UserDataManager.Instance.RoomId != UserDataManager.ROOM_ID_NONE) {
            NotificationManager.Instance.ShowTip("create_room", false, "必须先退出当前房间才能创建房间");
            return;
        }
        if (venueCreatePanel == null) {
            NotificationManager.Instance.ShowTip("event", false, "未挂载创建房间面板");
            return;
        }
        venueCreatePanel.OpenForVenue(_eventId);
    }

    private void SeatSelected() {
        if (_selectedReady.Count != 4) {
            NotificationManager.Instance.ShowTip("event", false, "请恰好选择 4 名准备中的玩家");
            return;
        }
        EventNetworkManager.Instance.SeatTable(_eventId, _selectedReady.ToArray(), "guobiao");
    }

    private void AddEmptyRow(Transform parent, string text) {
        if (parent == null || emptyRowTemplate == null) return;
        var go = Instantiate(emptyRowTemplate, parent);
        go.SetActive(true);
        var label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = text;
    }

    private void AddActionRow(Transform parent, string text, string action, UnityEngine.Events.UnityAction onClick) {
        if (parent == null || actionRowTemplate == null) return;
        var go = Instantiate(actionRowTemplate, parent);
        go.SetActive(true);
        var label = go.transform.Find("Label")?.GetComponent<TMP_Text>();
        if (label == null) label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = text;
        var btn = go.transform.Find("Act")?.GetComponent<Button>() ?? go.GetComponentInChildren<Button>(true);
        var btnLabel = btn != null ? btn.GetComponentInChildren<TMP_Text>(true) : null;
        if (btnLabel != null) btnLabel.text = action;
        if (btn != null) {
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(onClick);
        }
    }

    private static void ClearSpawned(Transform parent) {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--) {
            GameObject child = parent.GetChild(i).gameObject;
            if (child.name.EndsWith("Example") || child.name.EndsWith("Template")) continue;
            Destroy(child);
        }
    }
}
