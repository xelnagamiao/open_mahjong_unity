using UnityEngine;
using Newtonsoft.Json;
using NativeWebSocket;
using System;
using System.Collections.Generic;

/// <summary>
/// 赛事/基地网络管理器。
/// </summary>
public class EventNetworkManager : MonoBehaviour {
    public static EventNetworkManager Instance { get; private set; }

    public List<EventListEntry> PublicEvents { get; private set; } = new List<EventListEntry>();
    public EventDetailInfo CurrentDetail { get; private set; }
    public RoomInfo[] VenueRooms { get; private set; }
    public RecordInfo[] EventRecords { get; private set; }

    public event Action OnPublicEventsUpdated;
    public event Action OnEventDetailUpdated;
    public event Action OnVenueRoomsUpdated;
    public event Action OnEventRecordsUpdated;
    public event Action<string, string, bool, string> OnOperationCompleted;
    public string VenueRoomsEventId { get; private set; }
    public string RecordsEventId { get; private set; }
    public int RecordsOffset { get; private set; }
    private sealed class RecordPageRequest {
        public string eventId;
        public int offset;
    }
    private readonly LinkedList<RecordPageRequest> recordRequests = new LinkedList<RecordPageRequest>();
    private RecordPageRequest latestRecordRequest;
    readonly Dictionary<string, Queue<string>> requests = new Dictionary<string, Queue<string>>();
    readonly Queue<string> roomRequests = new Queue<string>();
    WebSocket requestSocket;
    public void TrackRoomListRequest(string eventId) {
        CheckRequestSocket(); roomRequests.Enqueue(eventId);
    }
    void CheckRequestSocket() {
        var socket = NetworkManager.Instance?.GetWebSocket();
        if (socket == requestSocket) return;
        requestSocket = socket; requests.Clear(); roomRequests.Clear(); _publicListKinds.Clear();
        recordRequests.Clear(); latestRecordRequest = null;
    }
    string ConsumeRequest(string type) {
        return requests.TryGetValue(type, out var queue) && queue.Count > 0 ? queue.Dequeue() : null;
    }


    private string _lastDetailEventId;
    private string _latestPublicListKind;
    private readonly Queue<string> _publicListKinds = new Queue<string>();

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void HandleEventMessage(Response response) {
        if (response == null || string.IsNullOrEmpty(response.type)) return;
        if (response.type == "event/list_records") {
            if (recordRequests.First == null) return;
            var request = recordRequests.First.Value;
            recordRequests.RemoveFirst();
            if (request != latestRecordRequest) return;
            RecordsEventId = request.eventId;
            RecordsOffset = request.offset;
            OnOperationCompleted?.Invoke(response.type, request.eventId, response.success, response.message);
            if (response.success) {
                EventRecords = response.record_list ?? Array.Empty<RecordInfo>();
                OnEventRecordsUpdated?.Invoke();
            }
            return;
        }
        string responseEventId = ConsumeRequest(response.type);
        OnOperationCompleted?.Invoke(response.type, responseEventId, response.success, response.message);
        switch (response.type) {
            case "event/list_public":
                string requestedKind = _publicListKinds.Count > 0
                    ? _publicListKinds.Dequeue()
                    : _latestPublicListKind;
                if (!string.IsNullOrEmpty(requestedKind)
                    && !string.IsNullOrEmpty(_latestPublicListKind)
                    && requestedKind != _latestPublicListKind) {
                    break;
                }
                if (!response.success) break;
                PublicEvents = response.event_list != null
                    ? new List<EventListEntry>(response.event_list)
                    : new List<EventListEntry>();
                OnPublicEventsUpdated?.Invoke();
                break;
            case "event/get_detail":
                if (!response.success) {
                    NotificationManager.Instance.ShowTip("event", false, response.message);
                    break;
                }
                CurrentDetail = response.event_detail;
                OnEventDetailUpdated?.Invoke();
                break;
            case "event/register":
            case "event/cancel_register":
            case "event/ready":
            case "event/unready":
                NotificationManager.Instance.ShowTip("event", response.success, response.message);
                if (response.success && response.type == "event/register"
                    && CurrentDetail != null && CurrentDetail.event_id == responseEventId
                    && response.event_detail?.registration != null) {
                    CurrentDetail.registration = response.event_detail.registration;
                    OnEventDetailUpdated?.Invoke();
                }
                RefreshCurrentDetail();
                break;
            case "event/seated":
                if (response.success && response.room_info != null) {
                    RoomNetworkManager.Instance?.AcceptForcedRoomEntry(response.room_info);
                }
                break;
            case "event/review_registration":
            case "event/list_registrations":
                NotificationManager.Instance.ShowTip("event", response.success, response.message);
                break;
        }
    }

    public void HandleVenueRoomList(Response response) {
        VenueRooms = response != null && response.success ? response.room_list : null;
        OnVenueRoomsUpdated?.Invoke();
    }

    public bool TryConsumePendingRoomListEventId(out string eventId) {
        eventId = roomRequests.Count > 0 ? roomRequests.Dequeue() : null;
        VenueRoomsEventId = eventId;
        return !string.IsNullOrEmpty(eventId);
    }

    private static WebSocket _GetWs() {
        var ws = NetworkManager.Instance?.GetWebSocket();
        return (ws != null && ws.State == WebSocketState.Open) ? ws : null;
    }

    private async void _Send(object msg) {
        var payload = Newtonsoft.Json.Linq.JObject.FromObject(msg);
        string type = (string)payload["type"], id = (string)payload["event_id"];
        CheckRequestSocket();
        var ws = _GetWs();
        if (ws == null) { OnOperationCompleted?.Invoke(type,id,false,"连接已断开，请连接后重试"); return; }
        if (type != "room/get_room_list") {
            if (!requests.TryGetValue(type,out var queue)) requests[type] = queue = new Queue<string>();
            queue.Enqueue(id);
        }
        try { await ws.SendText(JsonConvert.SerializeObject(msg)); }
        catch (Exception) { OnOperationCompleted?.Invoke(type,id,false,"请求未确认，请刷新后核对结果"); }
    }

    public void ListPublicEvents(string kind) {
        string k = kind == "base" ? "base" : "event";
        CheckRequestSocket();
        _latestPublicListKind = k;
        if (_GetWs() != null) _publicListKinds.Enqueue(k);
        _Send(new { type = "event/list_public", kind = k });
    }

    public void GetEventDetail(string eventId) {
        if (!string.IsNullOrEmpty(eventId)) _lastDetailEventId = eventId;
        _Send(new { type = "event/get_detail", event_id = eventId });
    }

    public void RefreshCurrentDetail() {
        string eventId = !string.IsNullOrEmpty(_lastDetailEventId)
            ? _lastDetailEventId
            : CurrentDetail?.event_id;
        if (!string.IsNullOrEmpty(eventId)) GetEventDetail(eventId);
    }

    public void Register(string eventId, string contact, string remark, string joinCode) {
        _Send(new {
            type = "event/register",
            event_id = eventId,
            contact,
            remark,
            join_code = joinCode
        });
    }

    public void CancelRegister(string eventId) {
        _Send(new { type = "event/cancel_register", event_id = eventId });
    }

    public void Ready(string eventId) {
        _Send(new { type = "event/ready", event_id = eventId });
    }

    public void Unready(string eventId) {
        _Send(new { type = "event/unready", event_id = eventId });
    }

    public void ListVenueRooms(string eventId) {
        TrackRoomListRequest(eventId);
        _Send(new { type = "room/get_room_list", show_tip = false, event_id = eventId });
    }

    public async void ListEventRecords(string eventId, int offset = 0) {
        CheckRequestSocket();
        var request = new RecordPageRequest { eventId = eventId, offset = Math.Max(0, offset) };
        latestRecordRequest = request;
        LinkedListNode<RecordPageRequest> pending = null;
        try {
            var socket = _GetWs();
            if (socket == null) throw new InvalidOperationException("连接已断开，请连接后重试");
            pending = recordRequests.AddLast(request);
            await socket.SendText(JsonConvert.SerializeObject(new {
                type = "event/list_records", event_id = eventId,
                limit = DataNetworkManager.RecordListPageSize, offset = request.offset
            }));
        } catch (Exception e) {
            if (pending?.List != null) recordRequests.Remove(pending);
            if (request == latestRecordRequest)
                OnOperationCompleted?.Invoke("event/list_records", eventId, false, e.Message);
        }
    }
}
