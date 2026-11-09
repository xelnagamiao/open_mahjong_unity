#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Opt-in MainScene regression using a saved Washizu doubles record. Leaves its replay interactive.</summary>
public sealed class WashizuTeammateHandValidation : MonoBehaviour {
    static string folder;
    readonly List<string> checks = new List<string>();
    readonly List<string> errors = new List<string>();
    string scenario;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure() {
        folder = null;
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--verify-washizu-teammate");
        if (index < 0 || index + 1 >= args.Length) return;
        folder = Path.GetFullPath(args[index + 1]);
        ConfigManager.BuildForSteam = false;
        Application.runInBackground = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch() {
        if (folder == null) return;
        if (NetworkManager.Instance) NetworkManager.Instance.enabled = false;
        foreach (var chat in Resources.FindObjectsOfTypeAll<ChatManager>()) chat.enabled = false;
        new GameObject(nameof(WashizuTeammateHandValidation)).AddComponent<WashizuTeammateHandValidation>();
    }

    void Check(bool passed, string message) {
        if (!passed) throw new InvalidOperationException(scenario + ": " + message);
        checks.Add(scenario + ": " + message);
    }

    IEnumerator Start() {
        yield return null;
        var stack = new Stack<IEnumerator>(); stack.Push(Run());
        while (stack.Count > 0) {
            bool next; object current = null;
            try { next = stack.Peek().MoveNext(); if (next) current = stack.Peek().Current; }
            catch (Exception error) { errors.Add(error.ToString()); break; }
            if (!next) stack.Pop();
            else if (current is IEnumerator child) stack.Push(child);
            else yield return current;
        }
        File.WriteAllText(Path.Combine(folder, "runtime-results.json"), JsonConvert.SerializeObject(new {
            success = errors.Count == 0, checks, errors
        }, Formatting.Indented));
        Debug.Log("Washizu teammate hand validation: " + checks.Count + " checks, " + errors.Count + " failures");
    }

    IEnumerator Idle() {
        yield return null;
        float until = Time.realtimeSinceStartup + 20;
        while ((GameCanvas.Instance.IsChangeHandCardProcessing || Game3DManager.Instance.HasPendingRecordTableAnimations)
            && Time.realtimeSinceStartup < until) yield return null;
        yield return null;
        Check(Time.realtimeSinceStartup < until, "table animation completed");
    }

    void CheckTable(bool visible, int viewer) {
        var replay = GameRecordManager.Instance;
        var teams = (JArray)replay.gameRecord.gameTitle["team_by_player_index"];
        foreach (string seat in new[] { "left", "top", "right" }) {
            var player = replay.recordPlayer_to_info[seat];
            bool ally = (int)teams[player.originalPlayerIndex] == (int)teams[viewer];
            Check(replay.CanSeeWashizuTeammateHand(seat) == (visible && ally), seat + " follows current viewer's team");
            var panel = Game3DManager.Instance.GetPosPanel(seat);
            var physical = panel.cardsPosition.GetComponentsInChildren<Tile3D>();
            int transparent = player.tileList.Count(WashizuTile.IsTransparent);
            Check(physical.Length == player.tileList.Count, seat + " physical hand count preserved");
            Check(physical.Count(t => t.GetTileId() == 0) == player.tileList.Count - transparent,
                seat + " opaque 3D faces stay concealed");
            Check(physical.All(t => t.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount > 4),
                seat + " retains original 3D tile meshes");
            var reading = panel.ShowCardsPosition.GetComponentsInChildren<WashizuReadingSurface>()
                .Where(t => t.IsReadingSurface).ToArray();
            int expected = visible && ally ? player.tileList.Count : transparent;
            Check(reading.Length == expected, seat + " shows only permitted tabletop faces");
            Check(reading.All(t => t.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount == 4),
                seat + " tabletop faces remain flat");
            var actual = reading.Select(t => t.GetComponent<Tile3D>().GetTileId()).OrderBy(t => t);
            var allowed = player.tileList.Where(t => WashizuTile.IsTransparent(t) || visible && ally)
                .Select(WashizuTile.Face).OrderBy(t => t);
            Check(actual.SequenceEqual(allowed), seat + " tabletop faces match saved tiles");
        }
    }

    IEnumerator Run() {
        scenario = "startup";
        float until = Time.realtimeSinceStartup + 60;
        while (!WindowsManager.Instance && Time.realtimeSinceStartup < until) yield return null;
        Check(WindowsManager.Instance, "MainScene ready");
        Check(DetailedConfigRegistry.TryGet("riichi", out var definition)
            && definition.TryGetOption(WashizuDoubles.VisibleTeammateHandKey, out var option)
            && option.DefaultValue is bool value && !value, "setting defaults off");
        var fixture = JObject.Parse(File.ReadAllText(Path.Combine(folder, "preview.json")));
        var players = fixture["players"].ToObject<PlayerRecordInfo[]>();
        var record = (JObject)fixture["record"];
        foreach (bool visible in new[] { false, true }) {
            scenario = visible ? "enabled" : "disabled";
            record["game_title"]["detailed_config"][WashizuDoubles.VisibleTeammateHandKey] = visible;
            NormalGameStateManager.Instance?.ResetForExit();
            WindowsManager.Instance.SwitchWindow("game");
            yield return null;
            var replay = GameRecordManager.Instance;
            Check(replay, "replay manager initialized");
            replay.ResetForSessionEnd();
            replay.LoadRecord(record.ToString(), players);
            WindowsManager.Instance.SwitchWindow("recordscene");
            RecordSetting.Instance.SetShowCardsMode(false);
            int firstRound = replay.gameRecord.gameRound.rounds.Keys.Min();
            for (int viewer = 0; viewer < 4; viewer++) {
                scenario = (visible ? "enabled" : "disabled") + "/viewer" + viewer;
                replay.SwitchRecordPerspectiveToOriginalPlayerIndex(viewer);
                replay.JumpToSharePosition(firstRound, 0);
                yield return Idle();
                CheckTable(visible, viewer);
                replay.GotoAction(7);
                yield return Idle();
                CheckTable(visible, viewer);
            }
            replay.SwitchRecordPerspectiveToOriginalPlayerIndex(0);
            replay.JumpToSharePosition(firstRound, 0);
            yield return Idle();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, visible ? "enabled.png" : "disabled.png"));
            yield return new WaitForEndOfFrame();
        }
    }
}
#endif
