#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

/// <summary>Opt-in regression of real migrated records in MainScene. Leaves an interactive replay open.</summary>
public sealed class ReplayMatchResultValidation : MonoBehaviour {
    static string folder;
    readonly List<string> checks = new List<string>();
    readonly List<string> errors = new List<string>();
    string currentCase;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure() {
        folder = null;
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--verify-replay-results");
        if (index >= 0 && index + 1 < args.Length) folder = args[index + 1];
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(folder)) folder = UnityEditor.SessionState.GetString("ReplayMatchResultValidationFolder", "");
        UnityEditor.SessionState.EraseString("ReplayMatchResultValidationFolder");
#endif
        if (string.IsNullOrEmpty(folder)) { folder = null; return; }
        folder = Path.GetFullPath(folder);
        ConfigManager.BuildForSteam = false;
        Application.runInBackground = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch() {
        if (folder == null) return;
        if (NetworkManager.Instance != null) NetworkManager.Instance.enabled = false;
        foreach (var chat in Resources.FindObjectsOfTypeAll<ChatManager>()) chat.enabled = false;
        new GameObject(nameof(ReplayMatchResultValidation)).AddComponent<ReplayMatchResultValidation>();
    }
    void OnEnable() => Application.logMessageReceived += CaptureLog;
    void OnDisable() => Application.logMessageReceived -= CaptureLog;
    void CaptureLog(string message, string trace, LogType type) {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(currentCase + ": " + message + "\n" + trace);
    }
    void Check(bool pass, string name) {
        if (!pass) throw new InvalidOperationException(currentCase + ": " + name);
        checks.Add(currentCase + ": " + name);
    }
    IEnumerator Start() {
        yield return null;
        var stack = new Stack<IEnumerator>(); stack.Push(Run());
        while (stack.Count > 0) {
            object current = null; bool next;
            try { next = stack.Peek().MoveNext(); if (next) current = stack.Peek().Current; }
            catch (Exception error) { errors.Add(error.ToString()); break; }
            if (!next) stack.Pop();
            else if (current is IEnumerator child) stack.Push(child);
            else yield return current;
        }
        File.WriteAllText(Path.Combine(folder, "runtime-results.json"), JsonConvert.SerializeObject(new {
            success = errors.Count == 0, checks, errors
        }, Formatting.Indented));
        Debug.Log("Replay match result validation: " + checks.Count + " checks, " + errors.Count + " failures");
    }
    IEnumerator Run() {
        float deadline = Time.realtimeSinceStartup + 60;
        while (!WindowsManager.Instance && Time.realtimeSinceStartup < deadline) yield return null;
        Check(WindowsManager.Instance, "MainScene ready");
        foreach (string path in Directory.GetFiles(folder, "case-*.json").OrderBy(p => p)) {
            currentCase = Path.GetFileNameWithoutExtension(path);
            Debug.Log("Replay match result case: " + currentCase);
            var fixture = JObject.Parse(File.ReadAllText(path));
            NormalGameStateManager.Instance?.ResetForExit();
            Check(WindowsManager.Instance.TryPrepareRecordScene(out var replay, out var error), "normal replay entry: " + error);
            yield return null;
            replay.ResetForSessionEnd();
            replay.LoadRecord(fixture["record"].ToString(), fixture["players"].ToObject<PlayerRecordInfo[]>());
            WindowsManager.Instance.SwitchWindow("recordscene");
            int lastRound = replay.gameRecord.gameRound.rounds.Keys.Max();
            replay.GotoSelectRound(lastRound);
            yield return null;
            int end = replay.gameRecord.gameRound.rounds[lastRound].actionTicks.Count;
            replay.GotoAction(end);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Check(replay.IsRecordMatchResultVisible, "terminal seek opens result");
            var panel = EndGamePanel.Instance;
            Check(panel.IsRecordResultVisible, "existing end-game panel reused in read-only mode");
            Check(((RectTransform)panel.transform).rect.width >= 1000, "result fills table page");
            var labels = panel.GetComponentsInChildren<TMP_Text>();
            var expected = fixture["record"]["match_result"]["players"];
            Check(panel.GetComponentsInChildren<StaticCard>().Length == 0,
                "match summary does not render terminal hands");
            if (fixture["expected_final_hands"] is JObject expectedHands) {
                foreach (var seat in expectedHands.Properties()) {
                    var hand = replay.recordPlayer_to_info.Values.Single(player => player.originalPlayerIndex == int.Parse(seat.Name));
                    Check(hand.tileList.OrderBy(tile => tile).SequenceEqual(seat.Value.ToObject<int[]>().OrderBy(tile => tile)),
                        "exact terminal hand for seat " + seat.Name);
                }
            }
            if (replay.gameRecord.matchResult.sichuan_match == null) foreach (JObject player in expected) {
                string name = StreamerModeHelper.FormatGamestatePlayerName((string)player["username"], null, (int)player["user_id"]);
                var username = labels.Single(label => label.name == "Username" && (label.text == name || label.text.EndsWith(" · " + name, StringComparison.Ordinal)));
                string system = (string)player["rating_system"];
                string value = (string)player["rating_status"] == "missing" ? "-" : system == "elo"
                    ? ((double)player["elo_delta"]).ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture) + " Elo"
                    : ((double)player["rating_pt"]).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                if (player["team_id"] == null && player["competition_points"] == null)
                    Check(username.transform.parent.Find("PT").GetComponent<TMP_Text>().text == value,
                        "persisted rating " + player["original_player_index"]);
                if (player["team_imp_score"] != null)
                    Check(labels.Any(label => label.text.Contains(" IMP")), "team IMP shown");
            }
            yield return new WaitForSecondsRealtime(.15f);
            yield return new WaitForEndOfFrame();
            var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(folder, currentCase + ".png"), screenshot.EncodeToPNG());
            Destroy(screenshot);
            yield return null;
            replay.HideRecordMatchResult();
            Check(!replay.IsRecordMatchResultVisible && replay.currentRoundIndex == lastRound, "close stays in final round");
            replay.BackStep();
            Check(!replay.IsRecordMatchResultVisible && replay.currentNode < end, "rewind dismisses result");
            replay.GotoAction(end);
            replay.HideRecordMatchResult();
            replay.NextStep();
            Check(replay.IsRecordMatchResultVisible, "next reopens without looping");
            replay.GotoAction(end - 1);
            replay.AdvanceToNextAction();
            float endDeadline = Time.realtimeSinceStartup + 10;
            while (!replay.IsRecordMatchResultVisible && Time.realtimeSinceStartup < endDeadline) yield return null;
            Check(replay.IsRecordMatchResultVisible && replay.currentRoundIndex == lastRound, "playing final end opens match result");
        }
    }
}
#endif
