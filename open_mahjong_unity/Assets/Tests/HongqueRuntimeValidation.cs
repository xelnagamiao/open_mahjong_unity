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

/// <summary>Opt-in, windowless MainScene regression using authoritative server packets.</summary>
public sealed partial class HongqueRuntimeValidation : MonoBehaviour {
    static string folder;
    readonly List<object> checks = new List<object>();
    readonly List<string> failures = new List<string>();
    string currentCase = "boot";
    int completed;
    float deadline;
    readonly Dictionary<string,double> caseSeconds = new Dictionary<string,double>();
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure() {
        folder = null;
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--verify-hongque-client");
        if (index < 0 || index + 1 >= args.Length) return;
        folder = Path.GetFullPath(args[index + 1]);
        ConfigManager.BuildForSteam = false;
        ConfigManager.gameUrl = "ws://127.0.0.1:8081/game";
        Application.runInBackground = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch() {
        if (folder == null) return;
        // Packet projection is isolated from real accounts; network tests run separately.
        NetworkManager.Instance.enabled = false;
        foreach (var chat in Resources.FindObjectsOfTypeAll<ChatManager>()) chat.enabled = false;
        new GameObject("HongqueRuntimeValidation").AddComponent<HongqueRuntimeValidation>();
    }
    void Save(string status) => File.WriteAllText(Path.Combine(folder, "runtime-results.json"),
        JsonConvert.SerializeObject(new { status, completed, currentCase, checks, failures, caseSeconds }, Formatting.Indented));
    void Check(bool passed, string name, object detail = null) {
        checks.Add(new { currentCase, name, passed, detail });
        if (!passed) failures.Add(currentCase + ": " + name);
    }
    void Log(string text, string stack, LogType type) {
        if (type == LogType.Error || type == LogType.Exception
            || text.StartsWith("牌池中已无可用对象") || text.StartsWith("规则未提供听牌计算")
            || text.StartsWith("SyncRecordLastDiscardForRon:") || text.StartsWith("TryTakeLastDiscardObjectForRon:"))
            failures.Add(currentCase + ": " + text + "\n" + stack);
    }
    void Update() {
        if (deadline > 0 && Time.realtimeSinceStartup > deadline) {
            failures.Add(currentCase + ": runtime deadline exceeded"); Save("failed"); deadline = 0; Application.Quit(2);
        }
    }
    IEnumerator Start() {
        Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
        Time.timeScale = 8;
        Directory.CreateDirectory(folder); Application.logMessageReceived += Log;
        deadline = Time.realtimeSinceStartup + 900;
        yield return null;
        var fixtures = JArray.Parse(File.ReadAllText(Path.Combine(folder, "runtime-fixtures.json")));
        foreach (JObject fixture in fixtures) {
            currentCase = (string)fixture["name"]; Save("running");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            yield return Guard(RunCase(fixture));
            caseSeconds[currentCase] = watch.Elapsed.TotalSeconds;
            completed++; Save("running");
        }
        Save(failures.Count == 0 ? "completed" : "failed");
        Application.Quit(failures.Count == 0 ? 0 : 1);
    }
    IEnumerator Guard(IEnumerator routine) {
        var stack = new Stack<IEnumerator>(); stack.Push(routine);
        while (stack.Count > 0) {
            object yielded = null; bool next;
            try { next = stack.Peek().MoveNext(); if (next) yielded = stack.Peek().Current; else stack.Pop(); }
            catch (Exception error) { failures.Add(currentCase + ": " + error); yield break; }
            if (!next) continue;
            if (yielded is IEnumerator nested) stack.Push(nested); else yield return yielded;
        }
    }
    IEnumerator Idle() {
        float until = Time.realtimeSinceStartup + 15;
        yield return null;
        while (GameCanvas.Instance.IsChangeHandCardProcessing || Game3DManager.Instance.HasPendingRecordTableAnimations) {
            if (Time.realtimeSinceStartup > until) throw new Exception("Hand/river animation queue stalled");
            yield return null;
        }
        yield return null;
    }
    string RecordSnapshot(GameRecordManager record) => JsonConvert.SerializeObject(
        record.recordPlayer_to_info.OrderBy(item => item.Key).Select(item => new {
            seat = item.Key, hand = item.Value.tileList.OrderBy(id => id).ToArray(),
            river = item.Value.discardTiles.ToArray(), melds = item.Value.combinationMasks.Select(mask => mask.ToArray()).ToArray(),
            score = item.Value.score
        }).ToArray());

    void CheckRecordTable(GameRecordManager record) {
        foreach (string position in new[] { "left", "top", "right" }) {
            var player = record.recordPlayer_to_info[position];
            var panel = Game3DManager.Instance.GetPosPanel(position);
            Transform row = RecordSetting.Instance.IsShowCardsMode ? panel.ShowCardsPosition : panel.cardsPosition;
            Check(row.childCount == player.tileList.Count, "replay physical hand " + position,
                new { node=record.currentNode, expected=player.tileList.Count, actual=row.childCount });
            if (!player.isHu && !player.hasRonWinningTile) Check3DRow(row, position, player.tileList.Count,
                player.showHandDrawSlotActive, RecordSetting.Instance.IsShowCardsMode,
                requireDrawSlot: RecordSetting.Instance.IsShowCardsMode);
        }
        var round = record.gameRecord.gameRound.rounds[record.currentRoundIndex];
        var drawn = new HashSet<int>(round.actionTicks.Take(record.currentNode)
            .Where(tick => tick[0] == "d" || tick[0] == "gd" || tick[0] == "bd" || tick[0] == "nd")
            .Select(tick => int.Parse(tick[1])));
        var wall = (List<int>)typeof(GameRecordManager).GetField("currentTilesList",Hidden).GetValue(record);
        Check(wall.SequenceEqual(round.tilesList.Where(tile => !drawn.Contains(tile))),
            "replay wall removes exactly the recorded draws", new { node=record.currentNode });
    }

    IEnumerator RunRecord(JObject fixture) {
        var gsm = NormalGameStateManager.Instance;
        gsm.StopAsRealtimeSpectator();
        PlayerSession.Current.SetGamestateId("");
        GameHost.Current.SwitchWindow("game");
        var record = GameRecordManager.Instance;
        record.ResetForSessionEnd();
        typeof(RecordSetting).GetField("isShowCardsMode",Hidden).SetValue(RecordSetting.Instance,
            (bool?)fixture["showCardsMode"] ?? true);
        var source = (JObject)fixture["record"];
        var players = fixture["players"].ToObject<PlayerRecordInfo[]>();
        UserDataManager.Instance.SetUserInfo("虹雀牌谱回归", "fixture-only", players[0].user_id, false);
        record.LoadRecord(source.ToString(), players);
        yield return Idle();
        Check(record.gameRecord != null && record.gameRecord.gameRound.rounds.Count > 0, "replay loads authoritative record");
        int seat = (int)fixture["seat"];
        record.SwitchRecordPerspectiveToOriginalPlayerIndex(seat);
        yield return Idle();
        Check(record.selectedPlayerOriginalIndex == seat, "replay perspective restored");
        var manifest = RuleRegistry.Resolve("hongque", "hongque/v1.6");
        Check(manifest.Tingpai != null && manifest.DescribeWaitingTile != null, "replay hint hooks are registered");
        var round = record.gameRecord.gameRound.rounds[1];
        for (int node = 0; node <= round.actionTicks.Count; node++) {
            record.GotoAction(node);
            yield return Idle();
            Check(record.currentNode == node, "seek reaches node " + node);
            CheckRecordTable(record);
            Check(GameCanvas.Instance.HandCardsContainer.childCount == record.recordPlayer_to_info["self"].tileList.Count,
                "seek hand rendering " + node);
            CheckWinningRecordState(record, fixture);
            if (node == 0) {
                var dealer = record.recordPlayer_to_info.Values.First(player => player.playerIndex == round.startPlayerIndex);
                Check(dealer.showHandDrawSlotActive, "opening draw slot is restored in replay");
                Check(RecordChongHintCalculator.NormalizeHandForTingpai(dealer, "hongque").Count == dealer.tileList.Count - 1,
                    "opening draw is excluded from replay waiting calculation");
            }
            if (node == round.actionTicks.Count && currentCase.StartsWith("replay-claim-")) {
                var waitingPlayer = record.recordPlayer_to_info.Values.First(player => player.playerIndex == 1);
                Check(RecordChongHintCalculator.ComputeWaitingTilesForPlayer(waitingPlayer, "hongque").Contains(1001),
                    "replay preserves the authoritative AX1 wait");
                Check(RecordChongHintCalculator.ComputeDangerTiles(record.recordPlayer_to_info, "hongque").Contains(1001),
                    "replay danger hints include actual ron waits");
            }
            if (node == round.actionTicks.Count) continue;
            string action = round.actionTicks[node][0];
            if (action.StartsWith("hu") || action == "liuju" || action == "end") continue;
            record.AdvanceToNextAction();
            yield return Idle();
            int reached = record.currentNode;
            CheckRecordTable(record);
            string sequential = RecordSnapshot(record);
            record.GotoAction(reached);
            yield return Idle();
            Check(RecordSnapshot(record) == sequential, "sequential playback equals seek " + node + " " + action);
        }
        record.ResetForSessionEnd(); record.HideGameRecord(); gsm.ResetForExit();
        {
            var initial = (JObject)source.DeepClone();
            var ticks = (JArray)initial["game_round"]["round_index_1"]["action_ticks"];
            var update = new JArray(new JObject { ["type"] = "ticks", ["round_index"] = 1, ["new_ticks"] = ticks.DeepClone() });
            initial["game_round"]["round_index_1"]["action_ticks"] = new JArray();
            GameRecordManager.PrepareDelayedSpectatorSession("qa-delayed");
            record.StartSpectating(initial.ToString());
            record.SwitchRecordPerspectiveToOriginalPlayerIndex(seat);
            yield return Idle();
            Check(record.IsSpectating && record.IsLiveSpectatorMode, "delayed spectator enters live mode");
            record.AppendSpectatorTicks(update.ToString());
            float until = Time.realtimeSinceStartup + 45;
            int count = record.gameRecord.gameRound.rounds[1].actionTicks.Count;
            int largestSelfReveal = 0;
            var observedWinningPanels = new HashSet<int>();
            while (record.currentNode < count && Time.realtimeSinceStartup < until) {
                largestSelfReveal = Math.Max(largestSelfReveal,
                    Game3DManager.Instance.GetPosPanel("self").cardsPosition.childCount);
                ObserveWinningRecordPanel(record, fixture, observedWinningPanels);
                yield return null;
            }
            Check(record.currentNode == count, "delayed spectator consumes appended ticks");
            CheckWinningRecordState(record, fixture);
            if (fixture["winningHands"] is JObject expectedWinners)
                Check(observedWinningPanels.Count == expectedWinners.Count, "delayed spectator displayed every complete winning panel",
                    new { observed = observedWinningPanels.ToArray(), expected = expectedWinners.Count });
            if (currentCase.StartsWith("replay-multi_ron-") && seat > 0) {
                var self=record.recordPlayer_to_info["self"];
                // End clears presentation objects and restores the ordinary 2D hand.
                // Inspect the complete physical hand while its reveal is actually visible.
                Check(largestSelfReveal == self.tileList.Count, "multi-ron replay shows the self winner's complete reveal",
                    new {expected=self.tileList.Count,actual=largestSelfReveal});
            }
            record.SwitchToRecordMode();
            record.GotoAction(0); yield return Idle();
            Check(!record.IsLiveSpectatorMode && record.currentNode == 0, "delayed spectator can review history");
            record.SwitchToLiveMode(); yield return Idle();
            // Catch-up deliberately replays trailing settlements instead of
            // silently seeking past their panels. Allow that asynchronous path.
            until = Time.realtimeSinceStartup + 45;
            while (record.currentNode < count && Time.realtimeSinceStartup < until) yield return null;
            Check(record.IsLiveSpectatorMode && record.currentNode == count, "delayed spectator returns to live tail",
                new { expected = count, actual = record.currentNode, live = record.IsLiveSpectatorMode });
            record.ResetForSessionEnd(); record.HideGameRecord(); gsm.ResetForExit();
        }
    }
    IEnumerator RunCase(JObject fixture) {
        Time.timeScale = (float?)fixture["timeScale"] ?? 8;
        NormalGameStateManager.Instance.ResetForExit();
        if (fixture["record"] is JObject) { yield return RunRecord(fixture); yield break; }
        var restore = fixture["restore"].ToObject<Response>();
        var state = restore.hongque_state;
        UserDataManager.Instance.SetUserInfo("虹雀回归", "fixture-only", state.players[state.you].user_id, false);
        bool spectating = (bool?)fixture["spectator"] == true;
        GameSession.Current.IsRealtimeSpectator = spectating;
        if (fixture["initial"] == null && fixture["livePackets"] == null) {
            var early = JObject.FromObject(restore);
            early["type"] = "gamestate/hongque/update";
            GameStateNetworkManager.Instance.HandleGameStateMessage(early.ToObject<Response>());
            Check(!HongqueGameState.IsTableActive, "unsolicited increment cannot establish a session");
        }
        if (fixture["initial"] is JObject) {
            GameStateNetworkManager.Instance.HandleGameStateMessage(fixture["initial"].ToObject<Response>());
            yield return Idle();
            if ((bool?)fixture["checkDrawLayout"] == true) CheckDrawLayout(fixture["initial"].ToObject<Response>().hongque_state);
        }
        if (fixture["livePackets"] is JArray live) {
            for (int packetIndex = 0; packetIndex < live.Count; packetIndex++) {
                var packet = live[packetIndex];
                GameStateNetworkManager.Instance.HandleGameStateMessage(packet.ToObject<Response>());
                if ((bool?)fixture["duplicateEvents"] == true && (string)packet["type"] == "gamestate/hongque/update") {
                    GameStateNetworkManager.Instance.HandleGameStateMessage(packet.ToObject<Response>());
                }
                yield return Idle();
                if (fixture["drawSnapshots"] is JArray snapshots) CheckDrawLayout(snapshots[packetIndex].ToObject<HongqueStateInfo>());
            }
        } else {
            GameStateNetworkManager.Instance.HandleGameStateMessage(restore);
        }
        if (state.phase == "round_end" && state.round_result.winner_indices.Length > 0) {
            EndResultPanel.Instance.UpdateReadyStatus(new Dictionary<int, bool> { [state.you] = true });
        }
        yield return Idle();
        Check(HongqueGameState.IsTableActive && GameSession.Current.RoomRule == "hongque", "reconnect establishes rule and table");
        if (!HongqueGameState.IsTableActive) yield break;
        var gsm = NormalGameStateManager.Instance;
        var hand = state.hand.Select(HongqueTileVisual.FromCode).OrderBy(x => x).ToArray();
        Check(gsm.selfHandTiles.OrderBy(x => x).SequenceEqual(hand), "restored authoritative hand", new { expected=hand, actual=gsm.selfHandTiles.ToArray() });
        Check(GameCanvas.Instance.HandCardsContainer.childCount == hand.Length, "visible hand is not empty", GameCanvas.Instance.HandCardsContainer.childCount);
        foreach (var player in state.players) {
            string seat = gsm.indexToPosition[player.index];
            var panel = Game3DManager.Instance.GetPosPanel(seat);
            if (state.phase != "round_end") Check(panel.discardsPosition.childCount == player.discards.Length, "river restored " + seat);
            Check(gsm.player_to_info[seat].combination_masks.Count == player.melds.Length, "meld state restored " + seat);
        }
        if (!spectating && state.legal_actions.Contains("discard"))
            Check(gsm.allowActionList.Contains("cut") && gsm.IsSelfActionRequired, "reconnect restores actionable discard");
        if (!spectating && state.phase == "claim" && state.legal_actions.Length > 0)
            Check(gsm.allowActionList.Count > 0, "reconnect restores claim buttons");
        if (spectating) Check(gsm.allowActionList.Count == 0 && !gsm.IsSelfActionRequired, "spectator cannot act");
        Check(GameSession.Current.PointerTips == state.pointer_tips, "pointer hint setting survives restore");
        if (fixture["nextPackets"] is JArray packets) {
            foreach (var packet in packets) {
                GameStateNetworkManager.Instance.HandleGameStateMessage(packet.ToObject<Response>());
                yield return Idle();
            }
            var expected = fixture["after"].ToObject<Response>().hongque_state;
            Check(gsm.selfHandTiles.OrderBy(x=>x).SequenceEqual(expected.hand.Select(HongqueTileVisual.FromCode).OrderBy(x=>x)),
                "subsequent packets continue after reconnect", gsm.selfHandTiles.ToArray());
        }
        if (state.phase == "round_end") {
            if (state.round_result.winner_indices.Length > 0) {
                float until = Time.realtimeSinceStartup + 60;
                while ((!EndResultPanel.Instance.gameObject.activeInHierarchy
                    || !(bool)typeof(EndResultPanel).GetField("readyStatusVisible", Hidden).GetValue(EndResultPanel.Instance))
                    && Time.realtimeSinceStartup < until) yield return null;
                Check(EndResultPanel.Instance.gameObject.activeInHierarchy, "settlement panel restored");
                Check((bool)typeof(EndResultPanel).GetField("readyStatusVisible", Hidden).GetValue(EndResultPanel.Instance),
                    "last winner reaches confirmation panel");
                var result = state.round_result.multi_ron ? state.round_result.winners.Last()
                    : state.round_result.winners.FirstOrDefault(w => w.player == state.you) ?? state.round_result.winners.First();
                int[] resultHand = result.hand.Select(HongqueTileVisual.FromCode).ToArray();
                int[] resultMelds = result.melds.SelectMany(m => m.tiles).Select(HongqueTileVisual.FromCode).ToArray();
                CheckWinningPanel(resultHand, resultMelds, "live/reconnect/spectator authoritative settlement");
                var ready = (Dictionary<int, bool>)typeof(EndResultPanel).GetField("cachedReadyStatus", Hidden).GetValue(EndResultPanel.Instance);
                Check(ready.TryGetValue(state.you, out bool accepted) && accepted, "ready received during reveal is retained");
                if (state.round_result.reason == "ron" && state.last_discard != null) {
                    int source = state.last_discard.player;
                    Check(Game3DManager.Instance.GetPosPanel(gsm.indexToPosition[source]).discardsPosition.childCount
                        == state.players[source].discards.Length - 1, "ron removes exactly one source river tile");
                }
            }
            foreach (var player in state.players) {
                var info = gsm.player_to_info[gsm.indexToPosition[player.index]];
                Check(info.score_history.SequenceEqual(player.score_history), "score history is authoritative " + player.index,
                    new { expected=player.score_history, actual=info.score_history.ToArray() });
                Check(info.round_number_history.SequenceEqual(player.round_number_history), "history round numbers " + player.index);
            }
            Check(gsm.roundSettlementHistory.Count == state.players[0].score_history.Length, "one settlement snapshot per server score row");
        }
        if (state.phase == "game_end") Check(EndGamePanel.Instance.gameObject.activeInHierarchy, "final ranking restored");
        if ((bool?)fixture["probeDrawLayout"] == true) {
            RuleRegistry.SetCurrent((string)fixture["drawProbeRule"] ?? "hongque");
            AutoAction.Instance.SetAutoArrangeHandCards((bool?)fixture["autoArrange"] ?? true);
            yield return ProbeDrawLayout();
        }
        if ((bool?)fixture["probeOpeningConsolidation"] == true) yield return ProbeOpeningConsolidation();
    }

    void CheckWinningRecordState(GameRecordManager record, JObject fixture) {
        if (!(fixture["winningHands"] is JObject winners)) return;
        var ticks = record.gameRecord.gameRound.rounds[1].actionTicks;
        foreach (var item in winners.Properties()) {
            int seat = int.Parse(item.Name);
            if (!ticks.Take(record.currentNode).Any(t => t[0].StartsWith("hu_") && int.Parse(t[1]) == seat)) continue;
            var player = record.recordPlayer_to_info[record.indexToPosition[seat]];
            Check(player.hasRonWinningTile && player.tileList.SequenceEqual(item.Value.ToObject<int[]>()),
                "spectator/seek retains explicit winning tile for " + seat, player.tileList.ToArray());
        }
    }

    void ObserveWinningRecordPanel(GameRecordManager record, JObject fixture, HashSet<int> seen) {
        if (!(fixture["winningHands"] is JObject winners) || !EndResultPanel.Instance.gameObject.activeInHierarchy) return;
        var row = (GameObject)typeof(EndResultPanel).GetField("EndTilescontainer", Hidden).GetValue(EndResultPanel.Instance);
        var cards = row.GetComponentsInChildren<StaticCard>();
        if (cards.Length == 0) return;
        foreach (var item in winners.Properties()) {
            int seat = int.Parse(item.Name);
            if (seen.Contains(seat)) continue;
            int[] hand = item.Value.ToObject<int[]>();
            var player = record.recordPlayer_to_info[record.indexToPosition[seat]];
            int[] melds = player.combinationMasks.SelectMany(mask => mask.Where((tile, index) => index % 2 == 1)).ToArray();
            // 多家和同用一张河牌，使用整行牌面辨别当前展示的赢家。
            if (!cards.Select(card => card.TileId).OrderBy(tile => tile).SequenceEqual(hand.Concat(melds).OrderBy(tile => tile))) continue;
            CheckWinningPanel(hand, melds, "delayed spectator panel " + seat);
            string position = record.indexToPosition[seat];
            var panel = Game3DManager.Instance.GetPosPanel(position);
            Transform tiles = position != "self" && RecordSetting.Instance.IsShowCardsMode ? panel.ShowCardsPosition : panel.cardsPosition;
            Check(tiles.GetComponentsInChildren<Tile3D>().Select(tile => tile.GetTileId()).OrderBy(tile => tile)
                .SequenceEqual(hand.OrderBy(tile => tile)), "delayed spectator physical winning hand " + seat);
            seen.Add(seat);
        }
    }

    void CheckWinningPanel(int[] hand, int[] melds, string label) {
        var row = (GameObject)typeof(EndResultPanel).GetField("EndTilescontainer", Hidden).GetValue(EndResultPanel.Instance);
        int[] actual = row.GetComponentsInChildren<StaticCard>().Select(card => card.TileId).ToArray();
        Check(actual.OrderBy(tile => tile).SequenceEqual(hand.Concat(melds).OrderBy(tile => tile)), label + " preserves every tile", actual);
        Check(actual.Length > 0 && actual.Last() == hand.Last(), label + " keeps the actual winning tile separate", actual);
    }
}
#endif
