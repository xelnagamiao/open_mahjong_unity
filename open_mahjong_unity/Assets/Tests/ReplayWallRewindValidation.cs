using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Play the two-round fixture and check the replay wall during same-frame rewinds.
/// Pass -replay-wall-validation &lt;fixture&gt; and optionally -replay-wall-report &lt;file&gt;.
/// Add -replay-wall-preview to leave the interactive replay open after validation.
/// </summary>
public sealed class ReplayWallRewindValidation : MonoBehaviour {
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<string> checks = new List<string>();
    private string fixturePath;
    private string reportPath;
    private bool leaveOpen;
    private GameRecordManager record;
    private int openingNode;
    private int[][] openingHands;
    private int[] openingWall;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-replay-wall-validation");
        if (index < 0 || index + 1 >= args.Length) return;
        var host = new GameObject(nameof(ReplayWallRewindValidation));
        DontDestroyOnLoad(host);
        var validation = host.AddComponent<ReplayWallRewindValidation>();
        validation.fixturePath = args[index + 1];
        int report = Array.IndexOf(args, "-replay-wall-report");
        validation.reportPath = report >= 0 && report + 1 < args.Length ? args[report + 1] : null;
        validation.leaveOpen = args.Contains("-replay-wall-preview");
        validation.StartCoroutine(validation.Run());
    }

    private IEnumerator Run() {
        var stack = new Stack<IEnumerator>();
        stack.Push(Verify());
        Exception error = null;
        while (stack.Count > 0) {
            object current = null;
            bool more = false;
            try {
                more = stack.Peek().MoveNext();
                if (more) current = stack.Peek().Current;
            } catch (Exception exception) {
                error = exception;
                break;
            }
            if (!more) stack.Pop();
            else if (current is IEnumerator nested) stack.Push(nested);
            else yield return current;
        }
        if (!string.IsNullOrEmpty(reportPath)) {
            File.WriteAllText(reportPath, JsonConvert.SerializeObject(new {
                success = error == null, checks, error = error?.ToString()
            }, Formatting.Indented));
        }
        if (error != null) Debug.LogError("Replay wall rewind validation failed: " + error);
        else Debug.Log("Replay wall rewind validation passed: " + checks.Count + " checks.");
        if (!leaveOpen) Application.Quit(error == null ? 0 : 1);
    }

    private IEnumerator Verify() {
        float deadline = Time.realtimeSinceStartup + 60;
        while (Time.realtimeSinceStartup < deadline &&
            (!WindowsManager.Instance || WindowsManager.Instance.GetCurrentWindow() != "menu"))
            yield return null;
        Check(WindowsManager.Instance && WindowsManager.Instance.GetCurrentWindow() == "menu",
            "MainScene is ready");
        WindowsManager.Instance.SwitchWindow("game");
        yield return null;
        Check(GameRecordManager.Instance, "Replay controls are ready");
        JObject data = JObject.Parse(File.ReadAllText(fixturePath));
        var opening = data["validation"]["round_1_opening"];
        openingNode = (int)opening["node"];
        openingHands = opening["hands"].ToObject<int[][]>();
        openingWall = opening["wall"].ToObject<int[]>();
        record = GameRecordManager.Instance;
        NormalGameStateManager.Instance?.ResetForExit();
        WindowsManager.Instance.SwitchWindow("game");
        record.ResetForSessionEnd();
        record.LoadRecord(data["record"].ToString(), data["players"].ToObject<PlayerRecordInfo[]>());
        WindowsManager.Instance.SwitchWindow("recordscene");
        yield return null;

        for (int cycle = 0; cycle < 3; cycle++) {
            record.GotoSelectRound(2);
            yield return null;
            VerifyRound("next round " + cycle, 2);
            record.BackXunmu();
            yield return null;
            VerifyRound("wheel rewind " + cycle, 1);
            Check(record.currentNode == openingNode, "rewind reaches the previous round after revealing gold");
            record.GotoAction(0);
            yield return null;
            VerifyRound("back to opening " + cycle, 1);
            record.GotoSelectNode(openingNode);
            yield return null;
            VerifyRound("seek forward " + cycle, 1);
        }

        if (leaveOpen) {
            record.GotoSelectRound(2);
            yield return null;
            record.BackXunmu();
            yield return null;
            if (!Field<GameObject>("tileListView").activeSelf)
                typeof(GameRecordManager).GetMethod("ShowTileList", Hidden).Invoke(record, null);
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            Field<GameObject>("tileListView").GetComponent<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition = 1f;
        }
    }

    private void VerifyRound(string name, int index) {
        Round round = record.gameRecord.gameRound.rounds[index];
        var expected = new[] { round.p0Tiles, round.p1Tiles, round.p2Tiles, round.p3Tiles };
        var sections = new[] { Field<Transform>("player0HandsContainer"), Field<Transform>("player1HandsContainer"),
            Field<Transform>("player2HandsContainer"), Field<Transform>("player3HandsContainer") };
        var shown = sections.Select(section => section.GetComponentsInChildren<StaticCard>(true).Select(c => c.TileId).ToArray()).ToArray();
        Check(record.currentRoundIndex == index, name + ": correct round");
        for (int seat = 0; seat < 4; seat++)
            Check(shown[seat].OrderBy(t => t).SequenceEqual(expected[seat].OrderBy(t => t)), name + ": initial hand " + seat);
        Check(shown.Sum(hand => hand.Count(t => t == 37)) == expected.Sum(hand => hand.Count(t => t == 37)), name + ": seven-bamboo count");
        Check(Field<List<StaticCard>>("tileListCards").Select(c => c.TileId).SequenceEqual(round.tilesList), name + ": original wall order");
        bool afterOpening = index == 1 && record.currentNode == openingNode;
        IEnumerable<int> remaining = afterOpening ? openingWall : round.tilesList;
        Check(Field<List<int>>("currentTilesList").SequenceEqual(remaining), name + ": remaining wall");
        var players = Field<List<GameRecordManager.RecordPlayer>>("recordPlayerList");
        var expectedHands = afterOpening ? openingHands : expected.Select(hand => hand.ToArray()).ToArray();
        Check(players.All(p => p.tileList.OrderBy(t => t).SequenceEqual(expectedHands[p.playerIndex].OrderBy(t => t))), name + ": replay player hands");
        var self = record.recordPlayer_to_info["self"];
        var hand = GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>();
        Check(hand.Select(card => card.tileId).OrderBy(t => t).SequenceEqual(self.tileList.OrderBy(t => t)), name + ": visible self hand");
    }

    private T Field<T>(string name) => (T)typeof(GameRecordManager).GetField(name, Hidden).GetValue(record);
    private void Check(bool condition, string message) {
        if (!condition) throw new InvalidOperationException(message);
        checks.Add(message);
    }
}
