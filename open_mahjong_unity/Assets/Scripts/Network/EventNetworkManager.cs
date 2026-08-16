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

    public List<EventListEntry> ActiveEvents { get; private set; } = new List<EventListEntry>();
    public List<EventListEntry> PublicEvents { get; private set; } = new List<EventListEntry>();
    public EventDetailInfo CurrentDetail { get; private set; }
    public RoomInfo[] VenueRooms { get; private set; }
    public EventReadyPlayer[] ReadyPlayers { get; private set; }
    public RecordInfo[] EventRecords { get; private set; }

    public event Action OnActiveEventsUpdated;
    public event Action OnPublicEventsUpdated;
    public event Action OnEventDetailUpdated;
    public event Action OnVenueRoomsUpdated;
    public event Action OnReadyPlayersUpdated;
    public event Action OnEventRecordsUpdated;

    private string _pendingRoomListEventId;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void HandleEventMessage(Response response) {
        if (response == null || string.IsNullOrEmpty(response.type)) return;
        switch (response.type) {
            case "event/list_my_active":
                ActiveEvents = response.event_list != null
                    ? new List<EventListEntry>(response.event_list)
                    : new List<EventListEntry>();
                OnActiveEventsUpdated?.Invoke();
                break;
            case "event/list_public":
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
                if (!string.IsNullOrEmpty(CurrentDetail?.event_id)) GetEventDetail(CurrentDetail.event_id);
                break;
            case "event/list_ready":
                ReadyPlayers = response.ready_players;
                OnReadyPlayersUpdated?.Invoke();
                break;
            case "event/create_empty_room":
                NotificationManager.Instance.ShowTip("event", response.success, response.message);
                if (response.success && CurrentDetail != null) ListVenueRooms(CurrentDetail.event_id);
                break;
            case "event/seat_table":
                NotificationManager.Instance.ShowTip("event", response.success, response.message);
                if (response.success && CurrentDetail != null) {
                    ListVenueRooms(CurrentDetail.event_id);
                    ListReadyPlayers(CurrentDetail.event_id);
                }
                break;
            case "event/seated":
                if (response.success && response.room_info != null) {
                    RoomNetworkManager.Instance?.AcceptForcedRoomEntry(response.room_info);
                }
                break;
            case "event/list_records":
                EventRecords = response.record_list;
                OnEventRecordsUpdated?.Invoke();
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
        eventId = _pendingRoomListEventId;
        _pendingRoomListEventId = null;
        return !string.IsNullOrEmpty(eventId);
    }

    private static WebSocket _GetWs() {
        var ws = NetworkManager.Instance.GetWebSocket();
        return (ws != null && ws.State == WebSocketState.Open) ? ws : null;
    }

    private static async void _Send(object msg) {
        var ws = _GetWs();
        if (ws == null) return;
        try {
            await ws.SendText(JsonConvert.SerializeObject(msg));
        } catch (Exception e) {
            Debug.LogError($"[EventNetworkManager] 发送失败: {e.Message}");
        }
    }

    public void ListMyActiveEvents() {
        _Send(new { type = "event/list_my_active" });
    }

    public void ListPublicEvents(string kind) {
        _Send(new { type = "event/list_public", kind });
    }

    public void GetEventDetail(string eventId) {
        _Send(new { type = "event/get_detail", event_id = eventId });
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

    public void ListReadyPlayers(string eventId) {
        _Send(new { type = "event/list_ready", event_id = eventId });
    }

    public void ListVenueRooms(string eventId) {
        _pendingRoomListEventId = eventId;
        _Send(new { type = "room/get_room_list", show_tip = false, event_id = eventId });
    }

    public void CreateEmptyRoom(string eventId, string roomRule, string roomName) {
        _Send(new {
            type = "event/create_empty_room",
            event_id = eventId,
            room_rule = roomRule,
            room_config = new { room_name = string.IsNullOrEmpty(roomName) ? "赛事房间" : roomName + "桌" }
        });
    }

    public void SeatTable(string eventId, int[] userIds, string roomRule) {
        _Send(new {
            type = "event/seat_table",
            event_id = eventId,
            user_ids = userIds,
            room_rule = roomRule
        });
    }

    public void ListEventRecords(string eventId) {
        _Send(new { type = "event/list_records", event_id = eventId, limit = 50, offset = 0 });
    }
}
