#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

/// <summary>Opt-in MainScene validation against the local game service.</summary>
public static class SichuanDuplicateLobbyRuntimeValidation {
    static string folder;
    static readonly List<string> checks = new List<string>();
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure() {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--verify-sichuan-eight-lobby");
        folder = index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null;
#if UNITY_EDITOR
        if (folder == null) folder = UnityEditor.SessionState.GetString("SichuanEightLobbyValidationOutput", "");
        UnityEditor.SessionState.EraseString("SichuanEightLobbyValidationOutput");
        if (string.IsNullOrEmpty(folder)) folder = null;
#endif
        if (folder == null) return;
        Directory.CreateDirectory(folder);
        ConfigManager.gameUrl = "ws://localhost:8081/game";
        ConfigManager.chatUrl = "ws://localhost:8083/chat";
        ConfigManager.webApiUrl = "http://localhost:3000";
        Application.runInBackground = true;
        File.WriteAllText(Path.Combine(folder, "bootstrap.json"), "{\"local_endpoints\":true}");
    }

    static T Field<T>(object target, string key) => (T)target.GetType().GetField(key, Flags).GetValue(target);
    static void Check(bool ok, string name) {
        if (!ok) throw new InvalidOperationException(name);
        checks.Add(name);
    }

    public static string CheckCurrentRoom(string phase, bool screenshot = true) {
        if (folder == null) throw new InvalidOperationException("Validation flag is required");
        var panel = RoomPanel.Instance;
        var room = Field<RoomInfo>(panel, "lastRoomInfo");
        var view = Field<SichuanDuplicateLobbyView>(panel, "duplicateLobbyView");
        Check(room?.duplicate_lobby?.version == 1, phase + ": authoritative eight-player room");
        Check(view && view.gameObject.activeInHierarchy, phase + ": baked lobby is visible");
        var seats = Field<SichuanDuplicateLobbyView.Seat[]>(view, "seats");
        var waiters = Field<SichuanDuplicateLobbyView.Waiter[]>(view, "waiters");
        Check(seats.Length == 8 && waiters.Length == 8, phase + ": eight seats and eight waiting positions");
        Check(room.player_list.Length <= 8, phase + ": total room capacity");
        Check(room.seat_list.Count(uid => uid >= 0) + room.duplicate_lobby.waiting.Length == room.player_list.Length,
            phase + ": waiting members are counted once");
        for (int index = 0; index < 8; index++) {
            string wind = new[] { "东", "南", "西", "北" }[index % 4];
            Check(seats[index].wind.text.StartsWith(wind), phase + ": fixed compass " + index);
            bool expectedBlue = index < 4 ? index % 2 == 1 : index % 2 == 0;
            Check(seats[index].wind.text.Contains(expectedBlue ? "蓝队" : "红队"), phase + ": team identity " + index);
        }
        Check(view.GetComponentsInChildren<TMP_Text>(true).All(t => (t.fontStyle & FontStyles.Bold) == 0),
            phase + ": normal Chinese font weight");
        foreach (string name in new[] { "ClosedTitle", "OpenTitle" }) {
            var title = view.transform.Find(name).GetComponent<TMP_Text>();
            title.ForceMeshUpdate();
            Check(title.textInfo.characterCount == 2, phase + ": visible table title " + name);
        }
        for (int index = 1; index <= 4; index++)
            Check(!Field<PlayerRoomPanel>(panel, "playerPanel" + index).gameObject.activeSelf,
                phase + ": original four-player card hidden " + index);
        File.WriteAllText(Path.Combine(folder, "runtime-checks.json"), JsonConvert.SerializeObject(checks, Formatting.Indented));
        if (screenshot) ScreenCapture.CaptureScreenshot(Path.Combine(folder, Path.GetFileName(phase) + ".png"));
        return JsonConvert.SerializeObject(new { phase, checks = checks.Count, roomId = room.room_id,
            members = room.player_list.Length, waiting = room.duplicate_lobby.waiting.Length,
            revision = room.roster_revision, host = room.host_user_id });
    }
}
#endif
