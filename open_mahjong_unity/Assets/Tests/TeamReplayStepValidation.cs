#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class TeamReplayStepValidation : MonoBehaviour {
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static string output;
    static string[] fixtures;
    readonly List<string> checks = new List<string>();
    readonly List<string> failures = new List<string>();
    GameRecordManager replay;
    GameSceneMouseInputController input;
    Vector2 table;
    string scenario;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure() {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--verify-team-replay-steps");
        if (index < 0 || index + 2 >= args.Length) return;
        output = Path.GetFullPath(args[index + 1]);
        fixtures = args.Skip(index + 2).TakeWhile(arg => !arg.StartsWith("-", StringComparison.Ordinal)).ToArray();
        ConfigManager.BuildForSteam = false;
        Application.runInBackground = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch() {
        if (output == null) return;
        new GameObject(nameof(TeamReplayStepValidation)).AddComponent<TeamReplayStepValidation>();
    }

    IEnumerator Start() {
        yield return null;
        if (NetworkManager.Instance) NetworkManager.Instance.enabled = false;
        foreach (string path in fixtures) {
            scenario = Path.GetFileName(path);
            var actions = new Stack<IEnumerator>();
            actions.Push(RunCase(path));
            while (actions.Count > 0) {
                object current = null;
                bool next;
                try {
                    next = actions.Peek().MoveNext();
                    if (next) current = actions.Peek().Current;
                } catch (Exception error) {
                    failures.Add(scenario + " " + error);
                    break;
                }
                if (!next) actions.Pop();
                else if (current is IEnumerator nested) actions.Push(nested);
                else yield return current;
            }
            if (input != null) input.enabled = true;
        }
        File.WriteAllText(output, JsonConvert.SerializeObject(new { success = checks.Count > 0 && failures.Count == 0,
            checks, failures }, Formatting.Indented));
        Application.Quit(checks.Count > 0 && failures.Count == 0 ? 0 : 1);
    }

    IEnumerator RunCase(string path) {
        float deadline = Time.realtimeSinceStartup + 30f;
        while (WindowsManager.Instance == null && Time.realtimeSinceStartup < deadline) yield return null;
        NormalGameStateManager.Instance?.ResetForExit();
        if (!WindowsManager.Instance.TryPrepareRecordScene(out replay, out string error)) throw new Exception(error);
        replay.ResetForSessionEnd();
        string json = File.ReadAllText(path);
        var document = JObject.Parse(json);
        var archive = (document["record"] ?? document) as JObject;
        bool paired = (string)archive["match_format"] == "paired_tables_2v2";
        if (archive["tables"] is JObject) replay.LoadMatchRecord(archive, paired ? "open" : "main");
        else replay.LoadRecord(json);
        WindowsManager.Instance.SwitchWindow("recordscene");
        var round = replay.gameRecord.gameRound.rounds.OrderBy(pair => pair.Key)
            .Select(pair => new { key = pair.Key, ticks = pair.Value.actionTicks })
            .FirstOrDefault(pair => FindMidgameSelfDraw(pair.ticks) >= 0);
        int hu = round == null ? -1 : FindMidgameSelfDraw(round.ticks);
        replay.GotoSelectRound(round?.key ?? 1, false);
        replay.GotoAction(hu >= 0 ? hu - 1 : 0);
        yield return null;
        input = GameSceneMouseInputController.Instance;
        input.enabled = false;
        Canvas.ForceUpdateCanvases();
        var hit = typeof(GameSceneMouseInputController).GetMethod("IsRecordScreenPointerBlocked", Private);
        table = (from x in new[] { .5f, .4f, .6f, .3f, .7f } from y in new[] { .5f, .6f, .4f, .7f }
                 let xy = new Vector2(Screen.width * x, Screen.height * y)
                 where !(bool)hit.Invoke(input, new object[] { xy }) select xy).First();

        if (hu < 0) {
            yield return OrdinarySteps();
            yield break;
        }
        Tap();
        yield return ExpectNode(hu, "draw");
        Tap();
        yield return ExpectNode(hu + 1, "early click before self draw");
        Tap();
        Tap();
        yield return ExpectNode(hu + 3, "two distinct early clicks advance the following draw and discard");
        yield return new WaitForSecondsRealtime(.8f);
        Check(replay.currentNode == hu + 3, "no delayed third advance");

        replay.GotoAction(hu);
        Tap();
        yield return ExpectNode(hu + 1, "self draw after seek");
        yield return new WaitForSecondsRealtime(2.2f);
        Check(replay.currentNode == hu + 1, "manual replay stays on midgame self draw");
        Tap();
        yield return ExpectNode(hu + 2, "one click after reveal advances");

        replay.GotoAction(hu);
        Tap();
        yield return ExpectNode(hu + 1, "self draw before rewind");
        Tap();
        int beforeBack = replay.currentNode;
        input.HandleRecordNavigationKey(KeyCode.LeftArrow, false, table);
        yield return new WaitForSecondsRealtime(1.7f);
        Check(replay.currentNode == beforeBack - 1 && !replay.HasPendingRecordStepInput,
            "back cancels pending clicks and the interrupted win cannot advance again");

        replay.GotoAction(hu);
        Tap();
        yield return ExpectNode(hu + 1, "self draw before seek");
        Tap();
        replay.GotoAction(hu - 1);
        yield return new WaitForSecondsRealtime(.85f);
        Check(replay.currentNode == hu - 1 && !replay.HasPendingRecordStepInput, "seek cancels pending click");

        Pointer(true, true);
        Pointer(false, true);
        Pointer(false, false);
        yield return new WaitForSecondsRealtime(.65f);
        Check(replay.currentNode == hu && !replay.HasPendingRecordStepInput, "short hold and release take exactly one step");

        if (paired) {
            replay.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(button => button.name == "IndependentProgress").onClick.Invoke();
            Check(replay.MatchSession.Mode == MatchRecordSession.PlaybackMode.History, "independent room cursors");
            replay.GotoSelectRound(round.key, false);
            replay.GotoAction(hu);
            Tap();
            yield return ExpectNode(hu + 1, "open room self draw");
            Tap();
            replay.SwitchMatchTable("closed");
            int switchedNode = replay.currentNode;
            yield return new WaitForSecondsRealtime(.85f);
            Check(replay.MatchSession.ActiveTable == "closed" && replay.currentNode == switchedNode
                && !replay.HasPendingRecordStepInput, "switch cancels the open room's pending input");
        }
    }

    IEnumerator OrdinarySteps() {
        var ticks = replay.gameRecord.gameRound.rounds[replay.currentRoundIndex].actionTicks;
        int firstVisible = ticks.FindIndex(t => t[0] != "reset");
        Tap();
        yield return ExpectNode(firstVisible + 1, "first visible action includes any leading reset");
        int draw = ticks.FindIndex(t => t[0] == "d");
        if (draw < 0 || draw + 2 >= ticks.Count || ticks[draw + 1][0] != "c")
            throw new Exception("fixture requires a draw followed by a discard");
        replay.GotoAction(draw);
        Tap();
        yield return ExpectNode(draw + 1, "draw before same-seat discard");
        Tap();
        Tap();
        yield return ExpectNode(draw + 3, "two early clicks advance two recorded actions");
        yield return new WaitForSecondsRealtime(.7f);
        Check(replay.currentNode == draw + 3, "no extra step after release");

        int riichiWin = ticks.FindIndex(t => t[0] == "hu_riichi");
        if (riichiWin >= 0) {
            replay.GotoAction(riichiWin);
            Tap();
            Tap();
            yield return ExpectNode(riichiWin + 1, "win opens its own presentation");
            float until = Time.realtimeSinceStartup + 10f;
            while (EndResultPanel.Instance?.IsAwaitingRecordResultConfirm != true && Time.realtimeSinceStartup < until)
                yield return null;
            yield return null;
            Check(EndResultPanel.Instance?.IsAwaitingRecordResultConfirm == true && replay.currentNode == riichiWin + 1
                && !replay.HasPendingRecordStepInput, "confirmation panel discards buffered next step");
        }
    }

    static int FindMidgameSelfDraw(List<List<string>> ticks) {
        for (int i = 1; i + 2 < ticks.Count; i++)
            if (ticks[i][0] == "hu_self" && ticks[i-1][0] == "d" && ticks[i+1][0] == "d" && ticks[i+2][0] == "c") return i;
        return -1;
    }

    static readonly MethodInfo PointerHandler = typeof(GameSceneMouseInputController).GetMethod("HandleRecordScreenPointer", Private);
    void Pointer(bool down, bool held) => PointerHandler.Invoke(input, new object[] { table, down, held });
    void Tap() { Pointer(true, true); Pointer(false, false); }
    IEnumerator ExpectNode(int expected, string label) {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (replay.currentNode < expected && Time.realtimeSinceStartup < deadline) yield return null;
        Check(replay.currentNode == expected, label + ", expected " + expected + ", observed " + replay.currentNode);
    }

    void Check(bool pass, string label) {
        if (!pass) throw new InvalidOperationException(label);
        checks.Add(scenario + " " + label);
    }
}
#endif
