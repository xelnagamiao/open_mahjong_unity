using System.Linq;
using UnityEngine;
using TMPro;

public partial class RoomPanel {
    private readonly TMP_Dropdown[] teamSelectors = new TMP_Dropdown[4];
    private int? DoublesRevision => lastRoomInfo != null && (SichuanLobby.IsTeamMode(lastRoomInfo.sub_rule) || WashizuDoubles.IsEnabled(lastRoomInfo.sub_rule))
        ? lastRoomInfo.roster_revision : (int?)null;

    public static bool DoublesTeamsComplete(RoomInfo room) => room?.team_by_seat?.Length == 4
        && room.team_by_seat.Count(t => t == 0) == 2 && room.team_by_seat.Count(t => t == 1) == 2
        && room.seat_list?.Length == 4 && room.seat_list.All(uid => uid >= 0)
        && (!WashizuDoubles.IsEnabled(room.sub_rule) || (WashizuDoubles.ValidRoles(room.team_role_by_seat)
            && room.team_by_seat.SequenceEqual(room.team_role_by_seat.Select(role => role / 2))));

    private void RefreshDoublesTeams(RoomInfo room, PlayerRoomPanel[] panels, int[] seats, bool isHost) {
        bool captain = WashizuDoubles.IsEnabled(room.sub_rule);
        bool doubles = SichuanLobby.IsTeamMode(room.sub_rule) || captain;
        if (!doubles) { HideDoublesTeams(); return; }
        for (int i = 0; i < panels.Length; i++) {
            if (!teamSelectors[i]) {
                // Reuse the existing dropdown's font, sprites and navigation. Card/footer geometry stays intact.
                teamSelectors[i] = Instantiate(botSpeedDropdown, panels[i].transform);
                var selector = teamSelectors[i];
                selector.name = "DoublesTeam";
                selector.onValueChanged = new TMP_Dropdown.DropdownEvent();
                selector.ClearOptions();
                var rect = (RectTransform)selector.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 0);
                rect.anchoredPosition = new Vector2(0, 170);
                rect.sizeDelta = new Vector2(280, 54);
                selector.captionText.fontSize = 28;
                selector.template.pivot = new Vector2(.5f, 0);
                selector.template.anchorMin = new Vector2(0, 1);
                selector.template.anchorMax = Vector2.one;
                selector.template.anchoredPosition = Vector2.zero;
                int seat = i;
                selector.onValueChanged.AddListener(value => ChangeTeam(seat, value));
            }
            int team = room.team_by_seat != null && i < room.team_by_seat.Length ? room.team_by_seat[i] : -1;
            bool occupied = i < seats.Length && seats[i] >= 0;
            var dropdown = teamSelectors[i];
            bool paired = SichuanLobby.IsDuplicate(room.sub_rule);
            var options = captain ? WashizuDoubles.RoleLabels.ToList() : paired
                ? Enumerable.Range(0, 4).Select(s => new[] { "东", "南", "西", "北" }[s]
                    + " · " + (room.team_by_seat[s] == 0 ? "蓝队" : "红队")).ToList()
                : new System.Collections.Generic.List<string> { "未分队", "A 队", "B 队" };
            if (!dropdown.options.Select(o => o.text).SequenceEqual(options)) {
                dropdown.Hide();
                dropdown.ClearOptions();
                dropdown.AddOptions(options);
            }
            dropdown.gameObject.SetActive(occupied);
            int selected = captain ? (room.team_role_by_seat != null && i < room.team_role_by_seat.Length ? room.team_role_by_seat[i] : 0) : paired ? i : team + 1;
            if (dropdown.value != selected) dropdown.Hide();
            dropdown.SetValueWithoutNotify(selected);
            dropdown.captionText.color = team == 0 ? new Color(.2f,.6f,1f) : team == 1 ? new Color(1f,.3f,.35f) : Color.white;
            dropdown.interactable = occupied && !room.is_game_running && (isHost || seats[i] == UserDataManager.Instance.UserId);
        }
        bool complete = DoublesTeamsComplete(room);
        startButton.interactable &= complete;
        if (readyButton) readyButton.interactable = !isHost && !room.is_game_running && complete;
    }

    private void ChangeTeam(int seat, int value) {
        if (lastRoomInfo == null) return;
        var room = lastRoomInfo;
        // Keep the authoritative selection until the server accepts and broadcasts a new revision.
        bool paired = SichuanLobby.IsDuplicate(room.sub_rule);
        bool captain = WashizuDoubles.IsEnabled(room.sub_rule);
        teamSelectors[seat].SetValueWithoutNotify(captain ? room.team_role_by_seat[seat] : paired ? seat : room.team_by_seat[seat] + 1);
        if (room.is_game_running) return;
        if (captain) RoomNetworkManager.Instance.SetCaptainRole(room.room_id, room.roster_revision, seat, value);
        else if (paired) RoomNetworkManager.Instance.MoveDuplicateSeat(room.room_id, room.roster_revision, seat, value);
        else RoomNetworkManager.Instance.SetTeam(room.room_id, room.roster_revision, seat, value - 1);
    }

    private void HideDoublesTeams() {
        foreach (var selector in teamSelectors) if (selector) selector.gameObject.SetActive(false);
    }
}
