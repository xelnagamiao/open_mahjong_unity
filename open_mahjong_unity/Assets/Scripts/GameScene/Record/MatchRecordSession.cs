using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>
/// Unity-independent match cache. Stream sequence numbers count transport events;
/// replay nodes count applied action ticks. Neither can identify a tile across tables.
/// </summary>
public sealed class MatchRecordSession {
    public enum PlaybackMode { Live, History, Compare }
    public sealed class Cursor {
        public string RoundKey;
        public int Node;
        public int ViewSeat;
        public Cursor(string roundKey, int node, int viewSeat = -1) { RoundKey = roundKey; Node = node; ViewSeat = viewSeat; }
    }
    public sealed class Location {
        public string Status;
        public string RoundKey;
        public int Node;
        public int? ActorOriginalSeat;
        public int ViewSeat = -1;
        public bool Exact => Status == "EXACT";
        public Location(string status) { Status = status; }
    }
    private sealed class Stream {
        public string Epoch;
        public long Sequence;
        public JObject Body;
    }
    private readonly Dictionary<string, Stream> streams = new Dictionary<string, Stream>();
    private readonly Dictionary<string, Cursor> positions = new Dictionary<string, Cursor>();
    private readonly Dictionary<string, Cursor> independentPositions = new Dictionary<string, Cursor>();
    private readonly Dictionary<string, JObject> tables = new Dictionary<string, JObject>();
    private readonly Dictionary<string, HashSet<string>> retiredEpochs = new Dictionary<string, HashSet<string>>();
    public string MatchId { get; private set; }
    public string ActiveTable { get; private set; }
    public PlaybackMode Mode { get; set; }
    public JObject Manifest { get; private set; }
    public bool IsLiveSession { get; }
    public IEnumerable<string> TableIds => tables.Keys;

    public MatchRecordSession(string matchId, bool live, string selectedTable = "open") {
        if (string.IsNullOrEmpty(matchId)) throw new ArgumentException("Missing match identity");
        MatchId = matchId; IsLiveSession = live; ActiveTable = selectedTable;
        Mode = live ? PlaybackMode.Live : PlaybackMode.History;
    }

    public static MatchRecordSession FromRecord(JObject record, string selectedTable = null) => OpenArchive(record, selectedTable, true);

    // The archive viewer owns a read-only snapshot. Live sessions and independent callers
    // can still request a detached copy before applying transport changes.
    internal static MatchRecordSession OpenArchive(JObject record, string selectedTable, bool copyTables) {
        var expanded = MatchRecordCodec.ExpandStorage(record);
        copyTables = copyTables && ReferenceEquals(expanded, record);
        record = expanded;
        if ((string)record?["format"] != "open_mahjong.match_record" || (int?)record["schema_version"] != 2)
            throw new FormatException("Unsupported match record version");
        var all = record["tables"] as JObject ?? throw new FormatException("Missing tables");
        string defaultTable = new[] { "main", "open", "closed" }.FirstOrDefault(t => all[t]?["payload"] is JObject);
        var session = new MatchRecordSession((string)record["match_id"], false,
            selectedTable ?? defaultTable ?? throw new FormatException("No playable table"));
        session.Manifest = new JObject(record.Properties().Where(p => p.Name != "tables")
            .Select(p => new JProperty(p.Name, p.Value.DeepClone())));
        foreach (var pair in all.Properties()) {
            var table = pair.Value as JObject ?? throw new FormatException("Invalid table");
            ValidateTable(table, pair.Name);
            session.tables[pair.Name] = copyTables ? (JObject)table.DeepClone() : table;
        }
        if (!session.Ready(session.ActiveTable)) throw new FormatException("Selected table unavailable");
        return session;
    }

    public JObject Table(string table) => tables.TryGetValue(table, out var data) ? data : null;
    public bool RestrictTables(IEnumerable<string> allowedTables) {
        var allowed = new HashSet<string>(allowedTables);
        if (allowed.Count == 0 || allowed.Any(t => t != "open" && t != "closed")) throw new FormatException("Invalid table permission set");
        bool changed = false;
        foreach (string table in new[] { "open", "closed" }) if (!allowed.Contains(table)) {
            changed |= tables.Remove(table);
            streams.Remove(table); positions.Remove(table); independentPositions.Remove(table);
        }
        if (!allowed.Contains(ActiveTable)) { ActiveTable = allowed.Contains("open") ? "open" : "closed"; changed = true; }
        if (changed) { streams.Remove("match"); Manifest = null; }
        return changed;
    }
    public void SelectInitialTable(string table) { if (table != "open" && table != "closed") throw new ArgumentException("Invalid table"); ActiveTable = table; }
    public string Epoch(string stream) => streams.TryGetValue(stream, out var value) ? value.Epoch : null;
    public bool Ready(string table) => Table(table)?["payload"] is JObject && Table(table)?["round_meta"] is JObject meta && meta.Count > 0;
    public JObject ResumeCursors() => new JObject(streams.Select(pair => new JProperty(pair.Key,
        new JObject { ["stream_epoch"] = pair.Value.Epoch, ["applied_seq"] = pair.Value.Sequence })));

    /// <returns>applied, duplicate, or resync. A rejected delta never partially changes a cache.</returns>
    public string ApplyPacket(JObject packet) {
        if ((string)packet["match_id"] != MatchId || (int?)packet["protocol_version"] != 2) return "resync";
        string id = (string)packet["stream_id"], epoch = (string)packet["stream_epoch"], type = (string)packet["type"];
        if ((id != "match" && id != "open" && id != "closed") || string.IsNullOrEmpty(epoch)) return "resync";
        streams.TryGetValue(id, out var previous);
        JObject body;
        long sequence;
        try {
            if (type == "spectator/stream_snapshot") {
                sequence = RequiredSequence(packet["at_seq"]);
                if (retiredEpochs.TryGetValue(id, out var retired) && retired.Contains(epoch)) return "duplicate";
                if (previous != null && previous.Epoch == epoch && sequence <= previous.Sequence) return "duplicate";
                body = (JObject)packet["body"].DeepClone();
            } else if (type == "spectator/stream_delta") {
                if (previous == null || previous.Epoch != epoch) return "resync";
                long first = RequiredSequence(packet["from_seq"]), last = RequiredSequence(packet["to_seq"]);
                var events = packet["events"] as JArray;
                if (events == null || first < 1 || last < first || last - first + 1 != events.Count) return "resync";
                if (last <= previous.Sequence) return "duplicate";
                if (first > previous.Sequence + 1) return "resync";
                body = (JObject)previous.Body.DeepClone();
                for (long i = Math.Max(0, previous.Sequence - first + 1); i < events.Count; i++)
                    body = ApplyEvent(id, body, (JObject)events[(int)i]);
                sequence = last;
            } else return "resync";
            if (id == "match") {
                if ((string)body["match_id"] != MatchId) return "resync";
            } else {
                if ((string)body["table_id"] != id) return "resync";
                if (body["table_record"] is JObject table) ValidateTable(table, id);
            }
        } catch (Exception e) when (e is FormatException || e is InvalidCastException || e is ArgumentException || e is NullReferenceException || e is OverflowException) {
            return "resync";
        }
        bool changedEpoch = previous != null && previous.Epoch != epoch;
        if (changedEpoch) {
            if (!retiredEpochs.TryGetValue(id, out var retired)) retiredEpochs[id] = retired = new HashSet<string>();
            retired.Add(previous.Epoch);
        }
        streams[id] = new Stream { Epoch = epoch, Sequence = sequence, Body = body };
        if (id == "match") Manifest = body;
        else {
            tables[id] = body["table_record"] as JObject;
            if (changedEpoch) { positions.Remove(id); independentPositions.Remove(id); }
        }
        return "applied";
    }

    private static long RequiredSequence(JToken value) {
        if (value?.Type != JTokenType.Integer || (long)value < 0) throw new FormatException("Invalid sequence");
        return (long)value;
    }

    private static JObject ApplyEvent(string id, JObject body, JObject change) {
        string kind = (string)change["kind"];
        if (id == "match") {
            if (kind != "manifest_updated" && kind != "record_ready") throw new FormatException("Unknown match event");
            return (JObject)change["manifest"].DeepClone();
        }
        if (kind == "table_available" || kind == "table_finalized") return (JObject)change["body"].DeepClone();
        var table = (JObject)body["table_record"];
        var rounds = (JObject)table["payload"]["game_round"];
        var metadata = (JObject)table["round_meta"];
        if (kind == "round_opened") {
            string key = (string)change["round_key"];
            if (string.IsNullOrEmpty(key) || rounds[key] != null) throw new FormatException("Round already exists");
            rounds[key] = change["round_header"].DeepClone();
            metadata[key] = change["round_meta"].DeepClone();
        } else if (kind == "actions_appended") {
            var keys = metadata.Properties().Where(p => (string)p.Value["table_round_id"] == (string)change["table_round_id"]).ToArray();
            if (keys.Length != 1) throw new FormatException("Unbound round");
            string key = keys[0].Name;
            var ticks = (JArray)rounds[key]["action_ticks"];
            var actions = (JArray)metadata[key]["action_meta"];
            var appended = (JArray)change["ticks"];
            var origins = (JArray)change["action_meta"];
            if ((int?)change["start_node"] != ticks.Count || appended.Count != origins.Count) throw new FormatException("Action prefix mismatch");
            foreach (var tick in appended) ticks.Add(tick.DeepClone());
            foreach (var action in origins) actions.Add(action.DeepClone());
            if (metadata[key]["legacy_node_map"] is JArray nodeMap)
                for (int n = nodeMap.Count; n <= ticks.Count; n++) nodeMap.Add(n);
            if (origins.Any(a => (string)a["wall_ref_basis"] == "unavailable")) {
                metadata[key]["alignment_status"] = "partial";
                metadata[key]["alignment_reason"] = "missing_draw_origin";
            }
            if (rounds[key]["action_times_ms"] is JArray times)
                foreach (var action in origins) times.Add(action["occurred_at_ms"]?.DeepClone() ?? JValue.CreateNull());
        } else throw new FormatException("Unknown table event");
        var head = metadata.Properties().OrderBy(p => (int)p.Value["round_index"]).LastOrDefault();
        body["released_head"] = head == null ? null : new JObject {
            ["table_round_id"] = head.Value["table_round_id"].DeepClone(), ["node"] = ((JArray)head.Value["action_meta"]).Count };
        body["availability"] = head == null ? "waiting" : "ready";
        return body;
    }

    private static void ValidateTable(JObject table, string id) {
        if ((string)table["table_id"] != id) throw new FormatException("Table identity mismatch");
        if ((string)table["record_state"] == "missing") return;
        var rounds = table["payload"]?["game_round"] as JObject;
        var metadata = table["round_meta"] as JObject;
        if (rounds == null || metadata == null || !rounds.Properties().Select(p => p.Name).OrderBy(x => x).SequenceEqual(metadata.Properties().Select(p => p.Name).OrderBy(x => x)))
            throw new FormatException("Round metadata mismatch");
        var roundIds = new HashSet<string>();
        foreach (var pair in rounds.Properties()) {
            var meta = metadata[pair.Name];
            if (!(pair.Value["action_ticks"] is JArray ticks) || !(meta["action_meta"] is JArray actions) || ticks.Count != actions.Count ||
                !roundIds.Add((string)meta["table_round_id"])) throw new FormatException("Action metadata mismatch");
        }
    }

    public void Remember(int roundIndex, int node, int viewSeat = -1) {
        if (viewSeat < 0 && positions.TryGetValue(ActiveTable, out var previous)) viewSeat = previous.ViewSeat;
        var cursor = new Cursor("round_index_" + roundIndex, node, viewSeat);
        positions[ActiveTable] = cursor;
        if (Mode != PlaybackMode.Compare) independentPositions[ActiveTable] = cursor;
    }

    public Cursor ChangeMode(PlaybackMode mode, int roundIndex, int node, int viewSeat) {
        Remember(roundIndex, node, viewSeat);
        if (Mode == mode) return Position(ActiveTable, mode == PlaybackMode.Live);
        if (mode == PlaybackMode.Compare) {
            foreach (string table in TableIds)
                if (Ready(table) && !independentPositions.ContainsKey(table)) independentPositions[table] = Position(table);
        }
        Mode = mode;
        var cursor = mode == PlaybackMode.History ? IndependentPosition(ActiveTable) : Position(ActiveTable, mode == PlaybackMode.Live);
        if (cursor != null) positions[ActiveTable] = cursor;
        return cursor;
    }

    private Cursor IndependentPosition(string table) {
        if (independentPositions.TryGetValue(table, out var cursor)) return ClampCursor(table, cursor);
        return FirstPosition(table);
    }

    public void RestorePlaybackFrom(MatchRecordSession previous) {
        if (previous == null || previous.MatchId != MatchId) throw new ArgumentException("Different match");
        foreach (var pair in previous.positions) if (Ready(pair.Key)) positions[pair.Key] = ClampCursor(pair.Key, pair.Value);
        foreach (var pair in previous.independentPositions) if (Ready(pair.Key)) independentPositions[pair.Key] = ClampCursor(pair.Key, pair.Value);
    }

    private Cursor ClampCursor(string table, Cursor cursor) {
        var meta = Table(table)?["round_meta"]?[cursor.RoundKey];
        return meta?["action_meta"] is JArray actions
            ? new Cursor(cursor.RoundKey, Math.Max(0, Math.Min(cursor.Node, actions.Count)), cursor.ViewSeat) : FirstPosition(table);
    }

    private Cursor FirstPosition(string table) {
        if (!Ready(table)) return null;
        var round = ((JObject)Table(table)["round_meta"]).Properties().OrderBy(p => (int)p.Value["round_index"]).First();
        return new Cursor(round.Name, 0);
    }
    public Cursor Position(string table, bool tail = false) {
        if (!Ready(table)) return null;
        var meta = (JObject)Table(table)["round_meta"];
        if (!tail && positions.TryGetValue(table, out var remembered) && meta[remembered.RoundKey] != null)
            return ClampCursor(table, remembered);
        var round = tail ? meta.Properties().OrderBy(p => (int)p.Value["round_index"]).Last()
                         : meta.Properties().OrderBy(p => (int)p.Value["round_index"]).First();
        return new Cursor(round.Name, tail ? ((JArray)round.Value["action_meta"]).Count : 0);
    }

    public JObject AnchorAt(int roundIndex, int node, string phase, int viewSeat) {
        if (viewSeat < 0 || viewSeat > 3 || (phase != "before_draw" && phase != "after_draw")) return null;
        var meta = Table(ActiveTable)?["round_meta"]?["round_index_" + roundIndex];
        if ((string)meta?["alignment_status"] == "unavailable") return null;
        if (!(meta?["action_meta"] is JArray actions)) return null;
        if (phase == "before_draw" && node >= 0 && node < actions.Count && (int?)actions[node]["actor_original_seat"] == viewSeat
            && (string)actions[node]["wall_ref_basis"] == "unavailable") return null;
        if (phase == "before_draw" && node >= 0 && node < actions.Count && (int?)actions[node]["actor_original_seat"] == viewSeat && actions[node]["wall_ref"] is JObject next)
            return (JObject)next.DeepClone();
        for (int i = Math.Min(node, actions.Count) - 1; i >= 0; i--) {
            if ((int?)actions[i]["actor_original_seat"] != viewSeat) continue;
            if (actions[i]["wall_ref"] is JObject anchor) return (JObject)anchor.DeepClone();
            // A missing draw origin cannot be replaced by an older draw of this player.
            if ((string)actions[i]["wall_ref_basis"] == "unavailable") return null;
        }
        var initial = meta["initial_anchors"] as JArray;
        return initial?.FirstOrDefault(a => (int?)a["actor_original_seat"] == viewSeat)?["wall_ref"] is JObject first ? (JObject)first.DeepClone() : null;
    }

    public static bool SameOrigin(JToken a, JToken b) => a is JObject && b is JObject &&
        new[] { "board_instance_id", "zone", "source_original_seat", "offset" }.All(k => JToken.DeepEquals(a[k], b[k]));

    public static Location Locate(JObject table, JObject origin, string phase = "after_draw") {
        if (origin == null) return new Location("ORIGIN_UNAVAILABLE");
        if (phase != "after_draw" && phase != "before_draw") return new Location("PHASE_UNAVAILABLE");
        if (table == null || (string)table["record_state"] == "missing") return new Location("TABLE_MISSING");
        var candidates = new List<Location>();
        string absence = null;
        foreach (var round in ((JObject)table["round_meta"]).Properties()) {
            var meta = round.Value;
            if ((string)meta["board_instance_id"] != (string)origin["board_instance_id"]) continue;
            if ((string)meta["alignment_status"] == "unavailable") return new Location("ORIGIN_UNAVAILABLE");
            foreach (var anchor in (JArray)meta["initial_anchors"] ?? new JArray()) {
                if (!SameOrigin(anchor["wall_ref"], origin)) continue;
                if (phase != "after_draw") return new Location("PHASE_UNAVAILABLE");
                candidates.Add(new Location("EXACT") { RoundKey = round.Name, Node = 0, ActorOriginalSeat = (int?)anchor["actor_original_seat"] });
            }
            var actions = (JArray)meta["action_meta"];
            for (int i = 0; i < actions.Count; i++) {
                if (SameOrigin(actions[i]["wall_ref"], origin)) candidates.Add(new Location("EXACT") {
                    RoundKey = round.Name, Node = i + (phase == "after_draw" ? 1 : 0), ActorOriginalSeat = (int?)actions[i]["actor_original_seat"] });
            }
            bool ended = ((JArray)table["payload"]["game_round"][round.Name]["action_ticks"]).Any(t => (string)t[0] == "end");
            absence = (string)meta["alignment_status"] != "exact" ? "ORIGIN_UNAVAILABLE" :
                ended || (string)table["record_state"] == "complete" ? "NOT_CONSUMED" : "NOT_VISIBLE_YET";
        }
        if (candidates.Count > 1) return new Location("DATA_CONFLICT");
        return candidates.Count == 1 ? candidates[0] : new Location(absence ?? ((string)table["record_state"] == "complete" ? "INSTANCE_UNVERIFIED" : "NOT_VISIBLE_YET"));
    }

    public Location Switch(string target, int currentRound, int currentNode, bool compare, string phase, int viewSeat) {
        if (!Ready(target)) return new Location("TABLE_MISSING");
        Remember(currentRound, currentNode, viewSeat);
        Location result;
        if (Mode != PlaybackMode.Live && compare) {
            result = Locate(Table(target), AnchorAt(currentRound, currentNode, phase, viewSeat), phase);
            if (!result.Exact) return result;
            if (Mode != PlaybackMode.Compare) ChangeMode(PlaybackMode.Compare, currentRound, currentNode, viewSeat);
            Mode = PlaybackMode.Compare;
            result.ViewSeat = viewSeat;
        } else {
            var cursor = Mode == PlaybackMode.Live ? Position(target, true) : IndependentPosition(target);
            result = new Location("EXACT") { RoundKey = cursor.RoundKey, Node = cursor.Node, ViewSeat = cursor.ViewSeat < 0 ? viewSeat : cursor.ViewSeat };
        }
        ActiveTable = target;
        positions[target] = new Cursor(result.RoundKey, result.Node, result.ViewSeat);
        return result;
    }

    public bool FinalStreamsApplied {
        get {
            if (!IsLiveSession) return true;
            if (!(Manifest?["final_streams"] is JObject final)) return false;
            foreach (var pair in final.Properties())
                if (!streams.TryGetValue(pair.Name, out var stream) || stream.Epoch != (string)pair.Value["stream_epoch"] || stream.Sequence < (long)pair.Value["applied_seq"]) return false;
            return true;
        }
    }

    public JObject ExportRecord() {
        if (!FinalStreamsApplied) throw new InvalidOperationException("Final table streams have not arrived");
        var result = (JObject)Manifest.DeepClone();
        result["tables"] = new JObject(tables.Where(p => p.Value != null).Select(p => new JProperty(p.Key, p.Value.DeepClone())));
        if (!IsLiveSession) return result;
        result["format"] = "open_mahjong.match_record"; result["schema_version"] = 2;
        result["completeness"] = tables.Count == 2 && tables.Values.All(t => (string)t?["record_state"] == "complete") ? "complete" : "partial";
        foreach (string table in new[] { "open", "closed" }) if (result["tables"][table] == null)
            result["tables"][table] = new JObject { ["table_id"] = table, ["table_record_id"] = null,
                ["record_state"] = "missing", ["payload"] = null, ["round_meta"] = new JObject(), ["players"] = new JArray() };
        return result;
    }

    public static string Explain(string status) {
        switch (status) {
            case "NOT_VISIBLE_YET": return "对桌尚未公布此牌山位置，请稍后重试";
            case "NOT_CONSUMED": return "对桌本副已结束，未摸到这个牌山位置";
            case "TABLE_MISSING": return "对桌牌谱暂不可用";
            case "INSTANCE_UNVERIFIED": return "无法确认两桌属于同一次发牌";
            case "PHASE_UNAVAILABLE": return "庄家起手第十四张只有摸牌后位置";
            case "DATA_CONFLICT": return "牌山来源记录冲突，不能定位";
            default: return "当前视角尚无可验证的摸牌位置";
        }
    }
}
