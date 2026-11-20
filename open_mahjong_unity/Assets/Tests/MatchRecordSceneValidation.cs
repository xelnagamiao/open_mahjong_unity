#if UNITY_EDITOR
using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>Run in MainScene Play Mode with a real server-produced paired record.</summary>
public static class MatchRecordSceneValidation {
    public static string Run(string detailJson) {
        if (!Application.isPlaying) throw new InvalidOperationException("MainScene Play Mode required");
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var detail = JsonConvert.DeserializeObject<RecordDetail>(detailJson);
        detail.selected_table_id = "open";
        detail.cloud_saved = false;
        Check(RecordPanel.OpenRecord(detail, true), "cold record entry");
        var manager = GameRecordManager.Instance;
        Button Button(string name) => manager.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
        var compare = manager.GetComponentsInChildren<Toggle>(true).Single(t => t.name == "CompareWall");
        var phase = manager.GetComponentsInChildren<TMP_Dropdown>(true).Single(t => t.name == "ComparePhase");
        phase.value = 0;
        Check(Button("GoOpen").gameObject.activeInHierarchy && Button("GoClosed").gameObject.activeInHierarchy, "both complete navigation buttons are visible");
        Check(!Button("GoOpen").interactable && Button("GoClosed").interactable, "current table button is disabled");
        var actions = (JArray)manager.MatchSession.Table("open")["round_meta"]["round_index_1"]["action_meta"];
        int sourceNode = -1, targetNode = -1, sourceSeat = -1;
        for (int i = 0; i < actions.Count; i++) {
            var origin = actions[i]["wall_ref"] as JObject;
            if (origin == null) continue;
            var location = MatchRecordSession.Locate(manager.MatchSession.Table("closed"), origin);
            if (location.Exact && location.Node != i + 1) { sourceNode = i + 1; targetNode = location.Node; sourceSeat = (int)actions[i]["actor_original_seat"]; break; }
        }
        Check(sourceNode > 0, "fixture contains divergent action counts");
        manager.SwitchRecordPerspectiveToOriginalPlayerIndex(sourceSeat);
        manager.JumpToSharePosition(1, sourceNode);
        Button("GoClosed").onClick.Invoke();
        Check(manager.MatchSession.ActiveTable == "closed" && manager.currentNode == targetNode, "button aligns identical physical source across unequal nodes");
        Button("GoOpen").onClick.Invoke();
        Check(manager.currentNode == sourceNode, "return button preserves same wall position");
        phase.value = 1; Button("GoClosed").onClick.Invoke();
        Check(manager.currentNode == targetNode - 1, "before draw is the preceding prefix");
        compare.isOn = false; manager.JumpToSharePosition(1, 7); Button("GoOpen").onClick.Invoke();
        manager.JumpToSharePosition(1, 9); Button("GoClosed").onClick.Invoke();
        Check(manager.currentNode == 7, "independent table cursors survive switches");
        Check(!Button("CopyRecordLink").gameObject.activeSelf, "local record cannot create a cloud link");
        var controls = Button("GoOpen").transform.parent;
        Check(controls.GetComponentsInChildren<TMP_Text>(true).All(t => t.fontWeight == FontWeight.Regular && (t.fontStyle & FontStyles.Bold) == 0), "Chinese controls keep regular weight");

        var live = new MatchRecordSession((string)detail.record["match_id"], true);
        JObject Table(string id, int count) {
            var table = (JObject)detail.record["tables"][id].DeepClone();
            var rounds = (JObject)table["payload"]["game_round"];
            foreach (var p in rounds.Properties().Where(p => p.Name != "round_index_1").ToArray()) p.Remove();
            var meta = (JObject)table["round_meta"];
            foreach (var p in meta.Properties().Where(p => p.Name != "round_index_1").ToArray()) p.Remove();
            rounds["round_index_1"]["action_ticks"] = new JArray(((JArray)rounds["round_index_1"]["action_ticks"]).Take(count));
            meta["round_index_1"]["action_meta"] = new JArray(((JArray)meta["round_index_1"]["action_meta"]).Take(count));
            table["record_state"] = "partial";
            return table;
        }
        JObject Snapshot(string id, JObject table, int sequence) => new JObject {
            ["type"] = "spectator/stream_snapshot", ["protocol_version"] = 2,
            ["match_id"] = live.MatchId, ["stream_id"] = id, ["stream_epoch"] = "scene-" + id, ["at_seq"] = sequence,
            ["body"] = new JObject { ["table_id"] = id, ["table_record"] = table }
        };
        live.ApplyPacket(Snapshot("open", Table("open", 8), 0));
        live.ApplyPacket(Snapshot("closed", Table("closed", 10), 0));
        manager.LoadMatchSession(live);
        Check(manager.IsSpectating && manager.currentNode == 8, "live entry starts at released tail");
        live.ApplyPacket(Snapshot("closed", Table("closed", 12), 1)); manager.RefreshMatchStream("closed");
        Check(manager.currentNode == 8 && manager.MatchSession.ActiveTable == "open", "inactive stream does not redraw active table");
        Button("GoClosed").onClick.Invoke();
        Check(manager.currentNode == 12 && manager.IsLiveSpectatorMode, "live switch uses other released tail");
        Button("IndependentProgress").onClick.Invoke();
        Check(!manager.IsLiveSpectatorMode && live.Mode == MatchRecordSession.PlaybackMode.History && manager.currentNode == 12, "independent mode can pause a live session even when compare was off");
        compare.isOn = true; manager.JumpToSharePosition(1, 2);
        live.ApplyPacket(Snapshot("closed", Table("closed", 14), 2)); manager.RefreshMatchStream("closed");
        Check(manager.currentNode == 2 && !manager.IsLiveSpectatorMode && live.Mode == MatchRecordSession.PlaybackMode.Compare, "incoming stream cannot resume paused comparison");
        var network = NetworkManager.Instance;
        var networkCache = typeof(NetworkManager).GetField("matchSpectatorSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        networkCache.SetValue(network, live);
        network.SuspendMatchSpectator();
        manager.HideGameRecord();
        Check(live.Position("closed").Node == 2 && NetworkManager.HasMatchSpectatorSession, "reconnect suspension remembers the cursor before UI teardown");
        Check(!manager.IsSpectating && manager.MatchSession == null, "suspended UI cannot masquerade as a competing spectator session");
        Check(GameSessionGuard.IsSpectatorSession, "network cache keeps exclusive ownership while its UI is suspended");
        var entryGuard = typeof(NetworkManager).GetMethod("BlocksMatchSpectatorEntry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Check(!(bool)entryGuard.Invoke(null, new object[] { live }), "the production network entry guard permits restoring the suspended match");
        manager.LoadMatchSession(live);
        Check(manager.currentNode == 2 && !manager.IsLiveSpectatorMode, "restored cache preserves comparison pause and cursor");
        foreach (string id in new[] { "open", "closed" }) {
            var complete = (JObject)detail.record["tables"][id].DeepClone();
            complete["table_record_id"] = null;
            live.ApplyPacket(Snapshot(id, complete, 3)); manager.RefreshMatchStream(id);
        }
        var manifest = (JObject)detail.record.DeepClone(); manifest.Remove("tables");
        manifest["final_streams"] = new JObject { ["open"] = new JObject { ["stream_epoch"] = "scene-open", ["applied_seq"] = 3 },
            ["closed"] = new JObject { ["stream_epoch"] = "scene-closed", ["applied_seq"] = 3 } };
        live.ApplyPacket(new JObject { ["type"] = "spectator/stream_snapshot", ["protocol_version"] = 2,
            ["match_id"] = live.MatchId, ["stream_id"] = "match", ["stream_epoch"] = "scene-match", ["at_seq"] = 0, ["body"] = manifest });
        manager.RefreshMatchStream("match");
        Check(!manager.IsSpectating && manager.MatchSession != null && !manager.MatchSession.IsLiveSession && manager.currentNode == 2, "final vector converts both tables to archive without moving the cursor");
        Check(!NetworkManager.HasMatchSpectatorSession, "completion releases network subscription");
        manager.LoadMatchSession(new MatchRecordSession(live.MatchId, true));
        manager.StopSpectating();
        Check(!GameSessionGuard.IsSpectatorSession && manager.MatchSession == null, "spectator exit releases all ownership");
        Check(RecordPanel.OpenRecord(detail, true), "record can reopen after spectator exit");
        return checks + " MainScene checks passed; aligned open node " + sourceNode + " to closed node " + targetNode;
    }
}
#endif
