using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SichuanDuplicateLobbyView : MonoBehaviour {
    [Serializable] public sealed class Seat {
        public TMP_Text wind, player, status;
        public UnityEngine.UI.Button choose, join, stand, remove;
        public UnityEngine.UI.Image teamHeader;
        public TMP_Dropdown bot;
        public UnityEngine.UI.Outline selection;
    }
    [Serializable] public sealed class Waiter {
        public TMP_Text name;
        public UnityEngine.UI.Button choose, remove;
        public UnityEngine.UI.Outline selection;
    }
    [SerializeField] private Seat[] seats = Array.Empty<Seat>();
    [SerializeField] private Waiter[] waiters = Array.Empty<Waiter>();
    [SerializeField] private TMP_Text waitingTitle;
    [SerializeField] private TMP_Dropdown botSpeed;
    private RoomInfo room;
    private int sourceSeat = -1, waitingUser = -1;
    private bool host => room != null && room.host_user_id == UserDataManager.Instance.UserId;
    private bool editable => room?.duplicate_lobby?.can_edit == true;

    void Awake() {
        for (int i = 0; i < seats.Length; i++) {
            int index = i;
            seats[i].choose.onClick.AddListener(() => ChooseSeat(index));
            seats[i].join.onClick.AddListener(() => ChooseSeat(index));
            seats[i].stand.onClick.AddListener(() => Send("stand", user: room.seat_list[index]));
            seats[i].remove.onClick.AddListener(() => Send("remove", target: index));
            seats[i].bot.onValueChanged.AddListener(value => {
                seats[index].bot.SetValueWithoutNotify(0);
                if (value > 0) Send("add_bot", target: index, botKind: value == 1 ? 0 : 2);
            });
        }
        for (int i = 0; i < waiters.Length; i++) {
            int index = i;
            waiters[i].choose.onClick.AddListener(() => ChooseWaiter(index));
            waiters[i].remove.onClick.AddListener(() => Send("remove", user: room.duplicate_lobby.waiting[index]));
        }
        botSpeed.onValueChanged.AddListener(value => {
            if (!host || !editable) return;
            string[] speeds = { "instant", "fast", "medium", "slow" };
            RoomNetworkManager.Instance.SetBotSpeed(room.room_id, speeds[value]);
        });
    }

    public static bool CanStart(RoomInfo info) {
        return info?.duplicate_lobby?.can_edit == true && (info.duplicate_status == "waiting"
                || (info.duplicate_status == "interrupted" && string.IsNullOrEmpty(info.event_id)))
            && info.seat_list?.Length == 8 && info.seat_list.All(uid => uid >= 0)
            && (info.duplicate_lobby.waiting?.Length ?? 0) == 0
            && info.seat_list.Where(uid => uid > 10).All(uid => uid == info.host_user_id || (info.ready_list?.Contains(uid) ?? false));
    }

    public void Refresh(RoomInfo value) {
        if (room == null || room.roster_revision != value.roster_revision || !value.duplicate_lobby.can_edit)
            sourceSeat = waitingUser = -1;
        room = value;
        int self = UserDataManager.Instance.UserId;
        for (int i = 0; i < seats.Length; i++) {
            var widget = seats[i];
            int uid = room.seat_list[i];
            bool occupied = uid >= 0;
            widget.wind.text = new[] { "东", "南", "西", "北" }[i % 4]
                + " · " + (room.team_by_seat[i] == 0 ? "蓝队" : "红队");
            var teamColor = room.team_by_seat[i] == 0
                ? new Color32(45, 105, 165, 255) : new Color32(184, 55, 75, 255);
            widget.teamHeader.color = teamColor;
            widget.join.image.color = teamColor;
            widget.wind.color = Color.white;
            widget.player.text = occupied ? Name(uid) : "";
            widget.status.text = !occupied ? "" : uid == room.host_user_id ? "房主"
                : uid <= 10 ? "机器人" : room.ready_list?.Contains(uid) == true ? "已准备" : "";
            widget.choose.interactable = occupied && (editable || uid > 10);
            widget.join.gameObject.SetActive(!occupied);
            widget.join.interactable = editable;
            widget.stand.gameObject.SetActive(editable && occupied && uid > 10 && (host || uid == self));
            widget.remove.gameObject.SetActive(editable && host && occupied && uid != self);
            widget.bot.gameObject.SetActive(editable && host && !occupied);
            widget.bot.interactable = (room.player_list?.Length ?? 0) < 8;
        }
        int[] waiting = room.duplicate_lobby.waiting ?? Array.Empty<int>();
        waitingTitle.text = $"等候区 {waiting.Length}/8";
        for (int i = 0; i < waiters.Length; i++) {
            var widget = waiters[i];
            bool occupied = i < waiting.Length;
            widget.choose.gameObject.SetActive(occupied);
            if (!occupied) continue;
            int uid = waiting[i];
            widget.name.text = (uid == room.host_user_id ? "房主 · " : "") + Name(uid);
            widget.choose.interactable = true;
            widget.remove.gameObject.SetActive(editable && host && uid != self);
        }
        botSpeed.transform.parent.gameObject.SetActive(room.seat_list.Any(uid => uid >= 0 && uid <= 10));
        botSpeed.interactable = host && editable;
        botSpeed.SetValueWithoutNotify(room.bot_speed == "instant" ? 0 : room.bot_speed == "medium" ? 2 : room.bot_speed == "slow" ? 3 : 1);
        Selection();
    }

    string Name(int uid) {
        string value = room.player_settings != null && room.player_settings.TryGetValue(uid.ToString(), out var profile)
            ? profile.username : uid == 0 ? "麻雀罗伯特" : uid == 2 ? "牌效罗伯特" : $"玩家{uid}";
        return StreamerModeHelper.FormatRoomPlayerName(value, uid);
    }

    void ChooseSeat(int target) {
        int uid = room.seat_list[target];
        if (!editable) { if (uid > 10) ProfileOnClick.OpenPlayerInfo(uid); return; }
        if (uid < 0 || (host && sourceSeat >= 0 && sourceSeat != target)) {
            Send("seat", target, sourceSeat >= 0 ? sourceSeat : (int?)null,
                waitingUser > 10 ? waitingUser : UserDataManager.Instance.UserId);
        } else if (host || uid == UserDataManager.Instance.UserId) {
            sourceSeat = sourceSeat == target ? -1 : target;
            waitingUser = -1;
            Selection();
        } else if (uid > 10) ProfileOnClick.OpenPlayerInfo(uid);
    }

    void ChooseWaiter(int index) {
        int uid = room.duplicate_lobby.waiting[index];
        if (editable && (host || uid == UserDataManager.Instance.UserId)) {
            waitingUser = waitingUser == uid ? -1 : uid;
            sourceSeat = -1;
            Selection();
        } else ProfileOnClick.OpenPlayerInfo(uid);
    }

    void Selection() {
        for (int i = 0; i < seats.Length; i++) seats[i].selection.enabled = i == sourceSeat;
        var waiting = room.duplicate_lobby.waiting ?? Array.Empty<int>();
        for (int i = 0; i < waiters.Length; i++)
            waiters[i].selection.enabled = i < waiting.Length && waiting[i] == waitingUser;
    }

    void Send(string action, int? target = null, int? source = null, int? user = null, int? botKind = null) {
        if (!editable) return;
        RoomNetworkManager.Instance.DuplicateLobbyAction(room.room_id, room.roster_revision,
            action, target, source, user, botKind);
    }
}
