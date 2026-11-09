#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

/// <summary>Opt-in regression of the real MainScene hints using a full Duocai replay.</summary>
public sealed class PopupRefreshRuntimeValidation : MonoBehaviour {
    static string folder;
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly List<string> checks = new List<string>();
    readonly List<string> errors = new List<string>();
    TipsContainer view;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure() {
        folder = null;
        var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-popup-layout-validation");
        if (index >= 0 && index + 1 < args.Length) folder = args[index + 1];
#if UNITY_EDITOR
        if (folder == null) folder = UnityEditor.SessionState.GetString("PopupLayoutValidationFolder", "");
#endif
        if (string.IsNullOrEmpty(folder)) return;
        ConfigManager.BuildForSteam = false; Application.runInBackground = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch() {
        if (string.IsNullOrEmpty(folder)) return;
        NetworkManager.Instance.enabled = false;
        foreach (var chat in Resources.FindObjectsOfTypeAll<ChatManager>()) chat.enabled = false;
        new GameObject(nameof(PopupRefreshRuntimeValidation)).AddComponent<PopupRefreshRuntimeValidation>();
    }
    void Check(bool passed, string label) { if (!passed) throw new InvalidOperationException(label); checks.Add(label); }
    void Log(string message, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error) errors.Add(message); }
    IEnumerator Start() {
        Directory.CreateDirectory(folder);
        Application.logMessageReceived += Log;
        var stack = new Stack<IEnumerator>(); stack.Push(Run()); Exception failure = null;
        while (stack.Count > 0) {
            bool more = false; object current = null;
            try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; }
            catch (Exception exception) { failure = exception; break; }
            if (!more) stack.Pop(); else if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
        Application.logMessageReceived -= Log;
        File.WriteAllText(Path.Combine(folder, "runtime-results.json"), JsonConvert.SerializeObject(new {
            success = failure == null && errors.Count == 0, count = checks.Count, checks, errors, exception = failure?.ToString(),
            width = Screen.width, height = Screen.height, frame = Time.frameCount
        }, Formatting.Indented));
    }
    RectTransform[] Cells() {
        var tiles = (GameObject)typeof(TipsContainer).GetField("TileContainer", Hidden).GetValue(view);
        var fans = (GameObject)typeof(TipsContainer).GetField("FanContainer", Hidden).GetValue(view);
        return tiles.transform.Cast<Transform>().Concat(fans.transform.Cast<Transform>()).Cast<RectTransform>().ToArray();
    }
    Vector3[] Corners(RectTransform[] cells) {
        return cells.SelectMany(cell => { var points = new Vector3[4]; cell.GetWorldCorners(points); return points; }).ToArray();
    }
    bool HasPending() => view.GetComponentsInChildren<TMP_Text>().Any(text => text.text == "…");
    IEnumerator Settled() {
        float deadline = Time.realtimeSinceStartup + 40;
        do { yield return null; if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Hint calculation did not settle"); } while (HasPending());
        Canvas.ForceUpdateCanvases(); yield return null;
    }
    IEnumerator Run() {
        yield return null;
        Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainScene", "uses actual MainScene");
        foreach (var result in new[] { UpwardPopupLayoutValidation.RunHints(), UpwardPopupLayoutValidation.RunActions(), UpwardPopupLayoutValidation.RunBalancedRows() })
            Check(JObject.FromObject(result).Value<int>("count") > 0, "popup geometry suite passed");
        var fixture = JObject.Parse(File.ReadAllText(Path.Combine(folder, "fixture.json")));
        var players = fixture["players"].ToObject<PlayerRecordInfo[]>();
        typeof(UserDataManager).GetProperty("UserId").SetValue(UserDataManager.Instance, players.First(p => p.original_player_index == 0).user_id);
        NormalGameStateManager.Instance.ResetForExit();
        WindowsManager.Instance.SwitchWindow("game"); yield return null;
        var record = GameRecordManager.Instance; record.ResetForSessionEnd();
        record.LoadRecord(fixture["record"].ToString(), players);
        WindowsManager.Instance.SwitchWindow("recordscene");
        record.SwitchRecordPerspectiveToOriginalPlayerIndex(0); record.JumpToSharePosition(1, 0);
        yield return new WaitForSecondsRealtime(.5f);
        var player = record.recordPlayer_to_info["self"];
        var hand = new List<int>(player.tileList); int cut = hand[hand.Count - 1]; hand.RemoveAt(hand.Count - 1);
        var ctx = (RecordTipsContext)typeof(GameRecordManager).GetMethod("BuildRecordTipsContext", Hidden).Invoke(record, new object[] { player });
        RuleRegistry.TryResolve(ctx.RoomRule, ctx.SubRule, out var manifest);
        var waits = RecordChongHintCalculator.ComputeWaitingTilesForPlayer(player, ctx.RoomRule, ctx.DetailedConfig, ctx.SubRule);
        float until = Time.realtimeSinceStartup + 40;
        while (waits.Count == 0 && Time.realtimeSinceStartup < until) {
            yield return null; waits = RecordChongHintCalculator.ComputeWaitingTilesForPlayer(player, ctx.RoomRule, ctx.DetailedConfig, ctx.SubRule);
        }
        Check(waits.Count > 1, "real Duocai hand has multiple waiting tiles");
        view = TipsContainer.Instance;
        view.SetTipsWithRecordContext(ctx, hand, waits.ToList(), cut); view.ShowTips(); yield return Settled();
        var layout = view.GetComponent<UpwardPopupLayout>();
        if (layout.HasMultiplePages) { layout.SetPage(1); yield return Settled(); }
        var cells = Cells(); var positions = Corners(cells); int page = layout.PageIndex;
        var caches = (Dictionary<int, DuocaiHintsCache>)typeof(GameRecordManager).GetField("recordDuocaiHints", Hidden).GetValue(record);
        var original = fixture["record"] is JObject obj ? obj : JObject.Parse(fixture["record"].ToString());
        var hints = original["game_round"]["round_index_1"]["duocai_hints"][player.playerIndex.ToString()].ToObject<DuocaiHints>();
        for (int update = 0; update < 6; update++) {
            // Public table information changes invalidate the asynchronous score key.
            // Reproduce the same refresh path used after another player's discard.
            hints.local_request["context"]["remaining"] = 90 - update;
            hints.local_request["context"]["visible_tiles"] = new JArray(Enumerable.Repeat(19, update % 4 + 1));
            caches[player.playerIndex].Accept(hints);
            view.SetTipsWithRecordContext(ctx, hand, waits.ToList(), cut);
            Check(Cells().SequenceEqual(cells), "refresh preserves tile and hint instances " + update);
            Check(layout.PageIndex == page, "refresh preserves current page " + update);
            until = Time.realtimeSinceStartup + 40;
            do {
                Canvas.ForceUpdateCanvases();
                Check(Corners(cells).Zip(positions, Vector3.Distance).All(distance => distance < .01f), "no pixel drift through asynchronous refresh " + update);
                yield return null;
                if (Time.realtimeSinceStartup > until) throw new TimeoutException("Refreshed Duocai score did not settle");
            } while (HasPending());
        }
        var fan = view.GetComponentsInChildren<TipsFanCount>().First();
        foreach (string label in new[] { "8番", "点和8番\n自摸10番" }) {
            fan.SetHint(WaitTileHint.Ron(label), 4, true, true); layout.Refresh();
            var stable = Corners(Cells());
            for (int remaining = 3; remaining >= 0; remaining--) {
                fan.SetHint(null, remaining, true, true); layout.Refresh();
                Check(Corners(Cells()).Zip(stable, Vector3.Distance).All(d => d < .01f), "pending score preserves two/three-row geometry " + remaining);
                Check(fan.GetComponentsInChildren<TMP_Text>().Any(t => t.text == "…"), "pending score never displays stale eligibility");
                fan.SetHint(WaitTileHint.Ron(label), remaining, true, true); layout.Refresh();
                Check(Corners(Cells()).Zip(stable, Vector3.Distance).All(d => d < .01f), "remaining count does not move geometry " + remaining);
                Check(fan.GetComponentsInChildren<TMP_Text>().Any(t => t.text == remaining + "枚"), "remaining count refreshes " + remaining);
            }
        }
        view.SetTipsWithRecordContext(ctx, hand, new List<int> { waits.First() }, cut); view.ShowTips(); yield return Settled();
        Check(view.GetComponentsInChildren<StaticCard>(true).Length == 1 && layout.PageIndex == 0, "changed wait set replaces old cells and resets page");
        view.SetTipsWithRecordContext(ctx, hand, waits.ToList(), cut); view.ShowTips(); yield return Settled();
        for (int i = 0; i < 3; i++) { view.HideTips(); yield return null; view.ShowTips(); yield return Settled(); }
        Check(view.GetComponentsInChildren<StaticCard>(true).Length == waits.Count, "repeated reopening preserves all waiting tiles");
        yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(folder, "duocai-hints.png"));
        yield return new WaitForEndOfFrame();
        view.HideTips();
        int node = record.currentNode; record.AdvanceToNextAction(); yield return new WaitForSecondsRealtime(1);
        Check(record.currentNode > node, "preview replay controls advance real actions");
        record.JumpToSharePosition(1, 0); yield return new WaitForSecondsRealtime(.5f);
        // Leave a playable replay and its genuine cut-preview hint for inspection.
        view.SetTipsWithRecordContext(ctx, hand, waits.ToList(), cut); view.ShowTips(); yield return Settled();
    }
}
#endif
