using System.Collections;
using UnityEngine;

/// <summary>赛事详情的网络响应与场馆创建生命周期；视图由场景内 EventVenuePanel 统一维护。</summary>
public class EventDetailPanel : MonoBehaviour {
    [SerializeField] private Salasasa.Events.EventVenuePanel redesignedPanel;
    [SerializeField] private CreatePanel venueCreatePanel;
    public static EventDetailPanel Instance { get; private set; }
    private string _eventId;
    private string _kind = "event";
    private EventDetailInfo _detail;
    private Coroutine _venueFade;
    public bool HasOpenVenue => !string.IsNullOrEmpty(_eventId);
    public void RequestVenueCreate() => OpenVenueCreatePanel();

    private void Awake() {
        Instance = this;
    }

    public void Open(string eventId, string kind) {
        _eventId = eventId;
        _kind = string.IsNullOrEmpty(kind) ? "event" : kind;
        _detail = null;
        HideVenueCreate();
        redesignedPanel.Open(eventId, _kind);
    }

    public void RestoreVisiblePage() {
        if (HasOpenVenue && gameObject.activeSelf) redesignedPanel.RestoreVisiblePage();
    }

    public void RefreshVisiblePage() {
        if (HasOpenVenue && gameObject.activeSelf) redesignedPanel.RefreshVisiblePage();
    }

    public void ShowRoomsAfterCreate() {
        HideVenueCreate();
        redesignedPanel.ShowPage(2);
    }

    public void OnSpectatorList(SpectatorInfo[] list) {
        if (isActiveAndEnabled) redesignedPanel.ApplySpectators(list);
    }

    private void OnDetail() {
        var detail = EventNetworkManager.Instance != null ? EventNetworkManager.Instance.CurrentDetail : null;
        if (detail != null && detail.event_id != _eventId) return;
        _detail = detail;
        redesignedPanel.ApplyDetail(_detail);
    }

    private void OnRooms() {
        var network = EventNetworkManager.Instance;
        if (network.VenueRoomsEventId == _eventId) redesignedPanel.ApplyRooms(network.VenueRooms);
    }

    private void OnRecords() {
        var network = EventNetworkManager.Instance;
        if (network.RecordsEventId == _eventId) redesignedPanel.ApplyRecords(network.EventRecords);
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

    public void HideVenueCreate() {
        if (_venueFade != null) {
            StopCoroutine(_venueFade);
            _venueFade = null;
        }
        if (venueCreatePanel != null && venueCreatePanel.IsVenueMode) {
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
        venueCreatePanel.transform.SetAsLastSibling();
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

    private static bool IsLoggedIn() {
        return UserDataManager.Instance.UserId > 0 && !UserDataManager.Instance.IsTourist;
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
}
